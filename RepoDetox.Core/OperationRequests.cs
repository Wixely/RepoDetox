namespace RepoDetox;

/// <summary>Request to remove deleted historical paths from a repository's history.</summary>
public sealed record VacuumRequest(string RepositoryPath, bool SkipConfirmation);

/// <summary>Request to anonymise commit/tag identities.</summary>
public sealed record AnonymiseRequest(
    string RepositoryPath,
    bool SkipConfirmation,
    IdentityRewriteMode NameMode,
    IdentityRewriteMode EmailMode,
    string? FixedName,
    string? FixedEmail);

/// <summary>Request to collapse all history to a single root commit.</summary>
public sealed record FlattenRequest(string RepositoryPath, bool SkipConfirmation);

/// <summary>Request to replace literal secret strings throughout history.</summary>
public sealed record ExpungeRequest(
    string RepositoryPath,
    bool SkipConfirmation,
    IReadOnlyList<string> Secrets,
    string Replacement,
    bool IncludeMessages);
