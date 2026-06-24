namespace RepoDetox;

/// <summary>How an identity field (name or email) is rewritten during anonymise.</summary>
public enum IdentityRewriteMode
{
    /// <summary>Leave the original value untouched.</summary>
    Keep,

    /// <summary>Replace with a deterministic per-identity hash.</summary>
    Hash,

    /// <summary>Replace every value with one caller-supplied literal.</summary>
    Fixed,
}
