using CommandLine;

namespace RepoDetox;

[Verb("expunge", HelpText = "Rewrite history to replace literal secret strings everywhere they appear (file contents and, by default, commit/tag messages). Requires an explicit repo path.")]
public sealed class ExpungeOptions : RepositoryOptions
{
    [Option('f', "force", Default = false, HelpText = "Skip the interactive confirmation prompt.")]
    public bool Force { get; set; }

    [Option('s', "secret", HelpText = "A literal secret string to replace. Repeatable. Note: values passed here are visible in shell history and the process list; prefer --secrets-file.")]
    public IEnumerable<string> Secrets { get; set; } = [];

    [Option("secrets-file", HelpText = "Path to a file containing one secret string per line.")]
    public string? SecretsFile { get; set; }

    [Option("replacement", Default = "***REMOVED***", HelpText = "Text that each secret is replaced with.")]
    public string Replacement { get; set; } = "***REMOVED***";

    [Option("contents-only", Default = false, HelpText = "Only replace inside file contents, leaving commit/tag messages untouched.")]
    public bool ContentsOnly { get; set; }
}
