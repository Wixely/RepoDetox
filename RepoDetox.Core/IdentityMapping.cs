namespace RepoDetox;

/// <summary>
/// A rule that replaces one specific contributor identity (matched exactly by name and email) with
/// a target identity, during anonymise. A null/empty target name or email leaves that side unchanged.
/// </summary>
public sealed record IdentityMapping(string SourceName, string SourceEmail, string? TargetName, string? TargetEmail)
{
    public string Display => $"{SourceName} <{SourceEmail}>  →  {TargetName} <{TargetEmail}>";
}
