namespace RepoDetox.Configuration;

/// <summary>
/// Safety options for the agent-facing MCP surface, modelled on the MCPSharp product line. RepoDetox
/// only has destructive operations (every one rewrites history), so the master <see cref="ReadOnly"/>
/// switch defaults to <c>true</c> and each operation has its own second gate. Flip both the master
/// switch and the per-operation flag to allow an agent to run that operation over the MCP server.
///
/// These gates apply to the <c>repodetox mcp</c> stdio server only; the interactive CLI keeps its own
/// <c>--force</c> / confirmation flow, since a human invoking a destructive verb is doing so explicitly.
/// </summary>
public sealed class RepoDetoxOptions
{
    public const string SectionName = "RepoDetox";

    /// <summary>
    /// Master safety switch. When true (default), every mutating MCP tool refuses with a clear error
    /// naming the config key to change. Read-only tools (<c>analyze_repository</c>) stay available.
    /// </summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>Second gate for <c>vacuum_repository</c>. Requires <see cref="ReadOnly"/>=false too.</summary>
    public bool AllowVacuum { get; set; }

    /// <summary>Second gate for <c>anonymise_repository</c>. Requires <see cref="ReadOnly"/>=false too.</summary>
    public bool AllowAnonymise { get; set; }

    /// <summary>Second gate for <c>flatten_repository</c>. Requires <see cref="ReadOnly"/>=false too.</summary>
    public bool AllowFlatten { get; set; }

    /// <summary>Second gate for <c>expunge_secrets</c>. Requires <see cref="ReadOnly"/>=false too.</summary>
    public bool AllowExpunge { get; set; }
}
