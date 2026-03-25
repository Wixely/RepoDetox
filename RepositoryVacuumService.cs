using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryVacuumService(
    GitCommandRunner gitCommandRunner,
    RepositoryAnalyzer repositoryAnalyzer,
    ILogger<RepositoryVacuumService> logger)
{
    public async Task<VacuumResult> VacuumAsync(
        VacuumOptions options,
        CancellationToken cancellationToken = default)
    {
        var analysis = await repositoryAnalyzer.AnalyzeAsync(options.RepositoryPath, cancellationToken);

        if (analysis.HistoricalOnlyPaths.Count == 0)
        {
            return new VacuumResult(false, $"No historical-only paths were found in {analysis.RepositoryRoot}.");
        }

        await EnsureRepositoryIsCleanAsync(analysis.RepositoryRoot, cancellationToken);
        await EnsureGitFilterRepoIsAvailableAsync(analysis.RepositoryRoot, cancellationToken);

        if (!options.Force && !ConfirmRewrite(analysis))
        {
            logger.LogWarning("Vacuum command was cancelled by the operator for {RepositoryRoot}.", analysis.RepositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. Removing {Count} historical-only paths.",
            analysis.RepositoryRoot,
            analysis.HistoricalOnlyPaths.Count);

        var filterRepoArguments = new List<string>
        {
            "filter-repo",
            "--force",
            "--invert-paths"
        };

        foreach (var path in analysis.HistoricalOnlyPaths)
        {
            filterRepoArguments.Add("--path");
            filterRepoArguments.Add(path);
        }

        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, filterRepoArguments, cancellationToken);
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        var message =
            $"Removed {analysis.HistoricalOnlyPaths.Count} path(s) from history in {analysis.RepositoryRoot} " +
            $"and compacted the repository.";

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

    private async Task EnsureGitFilterRepoIsAvailableAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunAsync(
            repositoryRoot,
            ["filter-repo", "--version"],
            cancellationToken);

        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "The 'git filter-repo' command is required for vacuum. Install it before running this command.");
    }

    private static bool ConfirmRewrite(RepositoryScanResult analysis)
    {
        Console.WriteLine("Warning: this will rewrite git history and permanently remove paths from every ref.");
        Console.WriteLine($"Repository: {analysis.RepositoryRoot}");
        Console.WriteLine($"Current branch: {analysis.CurrentBranch}");
        Console.WriteLine($"Paths to remove: {analysis.HistoricalOnlyPaths.Count}");
        Console.WriteLine();
        Console.Write("Continue? [y/N]: ");

        var response = Console.ReadLine();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
