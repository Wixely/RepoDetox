using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryVacuumService(
    GitCommandRunner gitCommandRunner,
    RepositoryAnalyzer repositoryAnalyzer,
    FastExportImportPipeline fastExportImportPipeline,
    ILogger<RepositoryVacuumService> logger)
{
    public async Task<VacuumResult> VacuumAsync(
        VacuumOptions options,
        CancellationToken cancellationToken = default)
    {
        var analysis = await repositoryAnalyzer.AnalyzeAsync(options.RepositoryPath, cancellationToken);

        Console.WriteLine("Vacuum analysis:");
        RepositoryScanConsoleWriter.Write(analysis);
        Console.WriteLine();

        if (analysis.HistoricalOnlyPaths.Count == 0)
        {
            return new VacuumResult(
                false,
                $"No deleted paths were found in {analysis.RepositoryRoot} that are absent from all live refs. Nothing to rewrite.");
        }

        await EnsureRepositoryIsCleanAsync(analysis.RepositoryRoot, cancellationToken);

        if (options.Force)
        {
            WriteRewriteWarnings(analysis);
        }
        else if (!ConfirmRewrite(analysis))
        {
            logger.LogWarning("Vacuum command was cancelled by the operator for {RepositoryRoot}.", analysis.RepositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. Removing {Count} deleted historical paths that are absent from all live refs.",
            analysis.RepositoryRoot,
            analysis.HistoricalOnlyPaths.Count);

        Console.WriteLine("Starting history rewrite...");

        var transform = new VacuumFastExportTransform(analysis.HistoricalOnlyPaths);
        await fastExportImportPipeline.RunAsync(analysis.RepositoryRoot, transform, cancellationToken);

        Console.WriteLine("Expiring reflogs...");
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);

        Console.WriteLine("Running git gc...");
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        var message =
            $"Completed history rewrite in {analysis.RepositoryRoot}: removed {analysis.HistoricalOnlyPaths.Count} deleted path(s) from history. " +
            "The repository was then compacted.";

        logger.LogInformation(message);

        return new VacuumResult(true, message);
    }

    private async Task EnsureRepositoryIsCleanAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["status", "--porcelain"],
            cancellationToken);

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return;
        }

        throw new InvalidOperationException(
            "The target repository has uncommitted changes. Commit or stash them before running vacuum.");
    }

    private static bool ConfirmRewrite(RepositoryScanResult analysis)
    {
        WriteRewriteWarnings(analysis);
        Console.Write("Continue? [y/N]: ");

        var response = Console.ReadLine();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteRewriteWarnings(RepositoryScanResult analysis)
    {
        Console.WriteLine("Warning: this will rewrite git history to remove files that were deleted and are no longer present on any live ref.");
        Console.WriteLine($"Repository: {analysis.RepositoryRoot}");
        Console.WriteLine($"Current branch: {analysis.CurrentBranch}");
        Console.WriteLine($"Paths to remove: {analysis.HistoricalOnlyPaths.Count}");
        Console.WriteLine("Warning: rewriting history changes commit hashes. Pushing the rewritten history to an existing remote can reintroduce the old history and create duplicate commit graphs; coordinate a force-push or a fresh remote.");
        Console.WriteLine();
    }
}
