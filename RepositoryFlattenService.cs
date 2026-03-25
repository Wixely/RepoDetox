using System.Text;
using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class RepositoryFlattenService(
    GitCommandRunner gitCommandRunner,
    RepositoryAnalyzer repositoryAnalyzer,
    ILogger<RepositoryFlattenService> logger)
{
    public async Task<FlattenResult> FlattenAsync(
        FlattenOptions options,
        CancellationToken cancellationToken = default)
    {
        var analysis = await repositoryAnalyzer.AnalyzeAsync(options.RepositoryPath, cancellationToken);

        await EnsureRepositoryHasCommitsAsync(analysis.RepositoryRoot, cancellationToken);
        await EnsureRepositoryIsCleanAsync(analysis.RepositoryRoot, cancellationToken);

        var currentBranchRef = await GetCurrentBranchRefAsync(analysis.RepositoryRoot, cancellationToken);
        var currentTarget = currentBranchRef is null ? "detached HEAD" : analysis.CurrentBranch;
        var refsToDelete = await GetRefsToDeleteAsync(analysis.RepositoryRoot, currentBranchRef, cancellationToken);

        if (options.Force)
        {
            WriteRewriteWarnings(analysis, currentTarget, refsToDelete.Count);
        }
        else if (!ConfirmRewrite(analysis, currentTarget, refsToDelete.Count))
        {
            logger.LogWarning("Flatten command was cancelled by the operator for {RepositoryRoot}.", analysis.RepositoryRoot);
            return new FlattenResult(false, "History flatten cancelled.");
        }

        logger.LogWarning(
            "Flattening history for {RepositoryRoot}. CurrentTarget={CurrentTarget}. RemovingRefs={RefCount}.",
            analysis.RepositoryRoot,
            currentTarget,
            refsToDelete.Count);

        var headCommitContext = await GetHeadCommitContextAsync(analysis.RepositoryRoot, cancellationToken);
        var flattenedCommit = await CreateFlattenedCommitAsync(
            analysis.RepositoryRoot,
            headCommitContext,
            cancellationToken);

        await PointHeadToFlattenedCommitAsync(
            analysis.RepositoryRoot,
            currentBranchRef,
            flattenedCommit,
            cancellationToken);

        foreach (var refToDelete in refsToDelete)
        {
            await gitCommandRunner.RunCheckedAsync(
                analysis.RepositoryRoot,
                ["update-ref", "-d", refToDelete],
                cancellationToken);
        }

        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["reflog", "expire", "--expire=now", "--all"], cancellationToken);
        await gitCommandRunner.RunCheckedAsync(analysis.RepositoryRoot, ["gc", "--prune=now", "--aggressive"], cancellationToken);

        var message =
            $"Flattened history in {analysis.RepositoryRoot}. {currentTarget} now points to single root commit {flattenedCommit}, " +
            $"and {refsToDelete.Count} other ref(s) were removed.";

        logger.LogInformation(message);

        return new FlattenResult(true, message);
    }

    private async Task EnsureRepositoryHasCommitsAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken);

        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException("The target repository has no commits. Flatten requires an existing HEAD commit.");
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
            "The target repository has uncommitted changes. Commit or stash them before running flatten.");
    }

    private async Task<string?> GetCurrentBranchRefAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunAsync(
            repositoryRoot,
            ["symbolic-ref", "--quiet", "HEAD"],
            cancellationToken);

        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardOutput.Trim()
            : null;
    }

    private async Task<IReadOnlyList<string>> GetRefsToDeleteAsync(
        string repositoryRoot,
        string? currentBranchRef,
        CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["for-each-ref", "--format=%(refname)", "refs"],
            cancellationToken);

        var refs = new List<string>();
        using var reader = new StringReader(result.StandardOutput);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (currentBranchRef is not null && string.Equals(line, currentBranchRef, StringComparison.Ordinal))
            {
                continue;
            }

            refs.Add(line);
        }

        return refs;
    }

    private async Task<HeadCommitContext> GetHeadCommitContextAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var treeResult = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["rev-parse", "HEAD^{tree}"],
            cancellationToken);

        var metadataResult = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["log", "-1", "--format=%an%x00%ae%x00%aI%x00%cn%x00%ce%x00%cI", "HEAD"],
            cancellationToken);

        var metadataParts = metadataResult.StandardOutput.TrimEnd('\r', '\n').Split('\0');
        if (metadataParts.Length != 6)
        {
            throw new InvalidOperationException("Unable to read HEAD commit metadata for flatten.");
        }

        var messageResult = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["log", "-1", "--format=%B", "HEAD"],
            cancellationToken);

        return new HeadCommitContext(
            treeResult.StandardOutput.Trim(),
            messageResult.StandardOutput,
            metadataParts[0],
            metadataParts[1],
            metadataParts[2],
            metadataParts[3],
            metadataParts[4],
            metadataParts[5]);
    }

    private async Task<string> CreateFlattenedCommitAsync(
        string repositoryRoot,
        HeadCommitContext headCommitContext,
        CancellationToken cancellationToken)
    {
        var tempMessageFile = Path.Combine(
            Path.GetTempPath(),
            $"repodetox-flatten-{Guid.NewGuid():N}.txt");

        await File.WriteAllTextAsync(
            tempMessageFile,
            headCommitContext.Message,
            new UTF8Encoding(false),
            cancellationToken);

        try
        {
            var environmentVariables = new Dictionary<string, string?>
            {
                ["GIT_AUTHOR_NAME"] = headCommitContext.AuthorName,
                ["GIT_AUTHOR_EMAIL"] = headCommitContext.AuthorEmail,
                ["GIT_AUTHOR_DATE"] = headCommitContext.AuthorDate,
                ["GIT_COMMITTER_NAME"] = headCommitContext.CommitterName,
                ["GIT_COMMITTER_EMAIL"] = headCommitContext.CommitterEmail,
                ["GIT_COMMITTER_DATE"] = headCommitContext.CommitterDate
            };

            var result = await gitCommandRunner.RunCheckedAsync(
                repositoryRoot,
                ["commit-tree", headCommitContext.TreeId, "-F", tempMessageFile],
                cancellationToken,
                environmentVariables);

            return result.StandardOutput.Trim();
        }
        finally
        {
            if (File.Exists(tempMessageFile))
            {
                File.Delete(tempMessageFile);
            }
        }
    }

    private async Task PointHeadToFlattenedCommitAsync(
        string repositoryRoot,
        string? currentBranchRef,
        string flattenedCommit,
        CancellationToken cancellationToken)
    {
        if (currentBranchRef is null)
        {
            await gitCommandRunner.RunCheckedAsync(
                repositoryRoot,
                ["update-ref", "HEAD", flattenedCommit],
                cancellationToken);

            return;
        }

        await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["update-ref", currentBranchRef, flattenedCommit],
            cancellationToken);

        await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["symbolic-ref", "HEAD", currentBranchRef],
            cancellationToken);
    }

    private static bool ConfirmRewrite(
        RepositoryScanResult analysis,
        string currentTarget,
        int refsToDeleteCount)
    {
        WriteRewriteWarnings(analysis, currentTarget, refsToDeleteCount);
        Console.Write("Continue? [y/N]: ");

        var response = Console.ReadLine();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteRewriteWarnings(
        RepositoryScanResult analysis,
        string currentTarget,
        int refsToDeleteCount)
    {
        Console.WriteLine("Warning: flatten will replace repository history with a single root commit.");
        Console.WriteLine($"Repository: {analysis.RepositoryRoot}");
        Console.WriteLine($"Current target: {currentTarget}");
        Console.WriteLine($"Other refs to delete: {refsToDeleteCount}");
        Console.WriteLine("Warning: all prior commit hashes, branches, tags, and refs will become invalid, which can affect clones, forks, pull requests, signed objects, and tooling that references existing hashes.");
        Console.WriteLine();
    }

    private sealed record HeadCommitContext(
        string TreeId,
        string Message,
        string AuthorName,
        string AuthorEmail,
        string AuthorDate,
        string CommitterName,
        string CommitterEmail,
        string CommitterDate);
}
