using CommandLine;

namespace RepoDetox;

[Verb("flatten", HelpText = "Rewrite the repository to a single root commit that matches the current HEAD state.")]
public sealed class FlattenOptions : RepositoryOptions
{
    [Option('f', "force", Default = false, HelpText = "Skip the interactive confirmation prompt.")]
    public bool Force { get; set; }
}
