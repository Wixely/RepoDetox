using CommandLine;

namespace RepoDetox;

[Verb("list", HelpText = "List files that exist in git history but no longer exist on the current branch.")]
public sealed class ListOptions : RepositoryOptions
{
}
