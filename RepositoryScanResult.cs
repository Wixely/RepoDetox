namespace RepoDetox;

public sealed record RepositoryScanResult(
    string RepositoryRoot,
    string CurrentBranch,
    int CurrentTrackedFileCount,
    int HistoricalPathCount,
    IReadOnlyList<string> HistoricalOnlyPaths);
