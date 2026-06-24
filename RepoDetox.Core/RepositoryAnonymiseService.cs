using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryAnonymiseService(
    GitCommandRunner gitCommandRunner,
    FastExportImportPipeline fastExportImportPipeline,
    ILogger<RepositoryAnonymiseService> logger)
{
    public async Task<VacuumResult> AnonymiseAsync(
        AnonymiseRequest request,
        IOperationReporter reporter,
        CancellationToken cancellationToken = default)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync(request.RepositoryPath, cancellationToken);
        var currentBranch = await GetCurrentBranchAsync(repositoryRoot, cancellationToken);
        var description = DescribeTargets(request.NameMode, request.FixedName, request.EmailMode, request.FixedEmail);

        await EnsureRepositoryIsCleanAsync(repositoryRoot, cancellationToken);

        WriteRewriteWarnings(reporter, repositoryRoot, currentBranch, description);

        if (!request.SkipConfirmation
            && !await reporter.ConfirmAsync("Continue with the anonymise history rewrite?", cancellationToken))
        {
            logger.LogWarning("Anonymise command was cancelled by the operator for {RepositoryRoot}.", repositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. Rewriting {Targets}.",
            repositoryRoot,
            description);

        reporter.Report("Starting history rewrite...");

        var transform = new AnonymiseFastExportTransform(
            request.NameMode,
            request.EmailMode,
            request.FixedName,
            request.FixedEmail);
        await fastExportImportPipeline.RunAsync(repositoryRoot, transform, cancellationToken);

        reporter.Report("Expiring reflogs...");
        await gitCommandRunner.RunCheckedAsync(repositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);

        reporter.Report("Running git gc...");
        await gitCommandRunner.RunCheckedAsync(repositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        var message =
            $"Completed history rewrite in {repositoryRoot}: rewrote {description}. " +
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
            "The target repository has uncommitted changes. Commit or stash them before running anonymise.");
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
        string description)
    {
        reporter.Report("Warning: this will rewrite git history to change commit and tag metadata.");
        reporter.Report($"Repository: {repositoryRoot}");
        reporter.Report($"Current branch: {currentBranch}");
        reporter.Report($"Rewrite: {description}");
        reporter.Report("Warning: changing identities rewrites commit hashes and can affect clones, forks, pull requests, signed objects, and tooling that references existing hashes.");
        reporter.Report(string.Empty);
    }

    private static string DescribeTargets(
        IdentityRewriteMode nameMode,
        string? fixedName,
        IdentityRewriteMode emailMode,
        string? fixedEmail)
    {
        var parts = new List<string>();

        if (nameMode == IdentityRewriteMode.Hash)
        {
            parts.Add("usernames (anonymised)");
        }
        else if (nameMode == IdentityRewriteMode.Fixed)
        {
            parts.Add($"usernames (set to '{fixedName}')");
        }

        if (emailMode == IdentityRewriteMode.Hash)
        {
            parts.Add("emails (anonymised)");
        }
        else if (emailMode == IdentityRewriteMode.Fixed)
        {
            parts.Add($"emails (set to '{fixedEmail}')");
        }

        return parts.Count == 0 ? "nothing" : string.Join(" and ", parts);
    }
}
