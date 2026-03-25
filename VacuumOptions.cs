using CommandLine;

namespace RepoDetox;

[Verb("vacuum", HelpText = "Rewrite history to remove files that no longer exist on the current branch.")]
public sealed class VacuumOptions : RepositoryOptions
{
    [Option('f', "force", Default = false, HelpText = "Skip the interactive confirmation prompt.")]
    public bool Force { get; set; }
}
