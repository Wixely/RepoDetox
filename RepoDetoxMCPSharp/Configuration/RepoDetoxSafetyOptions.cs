namespace RepoDetoxMCPSharp.Configuration;

/// <summary>
/// Safety gates that mirror the CLI's <c>RepoDetox.Configuration.RepoDetoxOptions</c> shape so operators
/// see the same section name and keys they already know from <c>RepoDetox.json</c>. Every operation
/// this server exposes rewrites history, so the master <see cref="ReadOnly"/> switch defaults to true
/// and each destructive operation carries a second, per-op gate.
/// </summary>
public sealed class RepoDetoxSafetyOptions
{
    public const string SectionName = "RepoDetox";

    public bool ReadOnly { get; set; } = true;
    public bool AllowVacuum { get; set; }
    public bool AllowAnonymise { get; set; }
    public bool AllowFlatten { get; set; }
    public bool AllowExpunge { get; set; }
}
