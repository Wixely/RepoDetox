using CommandLine;

namespace RepoDetox;

public abstract class RepositoryOptions
{
    [Option('r', "repo", Default = ".", HelpText = "Path to the git repository to inspect. Defaults to the current directory.")]
    public string RepositoryPath { get; set; } = ".";
}
