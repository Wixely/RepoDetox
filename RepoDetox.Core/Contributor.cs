namespace RepoDetox;

/// <summary>A distinct commit identity (name + email) found in a repository's history.</summary>
public sealed record Contributor(string Name, string Email)
{
    /// <summary>Human-readable form: <c>Name &lt;email&gt;</c>.</summary>
    public string Display => $"{Name} <{Email}>";
}
