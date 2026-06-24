using CommandLine;

namespace RepoDetox;

[Verb("list", HelpText = "List files that were deleted in history and are no longer present on any live ref.")]
public sealed class ListOptions : RepositoryOptions
{
}
