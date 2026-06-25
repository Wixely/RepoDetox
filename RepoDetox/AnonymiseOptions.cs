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

    [Option("set-name", HelpText = "Replace every username with this exact value instead of a per-identity hash.")]
    public string? SetName { get; set; }

    [Option("set-email", HelpText = "Replace every email with this exact value instead of a per-identity hash.")]
    public string? SetEmail { get; set; }

    [Option("map", HelpText = "Replace a specific contributor with another. Format: \"Old Name <old@email>=New Name <new@email>\". Repeatable. When any --map is given, only the listed contributors are changed and all others are left unchanged (the blanket anonymise flags are ignored). Use the 'contributors' command to list existing identities.")]
    public IEnumerable<string> Map { get; set; } = [];

    public IdentityRewriteMode NameMode => IdentityRewritePlan.Resolve(Users, Emails, SetName, SetEmail).NameMode;

    public IdentityRewriteMode EmailMode => IdentityRewritePlan.Resolve(Users, Emails, SetName, SetEmail).EmailMode;
}
