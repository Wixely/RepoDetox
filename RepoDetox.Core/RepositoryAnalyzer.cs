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

        logger.LogInformation("Scanning deleted paths across repository history for {RepositoryRoot}.", repositoryRoot);
        var deletedPaths = await GetDeletedPathsAsync(repositoryRoot, cancellationToken);

        logger.LogInformation("Loading paths that still exist on live refs for {RepositoryRoot}.", repositoryRoot);
        var livePaths = await GetLivePathsAcrossRefsAsync(repositoryRoot, cancellationToken);

        logger.LogInformation("Resolving historical file sizes for deleted paths in {RepositoryRoot}.", repositoryRoot);
        var historicalOnlyPathEntries = await BuildHistoricalOnlyPathEntriesAsync(
            repositoryRoot,
            deletedPaths,
            livePaths,
            cancellationToken);

        logger.LogInformation(
            "Analyzed {RepositoryRoot}. Branch={CurrentBranch}, CurrentFiles={CurrentFiles}, DeletedPaths={DeletedPaths}, LivePaths={LivePaths}, RemovablePaths={HistoricalOnlyPaths}.",
            repositoryRoot,
            currentBranch,
            currentFiles.Count,
            deletedPaths.Count,
            livePaths.Count,
            historicalOnlyPathEntries.Count);

        logger.LogInformation(
            "Repository analysis for {RepositoryRoot} completed in {ElapsedSeconds:0.00}s.",
            repositoryRoot,
            stopwatch.Elapsed.TotalSeconds);

        return new RepositoryScanResult(
            repositoryRoot,
            currentBranch,
            currentFiles.Count,
            deletedPaths.Count,
            livePaths.Count,
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

    private async Task<HashSet<string>> GetDeletedPathsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["log", "--all", "--diff-filter=D", "--name-only", "--format=", "-z", "-M"],
            cancellationToken);

        return SplitNullSeparatedValues(result.StandardOutput);
    }

    private async Task<HashSet<string>> GetLivePathsAcrossRefsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var refsResult = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["for-each-ref", "--format=%(refname)"],
            cancellationToken);

        var refs = SplitLines(refsResult.StandardOutput);

        var livePaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var refName in refs)
        {
            var treeResult = await gitCommandRunner.RunCheckedAsync(
                repositoryRoot,
                ["ls-tree", "-r", "--name-only", "-z", "--full-tree", $"{refName}^{{tree}}"],
                cancellationToken);

            foreach (var path in SplitNullSeparatedValues(treeResult.StandardOutput))
            {
                livePaths.Add(path);
            }
        }

        return livePaths;
    }

    private async Task<IReadOnlyList<HistoricalPathEntry>> BuildHistoricalOnlyPathEntriesAsync(
        string repositoryRoot,
        HashSet<string> deletedPaths,
        HashSet<string> livePaths,
        CancellationToken cancellationToken)
    {
        var candidatePaths = deletedPaths
            .Where(path => !livePaths.Contains(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidatePaths.Length == 0)
        {
            return Array.Empty<HistoricalPathEntry>();
        }

        var historicalPathObjectIds = await GetHistoricalPathObjectIdsAsync(
            repositoryRoot,
            candidatePaths,
            cancellationToken);

        var objectSizes = await GetObjectSizesAsync(
            repositoryRoot,
            historicalPathObjectIds.Values.SelectMany(entry => entry).Distinct(StringComparer.Ordinal),
            cancellationToken);

        return candidatePaths
            .Select(path =>
            {
                long? maxSizeBytes = null;
                historicalPathObjectIds.TryGetValue(path, out var objectIds);

                foreach (var objectId in objectIds ?? [])
                {
                    if (!objectSizes.TryGetValue(objectId, out var sizeBytes))
                    {
                        continue;
                    }

                    maxSizeBytes = maxSizeBytes is null
                        ? sizeBytes
                        : Math.Max(maxSizeBytes.Value, sizeBytes);
                }

                return new HistoricalPathEntry(path, maxSizeBytes);
            })
            .ToArray();
    }

    private async Task<Dictionary<string, HashSet<string>>> GetHistoricalPathObjectIdsAsync(
        string repositoryRoot,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var historicalPathObjectIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var batch in Batch(paths, 256))
        {
            var arguments = new List<string> { "rev-list", "--objects", "--all", "--" };
            arguments.AddRange(batch.Select(ToLiteralPathspec));

            var result = await gitCommandRunner.RunCheckedAsync(
                repositoryRoot,
                arguments,
                cancellationToken);

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
        }

        return historicalPathObjectIds;
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

    private static HashSet<string> SplitNullSeparatedValues(string value) =>
        new(
            value.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);

    private static string[] SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<IReadOnlyList<T>> Batch<T>(IReadOnlyList<T> items, int size)
    {
        for (var index = 0; index < items.Count; index += size)
        {
            var count = Math.Min(size, items.Count - index);
            var batch = new T[count];
            for (var offset = 0; offset < count; offset++)
            {
                batch[offset] = items[index + offset];
            }

            yield return batch;
        }
    }

    private static string ToLiteralPathspec(string path) =>
        $":(literal){path}";
}
