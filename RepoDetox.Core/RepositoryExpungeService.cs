using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryExpungeService(
    GitCommandRunner gitCommandRunner,
    FastExportImportPipeline fastExportImportPipeline,
    ILogger<RepositoryExpungeService> logger)
{
    public async Task<VacuumResult> ExpungeAsync(
        ExpungeRequest request,
        IOperationReporter reporter,
        CancellationToken cancellationToken = default)
    {
        var secrets = request.Secrets
            .Where(secret => !string.IsNullOrEmpty(secret))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (secrets.Count == 0)
        {
            return new VacuumResult(false, "No secret strings were provided. Nothing to expunge.");
        }

        var repositoryRoot = await ResolveRepositoryRootAsync(request.RepositoryPath, cancellationToken);
        var currentBranch = await GetCurrentBranchAsync(repositoryRoot, cancellationToken);

        await EnsureRepositoryIsCleanAsync(repositoryRoot, cancellationToken);

        // Never echo the secret values themselves — only counts and the replacement token.
        WriteRewriteWarnings(reporter, repositoryRoot, currentBranch, secrets.Count, request.Replacement, request.IncludeMessages);

        if (!request.SkipConfirmation
            && !await reporter.ConfirmAsync("Continue with the expunge history rewrite?", cancellationToken))
        {
            logger.LogWarning("Expunge command was cancelled by the operator for {RepositoryRoot}.", repositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. Replacing {SecretCount} secret string(s). IncludeMessages={IncludeMessages}.",
            repositoryRoot,
            secrets.Count,
            request.IncludeMessages);

        reporter.Report("Starting history rewrite...");

        var transform = new ExpungeFastExportTransform(secrets, request.Replacement, request.IncludeMessages);
        await fastExportImportPipeline.RunAsync(repositoryRoot, transform, cancellationToken);

        // Expunge changes the content of currently-tracked files, so the working tree still holds
        // the secret and now diverges from the rewritten HEAD. Reset it to scrub the working copy.
        reporter.Report("Updating the working tree...");
        await gitCommandRunner.RunCheckedAsync(repositoryRoot, ["reset", "--hard"], cancellationToken);

        reporter.Report("Expiring reflogs...");
        await gitCommandRunner.RunCheckedAsync(repositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);

        reporter.Report("Running git gc...");
        await gitCommandRunner.RunCheckedAsync(repositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        var message =
            $"Completed history rewrite in {repositoryRoot}: replaced {secrets.Count} secret string(s) with '{request.Replacement}' across all history. " +
            "The repository was then compacted. " +
            "Important: the secret was already committed (and may have been pushed), so rotate/revoke it — this only removes it from this repository's history.";

        logger.LogInformation(
            "Completed expunge in {RepositoryRoot}: replaced {SecretCount} secret string(s).",
            repositoryRoot,
            secrets.Count);

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
            "The target repository has uncommitted changes. Commit or stash them before running expunge.");
    }

    private async Task<string> ResolveRepositoryRootAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var workingPath = Path.GetFullPath(string.IsNullOrWhiteSpace(repositoryPath) ? "." : repositoryPath);

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

    private static void WriteRewriteWarnings(
        IOperationReporter reporter,
        string repositoryRoot,
        string currentBranch,
        int secretCount,
        string replacement,
        bool includeMessages)
    {
        var scope = includeMessages ? "file contents and commit/tag messages" : "file contents";
        reporter.Report("Warning: this will rewrite git history to replace secret strings throughout the repository.");
        reporter.Report($"Repository: {repositoryRoot}");
        reporter.Report($"Current branch: {currentBranch}");
        reporter.Report($"Secrets to replace: {secretCount} (values are not shown)");
        reporter.Report($"Replacement token: {replacement}");
        reporter.Report($"Scope: {scope}");
        reporter.Report("Warning: rewriting history changes commit hashes and can affect clones, forks, pull requests, and signed objects.");
        reporter.Report("Note: rotate/revoke the secret regardless — it was already committed and may exist in clones, forks, backups, or CI logs.");
        reporter.Report(string.Empty);
    }
}
