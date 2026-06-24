using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryVacuumService(
    GitCommandRunner gitCommandRunner,
    RepositoryAnalyzer repositoryAnalyzer,
    FastExportImportPipeline fastExportImportPipeline,
    ILogger<RepositoryVacuumService> logger)
{
    public async Task<VacuumResult> VacuumAsync(
        VacuumRequest request,
        IOperationReporter reporter,
        CancellationToken cancellationToken = default)
    {
        var analysis = await repositoryAnalyzer.AnalyzeAsync(request.RepositoryPath, cancellationToken);

        reporter.Report("Vacuum analysis:");
        foreach (var line in ScanReportFormatter.Describe(analysis))
        {
            reporter.Report(line);
        }

        reporter.Report(string.Empty);

        if (analysis.HistoricalOnlyPaths.Count == 0)
        {
            return new VacuumResult(
                false,
                $"No deleted paths were found in {analysis.RepositoryRoot} that are absent from all live refs. Nothing to rewrite.");
        }

        await EnsureRepositoryIsCleanAsync(analysis.RepositoryRoot, cancellationToken);

        WriteRewriteWarnings(reporter, analysis);

        if (!request.SkipConfirmation
            && !await reporter.ConfirmAsync("Continue with the vacuum history rewrite?", cancellationToken))
        {
            logger.LogWarning("Vacuum command was cancelled by the operator for {RepositoryRoot}.", analysis.RepositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. Removing {Count} deleted historical paths that are absent from all live refs.",
            analysis.RepositoryRoot,
            analysis.HistoricalOnlyPaths.Count);

        reporter.Report("Starting history rewrite...");

        var transform = new VacuumFastExportTransform(analysis.HistoricalOnlyPaths);
        await fastExportImportPipeline.RunAsync(analysis.RepositoryRoot, transform, cancellationToken);

        reporter.Report("Expiring reflogs...");
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);

        reporter.Report("Running git gc...");
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

    private static void WriteRewriteWarnings(IOperationReporter reporter, RepositoryScanResult analysis)
    {
        reporter.Report("Warning: this will rewrite git history to remove files that were deleted and are no longer present on any live ref.");
        reporter.Report($"Repository: {analysis.RepositoryRoot}");
        reporter.Report($"Current branch: {analysis.CurrentBranch}");
        reporter.Report($"Paths to remove: {analysis.HistoricalOnlyPaths.Count}");
        reporter.Report("Warning: rewriting history changes commit hashes. Pushing the rewritten history to an existing remote can reintroduce the old history and create duplicate commit graphs; coordinate a force-push or a fresh remote.");
        reporter.Report(string.Empty);
    }
}
