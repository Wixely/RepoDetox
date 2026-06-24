namespace RepoDetox;

/// <summary>
/// Formats a <see cref="RepositoryScanResult"/> into plain text lines, shared by the CLI
/// console output and the GUI log so both present analysis identically.
/// </summary>
public static class ScanReportFormatter
{
    /// <summary>Builds the multi-line scan summary (no trailing console side effects).</summary>
    public static IReadOnlyList<string> Describe(RepositoryScanResult result)
    {
        var lines = new List<string>
        {
            $"Repository: {result.RepositoryRoot}",
            $"Current branch: {result.CurrentBranch}",
            $"Tracked files on current branch: {result.CurrentTrackedFileCount}",
            $"Deleted paths seen in history: {result.DeletedPathCount}",
            $"Paths still live on refs: {result.LivePathCount}",
            $"Paths eligible for removal: {result.HistoricalOnlyPaths.Count}",
            string.Empty,
        };

        if (result.HistoricalOnlyPaths.Count == 0)
        {
            lines.Add("No deleted paths were found that are absent from all live refs.");
            return lines;
        }

        foreach (var entry in result.HistoricalOnlyPathEntries)
        {
            lines.Add($"{entry.Path}{FormatHistoricalSize(entry.MaxSizeBytes)}");
        }

        lines.Add(string.Empty);
        lines.Add($"Estimated space saved after cleaning: {FormatAggregateSize(result.EstimatedSavingsBytes)}");
        return lines;
    }

    /// <summary>Formats a single historical path's maximum size as a parenthetical suffix.</summary>
    public static string FormatHistoricalSize(long? sizeBytes)
    {
        if (sizeBytes is null)
        {
            return string.Empty;
        }

        var sizeInMegabytes = sizeBytes.Value / (1024d * 1024d);
        return $" (max {sizeInMegabytes:F2} MB)";
    }

    /// <summary>Formats an aggregate byte count in megabytes, or "unknown" when not measurable.</summary>
    public static string FormatAggregateSize(long? sizeBytes)
    {
        if (sizeBytes is null)
        {
            return "unknown";
        }

        var sizeInMegabytes = sizeBytes.Value / (1024d * 1024d);
        return $"{sizeInMegabytes:F2} MB";
    }
}
