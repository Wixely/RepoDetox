using CommandLine;

namespace RepoDetox;

[Verb("anonymise", HelpText = "Rewrite history to anonymise commit and tag metadata without removing files. Requires an explicit repo path.")]
public sealed class AnonymiseOptions : RepositoryOptions
{
    [Option('f', "force", Default = false, HelpText = "Skip the interactive confirmation prompt.")]
    public bool Force { get; set; }

    [Option("users", Default = false, HelpText = "Anonymise usernames in commit and tag metadata.")]
    public bool Users { get; set; }

    [Option("emails", Default = false, HelpText = "Anonymise emails in commit and tag metadata.")]
    public bool Emails { get; set; }

    public bool ShouldAnonymiseUsers => !Users && !Emails || Users;

    public bool ShouldAnonymiseEmails => !Users && !Emails || Emails;
}
