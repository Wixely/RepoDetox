namespace RepoDetoxMCPSharp.Configuration;

/// <summary>
/// HTTP host options. Mirrors the MCPSharp product-line convention so the same knobs work here as on
/// SQLMCPSharp, RedisMCPSharp, etc. Bind under section <c>Server</c>.
/// </summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5714;
    public string Path { get; set; } = "/mcp";

    public string WindowsServiceName { get; set; } = "RepoDetoxMCPSharp";
    public string Password { get; set; } = string.Empty;
}
