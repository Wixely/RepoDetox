using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryVacuumService(
    GitCommandRunner gitCommandRunner,
    RepositoryAnalyzer repositoryAnalyzer,
    ILogger<RepositoryVacuumService> logger)
{
    private const string GitFilterRepoScriptName = "git-filter-repo.py";

    public async Task<VacuumResult> VacuumAsync(
        VacuumOptions options,
        CancellationToken cancellationToken = default)
    {
        var analysis = await repositoryAnalyzer.AnalyzeAsync(options.RepositoryPath, cancellationToken);
        var shouldAnonymizeUsers = options.ShouldAnonymizeUsers;
        var shouldAnonymizeEmails = options.ShouldAnonymizeEmails;
        var shouldRemovePaths = analysis.HistoricalOnlyPaths.Count > 0;
        var shouldAnonymize = shouldAnonymizeUsers || shouldAnonymizeEmails;

        if (!shouldRemovePaths && !shouldAnonymize)
        {
            return new VacuumResult(
                false,
                $"No historical-only paths were found in {analysis.RepositoryRoot}. Nothing to rewrite.");
        }

        await EnsureRepositoryIsCleanAsync(analysis.RepositoryRoot, cancellationToken);
        await EnsureGitFilterRepoIsAvailableAsync(analysis.RepositoryRoot, cancellationToken);

        if (options.Force)
        {
            WriteRewriteWarnings(analysis, shouldAnonymizeUsers, shouldAnonymizeEmails);
        }
        else if (!ConfirmRewrite(analysis, shouldAnonymizeUsers, shouldAnonymizeEmails))
        {
            logger.LogWarning("Vacuum command was cancelled by the operator for {RepositoryRoot}.", analysis.RepositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. Removing {Count} historical-only paths. AnonymizeUsers={AnonymizeUsers}. AnonymizeEmails={AnonymizeEmails}.",
            analysis.RepositoryRoot,
            analysis.HistoricalOnlyPaths.Count,
            shouldAnonymizeUsers,
            shouldAnonymizeEmails);

        var filterRepoArguments = BuildFilterRepoArguments(
            analysis.RepositoryRoot,
            analysis,
            shouldAnonymizeUsers,
            shouldAnonymizeEmails);

        await gitCommandRunner.RunExternalCheckedAsync(
            "python",
            analysis.RepositoryRoot,
            filterRepoArguments,
            cancellationToken,
            startupErrorMessage: "Python could not be started. Ensure python is installed and available on PATH.",
            commandDisplayName: GitFilterRepoScriptName);
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        var message = BuildSuccessMessage(
            analysis.RepositoryRoot,
            analysis.HistoricalOnlyPaths.Count,
            shouldAnonymizeUsers,
            shouldAnonymizeEmails);

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
        var scriptPath = ResolveGitFilterRepoScriptPath();
        var result = await gitCommandRunner.RunExternalAsync(
            "python",
            repositoryRoot,
            [scriptPath, "--version"],
            cancellationToken,
            startupErrorMessage: "Python could not be started. Ensure python is installed and available on PATH.",
            commandDisplayName: GitFilterRepoScriptName);

        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The '{GitFilterRepoScriptName}' script is required for vacuum. Place '{GitFilterRepoScriptName}' on PATH, " +
            "install it with 'python -m pip install git-filter-repo', or see https://github.com/newren/git-filter-repo/blob/main/INSTALL.md " +
            "for the upstream installation instructions.");
    }

    private static List<string> BuildFilterRepoArguments(
        string repositoryRoot,
        RepositoryScanResult analysis,
        bool anonymizeUsers,
        bool anonymizeEmails)
    {
        var scriptPath = ResolveGitFilterRepoScriptPath(repositoryRoot);
        var filterRepoArguments = new List<string>
        {
            scriptPath,
            "--force"
        };

        if (analysis.HistoricalOnlyPaths.Count > 0)
        {
            filterRepoArguments.Add("--invert-paths");

            foreach (var path in analysis.HistoricalOnlyPaths)
            {
                filterRepoArguments.Add("--path");
                filterRepoArguments.Add(path);
            }
        }

        if (anonymizeUsers)
        {
            filterRepoArguments.Add("--name-callback");
            filterRepoArguments.Add(BuildAnonymizeUserCallback());
        }

        if (anonymizeEmails)
        {
            filterRepoArguments.Add("--email-callback");
            filterRepoArguments.Add(BuildAnonymizeEmailCallback());
        }

        return filterRepoArguments;
    }

    private static string ResolveGitFilterRepoScriptPath(string? repositoryRoot = null)
    {
        var searchDirectories = new List<string>();

        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            searchDirectories.Add(repositoryRoot);
        }

        searchDirectories.Add(AppContext.BaseDirectory);

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            searchDirectories.AddRange(
                pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (var directory in searchDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(directory, GitFilterRepoScriptName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return GitFilterRepoScriptName;
    }

    private static string BuildSuccessMessage(
        string repositoryRoot,
        int removedPathCount,
        bool anonymizeUsers,
        bool anonymizeEmails)
    {
        var actions = new List<string>();

        if (removedPathCount > 0)
        {
            actions.Add($"removed {removedPathCount} path(s) from history");
        }

        if (anonymizeUsers || anonymizeEmails)
        {
            actions.Add($"anonymized {DescribeAnonymizationTargets(anonymizeUsers, anonymizeEmails)}");
        }

        return $"Completed history rewrite in {repositoryRoot}: {string.Join(" and ", actions)}. The repository was then compacted.";
    }

    private static bool ConfirmRewrite(
        RepositoryScanResult analysis,
        bool anonymizeUsers,
        bool anonymizeEmails)
    {
        WriteRewriteWarnings(analysis, anonymizeUsers, anonymizeEmails);
        Console.Write("Continue? [y/N]: ");

        var response = Console.ReadLine();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteRewriteWarnings(
        RepositoryScanResult analysis,
        bool anonymizeUsers,
        bool anonymizeEmails)
    {
        Console.WriteLine("Warning: this will rewrite git history.");
        Console.WriteLine($"Repository: {analysis.RepositoryRoot}");
        Console.WriteLine($"Current branch: {analysis.CurrentBranch}");
        Console.WriteLine($"Paths to remove: {analysis.HistoricalOnlyPaths.Count}");

        if (anonymizeUsers || anonymizeEmails)
        {
            Console.WriteLine($"Anonymize: {DescribeAnonymizationTargets(anonymizeUsers, anonymizeEmails)}");
            Console.WriteLine("Warning: anonymizing identities changes commit hashes and can affect clones, forks, pull requests, signed objects, and tooling that references existing hashes.");
        }

        Console.WriteLine();
    }

    private static string DescribeAnonymizationTargets(bool anonymizeUsers, bool anonymizeEmails) =>
        anonymizeUsers && anonymizeEmails
            ? "usernames and emails"
            : anonymizeUsers
                ? "usernames"
                : "emails";

    private static string BuildAnonymizeUserCallback() =>
        "import hashlib; return b\"anonymous-user-\" + hashlib.sha256(name).hexdigest()[:12].encode(\"ascii\")";

    private static string BuildAnonymizeEmailCallback() =>
        "import hashlib; return b\"anonymous-email-\" + hashlib.sha256(email).hexdigest()[:12].encode(\"ascii\") + b\"@example.invalid\"";
}
