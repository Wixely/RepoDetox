using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryAnonymiseService(
    GitCommandRunner gitCommandRunner,
    FastExportImportPipeline fastExportImportPipeline,
    ILogger<RepositoryAnonymiseService> logger)
{
    public async Task<VacuumResult> AnonymiseAsync(
        AnonymiseOptions options,
        CancellationToken cancellationToken = default)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync(options.RepositoryPath, cancellationToken);
        var currentBranch = await GetCurrentBranchAsync(repositoryRoot, cancellationToken);
        var nameMode = options.NameMode;
        var emailMode = options.EmailMode;
        var description = DescribeTargets(nameMode, options.SetName, emailMode, options.SetEmail);

        await EnsureRepositoryIsCleanAsync(repositoryRoot, cancellationToken);

        if (options.Force)
        {
            WriteRewriteWarnings(repositoryRoot, currentBranch, description);
        }
        else if (!ConfirmRewrite(repositoryRoot, currentBranch, description))
        {
            logger.LogWarning("Anonymise command was cancelled by the operator for {RepositoryRoot}.", repositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. Rewriting {Targets}.",
            repositoryRoot,
            description);

        Console.WriteLine("Starting history rewrite...");

        var transform = new AnonymiseFastExportTransform(nameMode, emailMode, options.SetName, options.SetEmail);
        await fastExportImportPipeline.RunAsync(repositoryRoot, transform, cancellationToken);

        Console.WriteLine("Expiring reflogs...");
        await gitCommandRunner.RunCheckedAsync(repositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);

        Console.WriteLine("Running git gc...");
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

    private static bool ConfirmRewrite(
        string repositoryRoot,
        string currentBranch,
        string description)
    {
        WriteRewriteWarnings(repositoryRoot, currentBranch, description);
        Console.Write("Continue? [y/N]: ");

        var response = Console.ReadLine();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteRewriteWarnings(
        string repositoryRoot,
        string currentBranch,
        string description)
    {
        Console.WriteLine("Warning: this will rewrite git history to change commit and tag metadata.");
        Console.WriteLine($"Repository: {repositoryRoot}");
        Console.WriteLine($"Current branch: {currentBranch}");
        Console.WriteLine($"Rewrite: {description}");
        Console.WriteLine("Warning: changing identities rewrites commit hashes and can affect clones, forks, pull requests, signed objects, and tooling that references existing hashes.");
        Console.WriteLine();
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
