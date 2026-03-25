using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace RepoDetox;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Configuration.Sources.Clear();
            builder.Configuration.SetBasePath(AppContext.BaseDirectory);
            builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            builder.Configuration.AddEnvironmentVariables();
            builder.Configuration.AddCommandLine(args);

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            builder.Logging.ClearProviders();
            builder.Services.AddSerilog(Log.Logger, dispose: false);
            builder.Services.AddSingleton<CliApplication>();
            builder.Services.AddSingleton<GitCommandRunner>();
            builder.Services.AddSingleton<PreviewServer>();
            builder.Services.AddSingleton<RepositoryAnalyzer>();
            builder.Services.AddSingleton<RepositoryVacuumService>();

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
