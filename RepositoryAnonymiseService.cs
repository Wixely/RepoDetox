using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryAnonymiseService(
    GitCommandRunner gitCommandRunner,
    ILogger<RepositoryAnonymiseService> logger)
{
    private const string GitFilterRepoScriptName = "git-filter-repo.py";

    public async Task<VacuumResult> AnonymiseAsync(
        AnonymiseOptions options,
        CancellationToken cancellationToken = default)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync(options.RepositoryPath, cancellationToken);
        var currentBranch = await GetCurrentBranchAsync(repositoryRoot, cancellationToken);
        var shouldAnonymiseUsers = options.ShouldAnonymiseUsers;
        var shouldAnonymiseEmails = options.ShouldAnonymiseEmails;

        await EnsureRepositoryIsCleanAsync(repositoryRoot, cancellationToken);
        await EnsureGitFilterRepoIsAvailableAsync(repositoryRoot, cancellationToken);

        if (options.Force)
        {
            WriteRewriteWarnings(repositoryRoot, currentBranch, shouldAnonymiseUsers, shouldAnonymiseEmails);
        }
        else if (!ConfirmRewrite(repositoryRoot, currentBranch, shouldAnonymiseUsers, shouldAnonymiseEmails))
        {
            logger.LogWarning("Anonymise command was cancelled by the operator for {RepositoryRoot}.", repositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. AnonymiseUsers={AnonymiseUsers}. AnonymiseEmails={AnonymiseEmails}.",
            repositoryRoot,
            shouldAnonymiseUsers,
            shouldAnonymiseEmails);

        var filterRepoArguments = BuildFilterRepoArguments(
            repositoryRoot,
            shouldAnonymiseUsers,
            shouldAnonymiseEmails);

        await gitCommandRunner.RunExternalCheckedAsync(
            "python",
            repositoryRoot,
            filterRepoArguments,
            cancellationToken,
            startupErrorMessage: "Python could not be started. Ensure python is installed and available on PATH.",
            commandDisplayName: GitFilterRepoScriptName);
        await gitCommandRunner.RunCheckedAsync(repositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);
        await gitCommandRunner.RunCheckedAsync(repositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        var message =
            $"Completed history rewrite in {repositoryRoot}: anonymised {DescribeAnonymisationTargets(shouldAnonymiseUsers, shouldAnonymiseEmails)}. " +
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
            $"The '{GitFilterRepoScriptName}' script is required for anonymise. Place '{GitFilterRepoScriptName}' on PATH, " +
            "install it with 'python -m pip install git-filter-repo', or see https://github.com/newren/git-filter-repo/blob/main/INSTALL.md " +
            "for the upstream installation instructions.");
    }

    private static List<string> BuildFilterRepoArguments(
        string repositoryRoot,
        bool anonymiseUsers,
        bool anonymiseEmails)
    {
        var scriptPath = ResolveGitFilterRepoScriptPath(repositoryRoot);
        var filterRepoArguments = new List<string>
        {
            scriptPath,
            "--force"
        };

        if (anonymiseUsers)
        {
            filterRepoArguments.Add("--name-callback");
            filterRepoArguments.Add(BuildAnonymiseUserCallback());
        }

        if (anonymiseEmails)
        {
            filterRepoArguments.Add("--email-callback");
            filterRepoArguments.Add(BuildAnonymiseEmailCallback());
        }

        return filterRepoArguments;
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

    private static bool ConfirmRewrite(
        string repositoryRoot,
        string currentBranch,
        bool anonymiseUsers,
        bool anonymiseEmails)
    {
        WriteRewriteWarnings(repositoryRoot, currentBranch, anonymiseUsers, anonymiseEmails);
        Console.Write("Continue? [y/N]: ");

        var response = Console.ReadLine();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteRewriteWarnings(
        string repositoryRoot,
        string currentBranch,
        bool anonymiseUsers,
        bool anonymiseEmails)
    {
        Console.WriteLine("Warning: this will rewrite git history to anonymise commit and tag metadata.");
        Console.WriteLine($"Repository: {repositoryRoot}");
        Console.WriteLine($"Current branch: {currentBranch}");
        Console.WriteLine($"Anonymise: {DescribeAnonymisationTargets(anonymiseUsers, anonymiseEmails)}");
        Console.WriteLine("Warning: anonymising identities changes commit hashes and can affect clones, forks, pull requests, signed objects, and tooling that references existing hashes.");
        Console.WriteLine();
    }

    private static string DescribeAnonymisationTargets(bool anonymiseUsers, bool anonymiseEmails) =>
        anonymiseUsers && anonymiseEmails
            ? "usernames and emails"
            : anonymiseUsers
                ? "usernames"
                : "emails";

    private static string BuildAnonymiseUserCallback() =>
        "import hashlib; return b\"anonymous-user-\" + hashlib.sha256(name).hexdigest()[:12].encode(\"ascii\")";

    private static string BuildAnonymiseEmailCallback() =>
        "import hashlib; return b\"anonymous-email-\" + hashlib.sha256(email).hexdigest()[:12].encode(\"ascii\") + b\"@example.invalid\"";
}
