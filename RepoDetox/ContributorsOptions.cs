using CommandLine;

namespace RepoDetox;

[Verb("contributors", HelpText = "List the distinct contributor identities (name <email>) found across all history. Useful for choosing values for 'anonymise --map'.")]
public sealed class ContributorsOptions : RepositoryOptions
{
}
