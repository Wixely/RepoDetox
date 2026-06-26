using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoDetox.Configuration;

namespace RepoDetox;

/// <summary>
/// Runs RepoDetox as an stdio Model Context Protocol server so an agent can call its operations as
/// tools. stdout carries only the JSON-RPC protocol; all logging goes to stderr so the stream stays
/// clean. The same Core services back both this server and the regular CLI verbs. Configuration and the
/// MCPSharp-style safety gates are loaded the same way as the CLI host.
/// </summary>
public static class McpServer
{
    public static async Task RunAsync(string[] args, string contentRoot, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);
        HostConfigurator.ApplyConfiguration(builder.Configuration, contentRoot, args);

        // stdout is reserved for the MCP protocol; route every log to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.Configure<RepoDetoxOptions>(builder.Configuration.GetSection(RepoDetoxOptions.SectionName));
        builder.Services.AddSingleton<McpOperationGate>();
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

        var app = builder.Build();

        LogStartup(app.Services, contentRoot);

        await app.RunAsync(cancellationToken);
    }

    private static void LogStartup(IServiceProvider services, string contentRoot)
    {
        var options = services.GetRequiredService<IOptions<RepoDetoxOptions>>().Value;
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("RepoDetox.Startup");

        log.LogInformation("RepoDetox startup");
        log.LogInformation("  Transport: stdio (MCP)");
        log.LogInformation("  Mode: {Mode}", Environment.UserInteractive ? "Console" : "Non-interactive");
        log.LogInformation("  Read-only: {ReadOnly}", options.ReadOnly);
        log.LogInformation(
            "  Allowed operations: vacuum={Vacuum} anonymise={Anonymise} flatten={Flatten} expunge={Expunge}",
            options.AllowVacuum,
            options.AllowAnonymise,
            options.AllowFlatten,
            options.AllowExpunge);
        log.LogInformation("  Content root: {ContentRoot}", contentRoot);
    }
}
