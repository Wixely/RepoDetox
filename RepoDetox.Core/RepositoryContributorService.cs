namespace RepoDetox;

/// <summary>
/// Enumerates the distinct contributor identities (author, committer, and annotated-tag tagger
/// name+email pairs) found across all of a repository's history. Used to populate the "replace
/// specific contributor" lists, so it must cover every identity the anonymise rewrite can touch.
/// </summary>
public sealed class RepositoryContributorService(GitCommandRunner gitCommandRunner)
{
    // Unit separator (0x1F): used to delimit fields; will not appear in names/emails.
    private const string Separator = "";

    public async Task<IReadOnlyList<Contributor>> GetContributorsAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var repositoryRoot = await ResolveRepositoryRootAsync(repositoryPath, cancellationToken);

        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["log", "--all", $"--format=%an{Separator}%ae{Separator}%cn{Separator}%ce"],
            cancellationToken);

        var contributors = new HashSet<Contributor>();
        using var reader = new StringReader(result.StandardOutput);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(Separator);
            if (parts.Length >= 2 && parts[1].Length > 0)
            {
                contributors.Add(new Contributor(parts[0], parts[1]));
            }

            if (parts.Length >= 4 && parts[3].Length > 0)
            {
                contributors.Add(new Contributor(parts[2], parts[3]));
            }
        }

        await AddTaggerIdentitiesAsync(repositoryRoot, contributors, cancellationToken);

        return contributors
            .OrderBy(contributor => contributor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(contributor => contributor.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Annotated tags carry their own tagger identity, which the anonymise rewrite also changes, but
    // which never appears in `git log`. Enumerate it so tagger-only identities are still listed.
    private async Task AddTaggerIdentitiesAsync(
        string repositoryRoot,
        HashSet<Contributor> contributors,
        CancellationToken cancellationToken)
    {
        var result = await gitCommandRunner.RunCheckedAsync(
            repositoryRoot,
            ["for-each-ref", $"--format=%(taggername){Separator}%(taggeremail)", "refs/tags"],
            cancellationToken);

        using var reader = new StringReader(result.StandardOutput);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(Separator);
            if (parts.Length < 2)
            {
                continue;
            }

            // %(taggeremail) is wrapped in angle brackets, e.g. "<a@b.com>"; lightweight tags yield empty.
            var email = parts[1].Trim().TrimStart('<').TrimEnd('>');
            if (email.Length > 0)
            {
                contributors.Add(new Contributor(parts[0], email));
            }
        }
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
}
