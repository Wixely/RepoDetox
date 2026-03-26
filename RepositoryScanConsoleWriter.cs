namespace RepoDetox;

public static class RepositoryScanConsoleWriter
{
    public static void Write(RepositoryScanResult result)
    {
        Console.WriteLine($"Repository: {result.RepositoryRoot}");
        Console.WriteLine($"Current branch: {result.CurrentBranch}");
        Console.WriteLine($"Tracked files on current branch: {result.CurrentTrackedFileCount}");
        Console.WriteLine($"Deleted paths seen in history: {result.DeletedPathCount}");
        Console.WriteLine($"Paths still live on refs: {result.LivePathCount}");
        Console.WriteLine($"Paths eligible for removal: {result.HistoricalOnlyPaths.Count}");
        Console.WriteLine();

        if (result.HistoricalOnlyPaths.Count == 0)
        {
            Console.WriteLine("No deleted paths were found that are absent from all live refs.");
            return;
        }

        foreach (var entry in result.HistoricalOnlyPathEntries)
        {
            Console.WriteLine($"{entry.Path}{FormatHistoricalSize(entry.MaxSizeBytes)}");
        }

        Console.WriteLine();
        Console.WriteLine($"Estimated space saved after cleaning: {FormatAggregateSize(result.EstimatedSavingsBytes)}");
    }

    private static string FormatHistoricalSize(long? sizeBytes)
    {
        if (sizeBytes is null)
        {
            return string.Empty;
        }

        var sizeInMegabytes = sizeBytes.Value / (1024d * 1024d);
        return $" (max {sizeInMegabytes:F2} MB)";
    }

    private static string FormatAggregateSize(long? sizeBytes)
    {
        if (sizeBytes is null)
        {
            return "unknown";
        }

        var sizeInMegabytes = sizeBytes.Value / (1024d * 1024d);
        return $"{sizeInMegabytes:F2} MB";
    }
}
