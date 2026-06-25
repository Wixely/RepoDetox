namespace RepoDetox;

/// <summary>Request to remove deleted historical paths from a repository's history.</summary>
public sealed record VacuumRequest(string RepositoryPath, bool SkipConfirmation);

/// <summary>Request to anonymise commit/tag identities.</summary>
/// <remarks>
/// <see cref="Mappings"/> replaces specific contributors (matched exactly by name+email) with a
/// target identity. Mappings take precedence per identity; identities not covered by a mapping fall
/// back to <see cref="NameMode"/>/<see cref="EmailMode"/> (use <see cref="IdentityRewriteMode.Keep"/>
/// for both to leave everyone else unchanged).
/// </remarks>
public sealed record AnonymiseRequest(
    string RepositoryPath,
    bool SkipConfirmation,
    IdentityRewriteMode NameMode,
    IdentityRewriteMode EmailMode,
    string? FixedName,
    string? FixedEmail,
    IReadOnlyList<IdentityMapping>? Mappings = null);

/// <summary>Request to collapse all history to a single root commit.</summary>
public sealed record FlattenRequest(string RepositoryPath, bool SkipConfirmation);

/// <summary>Request to replace literal secret strings throughout history.</summary>
public sealed record ExpungeRequest(
    string RepositoryPath,
    bool SkipConfirmation,
    IReadOnlyList<string> Secrets,
    string Replacement,
    bool IncludeMessages);
