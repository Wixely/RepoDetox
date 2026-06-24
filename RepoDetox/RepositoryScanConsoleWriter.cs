namespace RepoDetox;

public static class RepositoryScanConsoleWriter
{
    public static void Write(RepositoryScanResult result)
    {
        foreach (var line in ScanReportFormatter.Describe(result))
        {
            Console.WriteLine(line);
        }
    }
}
