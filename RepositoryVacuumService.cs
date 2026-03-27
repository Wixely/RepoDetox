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
        var remotes = await GetConfiguredRemotesAsync(analysis.RepositoryRoot, cancellationToken);

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
        await EnsureGitFilterRepoIsAvailableAsync(analysis.RepositoryRoot, cancellationToken);

        var restoreRemotesAfterRewrite = false;

        if (options.Force)
        {
            WriteRewriteWarnings(analysis, remotes);
        }
        else if (!ConfirmRewrite(analysis, remotes, out restoreRemotesAfterRewrite))
        {
            logger.LogWarning("Vacuum command was cancelled by the operator for {RepositoryRoot}.", analysis.RepositoryRoot);
            return new VacuumResult(false, "History rewrite cancelled.");
        }

        logger.LogWarning(
            "Rewriting history for {RepositoryRoot}. Removing {Count} deleted historical paths that are absent from all live refs.",
            analysis.RepositoryRoot,
            analysis.HistoricalOnlyPaths.Count);

        var filterRepoArguments = BuildFilterRepoArguments(analysis.RepositoryRoot, analysis);

        Console.WriteLine("Starting history rewrite with git-filter-repo...");
        Console.WriteLine("Progress output will appear below.");
        Console.WriteLine();

        await gitCommandRunner.RunExternalCheckedAsync(
            "python",
            analysis.RepositoryRoot,
            filterRepoArguments,
            cancellationToken,
            startupErrorMessage: "Python could not be started. Ensure python is installed and available on PATH.",
            commandDisplayName: GitFilterRepoScriptName,
            echoOutputToConsole: true);

        Console.WriteLine();
        Console.WriteLine("Expiring reflogs...");
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);

        Console.WriteLine("Running git gc...");
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        if (restoreRemotesAfterRewrite && remotes.Count > 0)
        {
            Console.WriteLine("Restoring saved remotes...");
            await RestoreRemotesAsync(analysis.RepositoryRoot, remotes, cancellationToken);
        }

        var message =
            $"Completed history rewrite in {analysis.RepositoryRoot}: removed {analysis.HistoricalOnlyPaths.Count} deleted path(s) from history. " +
            $"The repository was then compacted{BuildRemoteRestoreSuffix(restoreRemotesAfterRewrite, remotes.Count)}.";

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

    private async Task<IReadOnlyList<RemoteConfiguration>> GetConfiguredRemotesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var listResult = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["remote"],
            cancellationToken);

        var remoteNames = listResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var remotes = new List<RemoteConfiguration>();

        foreach (var remoteName in remoteNames)
        {
            var fetchUrlResult = await gitCommandRunner.RunCheckedAsync(
                repositoryRoot,
                ["remote", "get-url", remoteName],
                cancellationToken);

            var pushUrlResult = await gitCommandRunner.RunAsync(
                repositoryRoot,
                ["remote", "get-url", "--push", remoteName],
                cancellationToken);

            var fetchUrl = fetchUrlResult.StandardOutput.Trim();
            var pushUrl = pushUrlResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(pushUrlResult.StandardOutput)
                ? pushUrlResult.StandardOutput.Trim()
                : fetchUrl;

            remotes.Add(new RemoteConfiguration(remoteName, fetchUrl, pushUrl));
        }

        return remotes;
    }

    private async Task RestoreRemotesAsync(
        string repositoryRoot,
        IReadOnlyList<RemoteConfiguration> remotes,
        CancellationToken cancellationToken)
    {
        foreach (var remote in remotes)
        {
            await gitCommandRunner.RunCheckedAsync(
                repositoryRoot,
                ["remote", "add", remote.Name, remote.FetchUrl],
                cancellationToken);

            if (!string.Equals(remote.FetchUrl, remote.PushUrl, StringComparison.Ordinal))
            {
                await gitCommandRunner.RunCheckedAsync(
                    repositoryRoot,
                    ["remote", "set-url", "--push", remote.Name, remote.PushUrl],
                    cancellationToken);
            }
        }
    }

    private static bool ConfirmRewrite(
        RepositoryScanResult analysis,
        IReadOnlyList<RemoteConfiguration> remotes,
        out bool restoreRemotesAfterRewrite)
    {
        WriteRewriteWarnings(analysis, remotes);
        restoreRemotesAfterRewrite = remotes.Count > 0 && PromptToRestoreRemotes(remotes);
        Console.Write("Continue? [y/N]: ");

        var response = Console.ReadLine();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteRewriteWarnings(
        RepositoryScanResult analysis,
        IReadOnlyList<RemoteConfiguration> remotes)
    {
        Console.WriteLine("Warning: this will rewrite git history to remove files that were deleted and are no longer present on any live ref.");
        Console.WriteLine($"Repository: {analysis.RepositoryRoot}");
        Console.WriteLine($"Current branch: {analysis.CurrentBranch}");
        Console.WriteLine($"Paths to remove: {analysis.HistoricalOnlyPaths.Count}");

        if (remotes.Count > 0)
        {
            Console.WriteLine($"Configured remotes: {string.Join(", ", remotes.Select(remote => remote.Name))}");
            Console.WriteLine("Warning: git-filter-repo removes remotes such as 'origin' as a safety measure.");
            Console.WriteLine("Reason: after a history rewrite, pushing or merging against the old remote state can reintroduce the old history and create a worse mess with duplicate commit graphs.");
        }

        Console.WriteLine();
    }

    private static bool PromptToRestoreRemotes(IReadOnlyList<RemoteConfiguration> remotes)
    {
        Console.Write("Re-add the saved remotes after the rewrite completes? [Y/n]: ");
        var response = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(response))
        {
            return true;
        }

        return !string.Equals(response, "n", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(response, "no", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRemoteRestoreSuffix(bool restoreRemotesAfterRewrite, int remoteCount)
    {
        if (remoteCount == 0)
        {
            return string.Empty;
        }

        return restoreRemotesAfterRewrite
            ? ", and the saved remotes were restored"
            : ", and remotes were left removed by git-filter-repo";
    }

    private sealed record RemoteConfiguration(string Name, string FetchUrl, string PushUrl);
}
