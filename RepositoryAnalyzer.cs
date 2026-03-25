using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace RepoDetox;

public sealed class RepositoryAnalyzer(
    GitCommandRunner gitCommandRunner,
    ILogger<RepositoryAnalyzer> logger)
{
    public async Task<RepositoryScanResult> AnalyzeAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var workingPath = ResolveWorkingPath(repositoryPath);
        logger.LogInformation("Starting repository analysis for {RepositoryPath}.", workingPath);

        logger.LogInformation("Resolving repository root for {RepositoryPath}.", workingPath);
        var repositoryRoot = await ResolveRepositoryRootAsync(workingPath, cancellationToken);

        logger.LogInformation("Reading current branch for {RepositoryRoot}.", repositoryRoot);
        var currentBranch = await GetCurrentBranchAsync(repositoryRoot, cancellationToken);

        logger.LogInformation("Loading tracked files from the current branch for {RepositoryRoot}.", repositoryRoot);
        var currentFiles = await GetCurrentFilesAsync(repositoryRoot, cancellationToken);

        logger.LogInformation("Scanning all historical objects for {RepositoryRoot}. This can take a while on large repositories.", repositoryRoot);
        var historicalPathObjectIds = await GetHistoricalPathObjectIdsAsync(repositoryRoot, cancellationToken);

        logger.LogInformation("Resolving historical file sizes for {RepositoryRoot}.", repositoryRoot);
        var historicalOnlyPathEntries = await BuildHistoricalOnlyPathEntriesAsync(
            repositoryRoot,
            currentFiles,
            historicalPathObjectIds,
            cancellationToken);

        logger.LogInformation(
            "Analyzed {RepositoryRoot}. Branch={CurrentBranch}, CurrentFiles={CurrentFiles}, HistoricalOnlyPaths={HistoricalOnlyPaths}.",
            repositoryRoot,
            currentBranch,
            currentFiles.Count,
            historicalOnlyPathEntries.Count);

        logger.LogInformation(
            "Repository analysis for {RepositoryRoot} completed in {ElapsedSeconds:0.00}s.",
            repositoryRoot,
            stopwatch.Elapsed.TotalSeconds);

        return new RepositoryScanResult(
            repositoryRoot,
            currentBranch,
            currentFiles.Count,
            historicalPathObjectIds.Count,
            historicalOnlyPathEntries);
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

    private async Task<Dictionary<string, HashSet<string>>> GetHistoricalPathObjectIdsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["rev-list", "--objects", "--all"],
            cancellationToken);

        var historicalPathObjectIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        using var reader = new StringReader(result.StandardOutput);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var firstSeparatorIndex = line.IndexOf(' ');
            if (firstSeparatorIndex < 0 || firstSeparatorIndex == line.Length - 1)
            {
                continue;
            }

            var objectId = line[..firstSeparatorIndex];
            var path = line[(firstSeparatorIndex + 1)..];

            if (!historicalPathObjectIds.TryGetValue(path, out var objectIds))
            {
                objectIds = new HashSet<string>(StringComparer.Ordinal);
                historicalPathObjectIds[path] = objectIds;
            }

            objectIds.Add(objectId);
        }

        return historicalPathObjectIds;
    }

    private async Task<IReadOnlyList<HistoricalPathEntry>> BuildHistoricalOnlyPathEntriesAsync(
        string repositoryRoot,
        HashSet<string> currentFiles,
        Dictionary<string, HashSet<string>> historicalPathObjectIds,
        CancellationToken cancellationToken)
    {
        var historicalOnlyPathEntries = historicalPathObjectIds
            .Where(entry => !currentFiles.Contains(entry.Key))
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (historicalOnlyPathEntries.Length == 0)
        {
            return Array.Empty<HistoricalPathEntry>();
        }

        var objectSizes = await GetObjectSizesAsync(
            repositoryRoot,
            historicalOnlyPathEntries.SelectMany(entry => entry.Value).Distinct(StringComparer.Ordinal),
            cancellationToken);

        return historicalOnlyPathEntries
            .Select(entry =>
            {
                long? maxSizeBytes = null;

                foreach (var objectId in entry.Value)
                {
                    if (!objectSizes.TryGetValue(objectId, out var sizeBytes))
                    {
                        continue;
                    }

                    maxSizeBytes = maxSizeBytes is null
                        ? sizeBytes
                        : Math.Max(maxSizeBytes.Value, sizeBytes);
                }

                return new HistoricalPathEntry(entry.Key, maxSizeBytes);
            })
            .ToArray();
    }

    private async Task<Dictionary<string, long>> GetObjectSizesAsync(
        string repositoryRoot,
        IEnumerable<string> objectIds,
        CancellationToken cancellationToken)
    {
        var objectIdList = objectIds.ToArray();
        if (objectIdList.Length == 0)
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        var standardInput = string.Join(Environment.NewLine, objectIdList) + Environment.NewLine;
        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["cat-file", "--batch-check=%(objectname) %(objecttype) %(objectsize)"],
            cancellationToken,
            standardInput: standardInput);

        var objectSizes = new Dictionary<string, long>(StringComparer.Ordinal);
        using var reader = new StringReader(result.StandardOutput);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var separatorIndex = line.IndexOf(' ');
            if (separatorIndex < 0)
            {
                continue;
            }

            var secondSeparatorIndex = line.IndexOf(' ', separatorIndex + 1);
            if (secondSeparatorIndex < 0 || secondSeparatorIndex == line.Length - 1)
            {
                continue;
            }

            var objectId = line[..separatorIndex];
            var objectType = line[(separatorIndex + 1)..secondSeparatorIndex];
            var objectSizeText = line[(secondSeparatorIndex + 1)..];

            if (!string.Equals(objectType, "blob", StringComparison.Ordinal) ||
                !long.TryParse(objectSizeText, out var objectSize))
            {
                continue;
            }

            objectSizes[objectId] = objectSize;
        }

        return objectSizes;
    }

    private static string ResolveWorkingPath(string repositoryPath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(repositoryPath) ? "." : repositoryPath);
}
