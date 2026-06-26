using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RepoDetox.Configuration;
using Serilog;

namespace RepoDetox;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var contentRoot = HostConfigurator.GetContentRoot();

        // Run as an stdio MCP server when invoked as `repodetox mcp`. Handled before the normal
        // host/Serilog setup so stdout carries only the MCP protocol. Existing verbs are unchanged.
        if (args.Length > 0 && string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase))
        {
            await McpServer.RunAsync(args[1..], contentRoot);
            return 0;
        }

        // Bootstrap logger so failures before the host is built are still captured next to the exe.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(contentRoot, "logs", "repodetox-bootstrap-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateBootstrapLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            HostConfigurator.ApplyConfiguration(builder.Configuration, contentRoot, args);

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            builder.Logging.ClearProviders();
            builder.Services.AddSerilog(Log.Logger, dispose: false);
            builder.Services.Configure<RepoDetoxOptions>(builder.Configuration.GetSection(RepoDetoxOptions.SectionName));
            builder.Services.AddSingleton<CliApplication>();
            builder.Services.AddSingleton<IOperationReporter, ConsoleOperationReporter>();
            builder.Services.AddSingleton<GitCommandRunner>();
            builder.Services.AddSingleton<FastExportImportPipeline>();
            builder.Services.AddSingleton<PreviewServer>();
            builder.Services.AddSingleton<RepositoryAnalyzer>();
            builder.Services.AddSingleton<RepositoryAnonymiseService>();
            builder.Services.AddSingleton<RepositoryFlattenService>();
            builder.Services.AddSingleton<RepositoryVacuumService>();
            builder.Services.AddSingleton<RepositoryExpungeService>();
            builder.Services.AddSingleton<RepositoryContributorService>();

            RegisterGlobalExceptionLogging();

            using var host = builder.Build();
            var application = host.Services.GetRequiredService<CliApplication>();

            return await application.RunAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "RepoDetox terminated unexpectedly.");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void RegisterGlobalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "An unhandled exception reached AppDomain.CurrentDomain.UnhandledException.");
                return;
            }

            Log.Fatal("A non-exception object reached AppDomain.CurrentDomain.UnhandledException.");
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.Error(eventArgs.Exception, "An unobserved task exception was raised.");
            eventArgs.SetObserved();
        };
    }
}
