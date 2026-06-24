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

    private bool AnyTarget => Users || Emails || SetName is not null || SetEmail is not null;

    /// <summary>True when the username side should be rewritten (defaults to both sides when nothing is targeted).</summary>
    public bool TargetsUsers => !AnyTarget || Users || SetName is not null;

    /// <summary>True when the email side should be rewritten (defaults to both sides when nothing is targeted).</summary>
    public bool TargetsEmails => !AnyTarget || Emails || SetEmail is not null;

    public IdentityRewriteMode NameMode =>
        SetName is not null ? IdentityRewriteMode.Fixed
        : TargetsUsers ? IdentityRewriteMode.Hash
        : IdentityRewriteMode.Keep;

    public IdentityRewriteMode EmailMode =>
        SetEmail is not null ? IdentityRewriteMode.Fixed
        : TargetsEmails ? IdentityRewriteMode.Hash
        : IdentityRewriteMode.Keep;
}
