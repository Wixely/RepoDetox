using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryAnalyzer(
    GitCommandRunner gitCommandRunner,
    ILogger<RepositoryAnalyzer> logger)
{
    public async Task<RepositoryScanResult> AnalyzeAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var workingPath = ResolveWorkingPath(repositoryPath);
        var repositoryRoot = await ResolveRepositoryRootAsync(workingPath, cancellationToken);
        var currentBranch = await GetCurrentBranchAsync(repositoryRoot, cancellationToken);
        var currentFiles = await GetCurrentFilesAsync(repositoryRoot, cancellationToken);
        var historicalPaths = await GetHistoricalPathsAsync(repositoryRoot, cancellationToken);
        var historicalOnlyPaths = historicalPaths
            .Except(currentFiles, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        logger.LogInformation(
            "Analyzed {RepositoryRoot}. Branch={CurrentBranch}, CurrentFiles={CurrentFiles}, HistoricalOnlyPaths={HistoricalOnlyPaths}.",
            repositoryRoot,
            currentBranch,
            currentFiles.Count,
            historicalOnlyPaths.Length);

        return new RepositoryScanResult(
            repositoryRoot,
            currentBranch,
            currentFiles.Count,
            historicalPaths.Count,
            historicalOnlyPaths);
    }

    private async Task<string> ResolveRepositoryRootAsync(string workingPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workingPath))
        {
            throw new DirectoryNotFoundException($"The repository path '{workingPath}' does not exist.");
        }

        var result = await gitCommandRunner.RunAsync(
            workingPath,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{workingPath}' is not a git repository.");
        }

        return result.StandardOutput.Trim();
    }

    private async Task<string> GetCurrentBranchAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var symbolicRef = await gitCommandRunner.RunAsync(
            repositoryRoot,
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            cancellationToken);

        if (symbolicRef.ExitCode == 0 && !string.IsNullOrWhiteSpace(symbolicRef.StandardOutput))
        {
            return symbolicRef.StandardOutput.Trim();
        }

        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["rev-parse", "--abbrev-ref", "HEAD"],
            cancellationToken);

        return result.StandardOutput.Trim();
    }

    private async Task<HashSet<string>> GetCurrentFilesAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["ls-files", "-z"],
            cancellationToken);

        var values = result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new HashSet<string>(values, StringComparer.Ordinal);
    }

    private async Task<HashSet<string>> GetHistoricalPathsAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["rev-list", "--objects", "--all"],
            cancellationToken);

        var historicalPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in result.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf(' ');

            if (separatorIndex < 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var path = line[(separatorIndex + 1)..];
            historicalPaths.Add(path);
        }

        return historicalPaths;
    }

    private static string ResolveWorkingPath(string repositoryPath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(repositoryPath) ? "." : repositoryPath);
}
