namespace RepoDetox;

public sealed record RepositoryScanResult(
    string RepositoryRoot,
    string CurrentBranch,
    int CurrentTrackedFileCount,
    int HistoricalPathCount,
    IReadOnlyList<HistoricalPathEntry> HistoricalOnlyPathEntries)
{
    public IReadOnlyList<string> HistoricalOnlyPaths { get; } =
        HistoricalOnlyPathEntries.Select(entry => entry.Path).ToArray();
}
