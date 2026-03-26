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
        string? standardInput = null,
        bool echoOutputToConsole = false)
    {
        return await RunProcessAsync(
            "git",
            workingDirectory,
            arguments,
            cancellationToken,
            environmentVariables,
            standardInput,
            "Git could not be started. Ensure git is installed and available on PATH.",
            "git",
            echoOutputToConsole);
    }

    public async Task<GitCommandResult> RunCheckedAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? standardInput = null,
        bool echoOutputToConsole = false)
    {
        var result = await RunAsync(workingDirectory, arguments, cancellationToken, environmentVariables, standardInput, echoOutputToConsole);

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

    public Task<GitCommandResult> RunExternalAsync(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? standardInput = null,
        string? startupErrorMessage = null,
        string? commandDisplayName = null,
        bool echoOutputToConsole = false) =>
        RunProcessAsync(
            fileName,
            workingDirectory,
            arguments,
            cancellationToken,
            environmentVariables,
            standardInput,
            startupErrorMessage ?? $"The command '{fileName}' could not be started.",
            commandDisplayName ?? fileName,
            echoOutputToConsole);

    public async Task<GitCommandResult> RunExternalCheckedAsync(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? standardInput = null,
        string? startupErrorMessage = null,
        string? commandDisplayName = null,
        bool echoOutputToConsole = false)
    {
        var result = await RunExternalAsync(
            fileName,
            workingDirectory,
            arguments,
            cancellationToken,
            environmentVariables,
            standardInput,
            startupErrorMessage,
            commandDisplayName,
            echoOutputToConsole);

        if (result.ExitCode == 0)
        {
            return result;
        }

        var errorText = string.IsNullOrWhiteSpace(result.StandardError)
            ? "The command returned a non-zero exit code without stderr output."
            : result.StandardError.Trim();

        throw new InvalidOperationException(
            $"{commandDisplayName ?? fileName} {FormatArguments(arguments)} failed with exit code {result.ExitCode}. {errorText}");
    }

    private async Task<GitCommandResult> RunProcessAsync(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        string? standardInput,
        string startupErrorMessage,
        string commandDisplayName,
        bool echoOutputToConsole)
    {
        logger.LogDebug("Executing {Command} {Arguments} in {WorkingDirectory}.", commandDisplayName, FormatArguments(arguments), workingDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput is not null,
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
            throw new InvalidOperationException(startupErrorMessage, ex);
        }

        var standardOutputTask = ReadStreamAsync(
            process.StandardOutput,
            echoOutputToConsole ? Console.Out : null,
            cancellationToken);
        var standardErrorTask = ReadStreamAsync(
            process.StandardError,
            echoOutputToConsole ? Console.Error : null,
            cancellationToken);
        Task? standardInputTask = null;

        if (standardInput is not null)
        {
            standardInputTask = WriteStandardInputAsync(process, standardInput, cancellationToken);
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
        TextWriter? mirrorWriter,
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

            if (mirrorWriter is not null)
            {
                await mirrorWriter.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await mirrorWriter.FlushAsync(cancellationToken);
            }
        }

        return builder.ToString();
    }
}
