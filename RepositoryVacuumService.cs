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

        if (analysis.HistoricalOnlyPaths.Count == 0)
        {
            return new VacuumResult(
                false,
                $"No historical-only paths were found in {analysis.RepositoryRoot}. Nothing to rewrite.");
        }

        await EnsureRepositoryIsCleanAsync(analysis.RepositoryRoot, cancellationToken);
        await EnsureGitFilterRepoIsAvailableAsync(analysis.RepositoryRoot, cancellationToken);

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
            "Rewriting history for {RepositoryRoot}. Removing {Count} historical-only paths.",
            analysis.RepositoryRoot,
            analysis.HistoricalOnlyPaths.Count);

        var filterRepoArguments = BuildFilterRepoArguments(analysis.RepositoryRoot, analysis);

        await gitCommandRunner.RunExternalCheckedAsync(
            "python",
            analysis.RepositoryRoot,
            filterRepoArguments,
            cancellationToken,
            startupErrorMessage: "Python could not be started. Ensure python is installed and available on PATH.",
            commandDisplayName: GitFilterRepoScriptName);
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        var message =
            $"Completed history rewrite in {analysis.RepositoryRoot}: removed {analysis.HistoricalOnlyPaths.Count} path(s) from history. " +
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
        RepositoryScanResult analysis)
    {
        var scriptPath = ResolveGitFilterRepoScriptPath(repositoryRoot);
        var filterRepoArguments = new List<string>
        {
            scriptPath,
            "--force",
            "--invert-paths"
        };

        foreach (var path in analysis.HistoricalOnlyPaths)
        {
            filterRepoArguments.Add("--path");
            filterRepoArguments.Add(path);
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
        Console.WriteLine("Warning: this will rewrite git history to remove historical-only files.");
        Console.WriteLine($"Repository: {analysis.RepositoryRoot}");
        Console.WriteLine($"Current branch: {analysis.CurrentBranch}");
        Console.WriteLine($"Paths to remove: {analysis.HistoricalOnlyPaths.Count}");
        Console.WriteLine();
    }
}
