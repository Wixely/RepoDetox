using Microsoft.Extensions.Configuration;

namespace RepoDetox.Configuration;

/// <summary>
/// Shared host bootstrap helpers used by every RepoDetox front-end (CLI, stdio MCP server, and GUI) so
/// they all read the same configuration the same way. The content root is resolved from the real
/// executable path (not the process working directory), and configuration comes from a single
/// <c>RepoDetox.json</c> next to the executable. Unlike the wider MCPSharp product line, RepoDetox uses
/// one shared config file name for both of its executables rather than a per-executable name or an
/// <c>appsettings.json</c> compatibility layer.
/// </summary>
public static class HostConfigurator
{
    /// <summary>
    /// The directory containing the executable. Preferred over <see cref="AppContext.BaseDirectory"/>
    /// because single-file publishes extract to a temp directory — config and logs should resolve next
    /// to the real executable.
    /// </summary>
    public static string GetContentRoot() =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>
    /// Layers configuration sources (later wins): the shared <c>RepoDetox.json</c>, an optional
    /// machine-specific <c>RepoDetox.Local.json</c>, unprefixed then <c>REPODETOX_</c>-prefixed
    /// environment variables, and finally command-line arguments. The Serilog file path is pinned next
    /// to the executable so logs land beside the binary regardless of the working directory.
    /// </summary>
    public static void ApplyConfiguration(IConfigurationBuilder config, string contentRoot, string[] args)
    {
        config.Sources.Clear();
        config.SetBasePath(contentRoot);

        config.AddJsonFile(ResolveConfigFile(contentRoot, "RepoDetox.json"), optional: true, reloadOnChange: true);
        config.AddJsonFile(ResolveConfigFile(contentRoot, "RepoDetox.Local.json"), optional: true, reloadOnChange: true);

        // Resolve the rolling log file next to the executable (the JSON path is relative to cwd otherwise).
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:1:Args:path"] = Path.Combine(contentRoot, "logs", "repodetox-.log"),
        });

        config.AddEnvironmentVariables();
        config.AddEnvironmentVariables(prefix: "REPODETOX_");
        config.AddCommandLine(args);
    }

    /// <summary>
    /// Resolves a config file name case-insensitively within the content root, so a file authored as
    /// <c>RepoDetox.json</c> still loads on case-sensitive Linux filesystems if cased differently.
    /// Returns the requested name unchanged when no match exists (the source is optional anyway).
    /// </summary>
    public static string ResolveConfigFile(string contentRoot, string fileName)
    {
        if (File.Exists(Path.Combine(contentRoot, fileName)))
        {
            return fileName;
        }

        try
        {
            var match = Directory.EnumerateFiles(contentRoot, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

            return match is null ? fileName : Path.GetFileName(match);
        }
        catch (DirectoryNotFoundException)
        {
            return fileName;
        }
    }
}
