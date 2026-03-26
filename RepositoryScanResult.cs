namespace RepoDetox;

public sealed record RepositoryScanResult(
    string RepositoryRoot,
    string CurrentBranch,
    int CurrentTrackedFileCount,
    int DeletedPathCount,
    int LivePathCount,
    IReadOnlyList<HistoricalPathEntry> HistoricalOnlyPathEntries)
{
    public IReadOnlyList<string> HistoricalOnlyPaths { get; } =
        HistoricalOnlyPathEntries.Select(entry => entry.Path).ToArray();

    public long? EstimatedSavingsBytes { get; } =
        HistoricalOnlyPathEntries.All(entry => entry.MaxSizeBytes is not null)
            ? HistoricalOnlyPathEntries.Sum(entry => entry.MaxSizeBytes ?? 0)
            : null;
}
