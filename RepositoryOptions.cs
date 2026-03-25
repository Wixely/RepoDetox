using CommandLine;

namespace RepoDetox;

public abstract class RepositoryOptions
{
    [Value(0, MetaName = "repo", Required = false, HelpText = "Path to the git repository to inspect. Defaults to the current directory.")]
    public string? RepositoryPathArgument { get; set; }

    [Option('r', "repo", HelpText = "Path to the git repository to inspect. Defaults to the current directory.")]
    public string? RepositoryPathOption { get; set; }

    public string RepositoryPath =>
        !string.IsNullOrWhiteSpace(RepositoryPathArgument)
            ? RepositoryPathArgument
            : !string.IsNullOrWhiteSpace(RepositoryPathOption)
                ? RepositoryPathOption
                : ".";
}
