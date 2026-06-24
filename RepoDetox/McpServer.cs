using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RepoDetox;

/// <summary>
/// Runs RepoDetox as an stdio Model Context Protocol server so an agent can call its operations as
/// tools. stdout carries only the JSON-RPC protocol; all logging goes to stderr so the stream stays
/// clean. The same Core services back both this server and the regular CLI verbs.
/// </summary>
public static class McpServer
{
    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // stdout is reserved for the MCP protocol; route every log to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton<GitCommandRunner>();
        builder.Services.AddSingleton<FastExportImportPipeline>();
        builder.Services.AddSingleton<RepositoryAnalyzer>();
        builder.Services.AddSingleton<RepositoryVacuumService>();
        builder.Services.AddSingleton<RepositoryAnonymiseService>();
        builder.Services.AddSingleton<RepositoryFlattenService>();
        builder.Services.AddSingleton<RepositoryExpungeService>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync(cancellationToken);
    }
}
