using CommandLine;

namespace RepoDetox;

[Verb("vacuum", HelpText = "Rewrite history to remove files that were deleted and are no longer present on any live ref. Requires an explicit repo path.")]
public sealed class VacuumOptions : RepositoryOptions
{
    [Option('f', "force", Default = false, HelpText = "Skip the interactive confirmation prompt.")]
    public bool Force { get; set; }
}
