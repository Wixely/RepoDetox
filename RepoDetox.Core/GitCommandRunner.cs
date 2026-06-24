using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class GitCommandRunner(ILogger<GitCommandRunner> logger)
{
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? standardInput = null)
    {
        return await RunProcessAsync(
            workingDirectory,
            arguments,
            cancellationToken,
            environmentVariables,
            standardInput);
    }

    public async Task<GitCommandResult> RunCheckedAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? standardInput = null)
    {
        var result = await RunAsync(workingDirectory, arguments, cancellationToken, environmentVariables, standardInput);

        if (result.ExitCode == 0)
        {
            return result;
        }

        var errorText = string.IsNullOrWhiteSpace(result.StandardError)
            ? "Git returned a non-zero exit code without stderr output."
            : result.StandardError.Trim();

        throw new InvalidOperationException(
            $"git {FormatArguments(arguments)} failed with exit code {result.ExitCode}. {errorText}");
    }

    private async Task<GitCommandResult> RunProcessAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        string? standardInput)
    {
        logger.LogDebug("Executing git {Arguments} in {WorkingDirectory}.", FormatArguments(arguments), workingDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Always redirect stdin so the child git never inherits this process's stdin handle.
                // That matters when RepoDetox runs as an MCP server: its stdin is the JSON-RPC pipe,
                // and an inherited handle would otherwise leave git waiting on it.
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (environmentVariables is not null)
        {
            foreach (var environmentVariable in environmentVariables)
            {
                process.StartInfo.Environment[environmentVariable.Key] = environmentVariable.Value ?? string.Empty;
            }
        }

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
            throw new InvalidOperationException(
                "Git could not be started. Ensure git is installed and available on PATH.",
                ex);
        }

        var standardOutputTask = ReadStreamAsync(process.StandardOutput, cancellationToken);
        var standardErrorTask = ReadStreamAsync(process.StandardError, cancellationToken);
        Task? standardInputTask = null;

        if (standardInput is not null)
        {
            standardInputTask = WriteStandardInputAsync(process, standardInput, cancellationToken);
        }
        else
        {
            // No input for this command: close stdin so git sees EOF instead of inheriting/blocking.
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken);

        if (standardInputTask is not null)
        {
            await standardInputTask;
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        return new GitCommandResult(process.ExitCode, standardOutput, standardError);
    }

    private static string FormatArguments(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static async Task WriteStandardInputAsync(
        Process process,
        string standardInput,
        CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();
    }

    private static async Task<string> ReadStreamAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var builder = new System.Text.StringBuilder();

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }
}
