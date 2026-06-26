using Microsoft.Extensions.Options;
using ModelContextProtocol;
using RepoDetox.Configuration;

namespace RepoDetox;

/// <summary>
/// Enforces the MCPSharp-style safety posture for the MCP server: a mutating tool must clear both the
/// master <see cref="RepoDetoxOptions.ReadOnly"/> switch and its own per-operation gate before it runs.
/// Errors are surfaced to the agent as <see cref="McpException"/> and name the exact config keys to set.
/// </summary>
public sealed class McpOperationGate
{
    private readonly RepoDetoxOptions _options;

    public McpOperationGate(IOptions<RepoDetoxOptions> options) => _options = options.Value;

    /// <summary>
    /// Throws if the given tool is not currently permitted by configuration.
    /// </summary>
    /// <param name="toolName">The MCP tool name, for the error message.</param>
    /// <param name="isOperationEnabled">Selects the per-operation flag from the options.</param>
    /// <param name="operationKey">The per-operation config key name, for the error message.</param>
    public void EnsureAllowed(string toolName, Func<RepoDetoxOptions, bool> isOperationEnabled, string operationKey)
    {
        if (_options.ReadOnly)
        {
            throw new McpException(
                $"MCP tool '{toolName}' is blocked by server configuration. " +
                $"Set RepoDetox:ReadOnly=false (and RepoDetox:{operationKey}=true) to allow this history rewrite.");
        }

        if (!isOperationEnabled(_options))
        {
            throw new McpException(
                $"MCP tool '{toolName}' is blocked by server configuration. " +
                $"Set RepoDetox:{operationKey}=true to allow this history rewrite.");
        }
    }
}
