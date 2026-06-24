using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace RepoDetox;

/// <summary>
/// Rewrites git history in pure C# by streaming <c>git fast-export</c> through a
/// <see cref="FastExportTransform"/> into <c>git fast-import</c>. Both git processes run
/// concurrently, connected by an in-process pipe, so memory stays bounded on large repos.
/// This replaces the previous dependency on Python's git-filter-repo.
/// </summary>
public sealed class FastExportImportPipeline(ILogger<FastExportImportPipeline> logger)
{
    private static readonly string[] ExportArguments =
    [
        "fast-export",
        "--all",
        "--reencode=no",
        "--signed-tags=strip",
        "--tag-of-filtered-object=rewrite",
        "--fake-missing-tagger",
        "--reference-excluded-parents",
        "--use-done-feature",
        "--mark-tags",
    ];

    private static readonly string[] ImportArguments =
    [
        "fast-import",
        "--force",
        "--quiet",
    ];

    public async Task RunAsync(
        string repositoryRoot,
        FastExportTransform transform,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Starting fast-export/fast-import pipeline in {RepositoryRoot}.", repositoryRoot);

        using var exportProcess = StartGit(ExportArguments, repositoryRoot, redirectStdout: true, redirectStdin: false);
        using var importProcess = StartGit(ImportArguments, repositoryRoot, redirectStdout: true, redirectStdin: true);

        await using var cancellationRegistration = cancellationToken.Register(() =>
        {
            TryKill(exportProcess);
            TryKill(importProcess);
        });

        var exportStandardError = new StringBuilder();
        var importStandardError = new StringBuilder();

        var exportErrorTask = DrainAsync(exportProcess.StandardError, exportStandardError, cancellationToken);
        var importOutputTask = DrainAsync(importProcess.StandardOutput, null, cancellationToken);
        var importErrorTask = DrainAsync(importProcess.StandardError, importStandardError, cancellationToken);

        var sawDone = false;
        Exception? transformException = null;

        var transformTask = Task.Run(
            () =>
            {
                try
                {
                    var reader = new FastExportReader(exportProcess.StandardOutput.BaseStream);
                    var writer = new FastImportWriter(importProcess.StandardInput.BaseStream);
                    sawDone = transform.Process(reader, writer);
                    writer.Flush();
                }
                catch (Exception ex)
                {
                    // A broken pipe here usually means fast-import exited early; its stderr
                    // is captured separately and surfaced as the primary error below.
                    transformException = ex;
                }
                finally
                {
                    try
                    {
                        importProcess.StandardInput.Close();
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Ignored error while closing fast-import stdin.");
                    }
                }
            },
            cancellationToken);

        await transformTask;
        await exportProcess.WaitForExitAsync(cancellationToken);
        await importProcess.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(exportErrorTask, importOutputTask, importErrorTask);

        ThrowIfFailed(
            exportProcess.ExitCode,
            importProcess.ExitCode,
            sawDone,
            exportStandardError.ToString(),
            importStandardError.ToString(),
            transformException);
    }

    private static void ThrowIfFailed(
        int exportExitCode,
        int importExitCode,
        bool sawDone,
        string exportStandardError,
        string importStandardError,
        Exception? transformException)
    {
        if (importExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git fast-import failed with exit code {importExitCode}. {Describe(importStandardError)}",
                transformException);
        }

        if (exportExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git fast-export failed with exit code {exportExitCode}. {Describe(exportStandardError)}",
                transformException);
        }

        if (transformException is not null)
        {
            throw new InvalidOperationException(
                "The history rewrite transform failed while processing the fast-export stream.",
                transformException);
        }

        if (!sawDone)
        {
            throw new InvalidOperationException(
                "git fast-export ended without the expected 'done' marker; the export may have been truncated. " +
                "The repository was not fully rewritten.");
        }
    }

    private static string Describe(string standardError) =>
        string.IsNullOrWhiteSpace(standardError)
            ? "No stderr output was produced."
            : standardError.Trim();

    private Process StartGit(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        bool redirectStdout,
        bool redirectStdin)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = redirectStdout,
                RedirectStandardError = true,
                RedirectStandardInput = redirectStdin,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException(
                "Git could not be started. Ensure git is installed and available on PATH.",
                ex);
        }

        return process;
    }

    private static async Task DrainAsync(StreamReader reader, StringBuilder? sink, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];

        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            if (read == 0)
            {
                return;
            }

            sink?.Append(buffer, 0, read);
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ignored error while terminating a git process during cancellation.");
        }
    }
}
