using CommandLine;

namespace RepoDetox;

[Verb("vacuum", HelpText = "Rewrite history to remove historical-only files.")]
public sealed class VacuumOptions : RepositoryOptions
{
    [Option('f', "force", Default = false, HelpText = "Skip the interactive confirmation prompt.")]
    public bool Force { get; set; }
}
