namespace RepoDetox;

/// <summary>
/// Enumerates the distinct contributor identities (author and committer name+email pairs) found
/// across all of a repository's history. Used to populate the "replace specific contributor" lists.
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

        return contributors
            .OrderBy(contributor => contributor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(contributor => contributor.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
