using CommandLine;

namespace RepoDetox;

[Verb("vacuum", HelpText = "Rewrite history to remove historical-only files and optionally anonymize commit metadata.")]
public sealed class VacuumOptions : RepositoryOptions
{
    [Option('f', "force", Default = false, HelpText = "Skip the interactive confirmation prompt.")]
    public bool Force { get; set; }

    [Option("anonymize", Default = false, HelpText = "Anonymize both usernames and emails in commit/tag metadata.")]
    public bool Anonymize { get; set; }

    [Option("anonymize-users", Default = false, HelpText = "Anonymize usernames in commit/tag metadata.")]
    public bool AnonymizeUsers { get; set; }

    [Option("anonymize-emails", Default = false, HelpText = "Anonymize emails in commit/tag metadata.")]
    public bool AnonymizeEmails { get; set; }

    public bool ShouldAnonymizeUsers => Anonymize || AnonymizeUsers;

    public bool ShouldAnonymizeEmails => Anonymize || AnonymizeEmails;
}
