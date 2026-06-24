namespace RepoDetox;

/// <summary>
/// Resolves which <see cref="IdentityRewriteMode"/> applies to the name and email sides of an
/// anonymise operation. Shared by the CLI options and the GUI so the two front-ends cannot
/// diverge in behaviour.
/// </summary>
public static class IdentityRewritePlan
{
    /// <summary>
    /// Determines the per-side rewrite mode. When nothing is targeted, both sides are hashed
    /// (the default). Supplying a fixed value forces that side to <see cref="IdentityRewriteMode.Fixed"/>
    /// and counts as targeting that side only.
    /// </summary>
    public static (IdentityRewriteMode NameMode, IdentityRewriteMode EmailMode) Resolve(
        bool anonymiseUsers,
        bool anonymiseEmails,
        string? fixedName,
        string? fixedEmail)
    {
        var anyTarget = anonymiseUsers || anonymiseEmails || fixedName is not null || fixedEmail is not null;
        var targetsUsers = !anyTarget || anonymiseUsers || fixedName is not null;
        var targetsEmails = !anyTarget || anonymiseEmails || fixedEmail is not null;

        var nameMode = fixedName is not null
            ? IdentityRewriteMode.Fixed
            : targetsUsers ? IdentityRewriteMode.Hash : IdentityRewriteMode.Keep;

        var emailMode = fixedEmail is not null
            ? IdentityRewriteMode.Fixed
            : targetsEmails ? IdentityRewriteMode.Hash : IdentityRewriteMode.Keep;

        return (nameMode, emailMode);
    }
}
