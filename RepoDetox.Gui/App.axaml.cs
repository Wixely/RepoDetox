using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepoDetox.Gui.Services;
using RepoDetox.Gui.ViewModels;
using RepoDetox.Gui.Views;
using Serilog;

namespace RepoDetox.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = provider.GetRequiredService<MainViewModel>();

            // Optional startup argument: a repository path (positional, or --repo/-r <path>) to
            // pre-select on launch, e.g. `RepoDetox.Gui C:\path\to\repo`.
            var repositoryPath = ParseRepositoryArgument(desktop.Args);
            if (!string.IsNullOrWhiteSpace(repositoryPath))
            {
                mainViewModel.RepositoryPath = repositoryPath;
            }

            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? ParseRepositoryArgument(string[]? args)
    {
        if (args is null)
        {
            return null;
        }

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];

            if ((string.Equals(arg, "--repo", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-r", StringComparison.OrdinalIgnoreCase))
                && index + 1 < args.Length)
            {
                return args[index + 1];
            }

            // A bare positional path to an existing directory.
            if (!arg.StartsWith('-') && Directory.Exists(arg))
            {
                return arg;
            }
        }

        return null;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RepoDetox",
            "logs");
        Directory.CreateDirectory(logDirectory);

        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "repodetox-gui-.log"),
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });

        // Core services.
        services.AddSingleton<GitCommandRunner>();
        services.AddSingleton<FastExportImportPipeline>();
        services.AddSingleton<RepositoryAnalyzer>();
        services.AddSingleton<RepositoryVacuumService>();
        services.AddSingleton<RepositoryAnonymiseService>();
        services.AddSingleton<RepositoryFlattenService>();
        services.AddSingleton<RepositoryExpungeService>();

        // GUI services.
        services.AddSingleton<RepositorySession>();
        services.AddSingleton<OperationCoordinator>();
        services.AddSingleton<RepositoryDiscoveryService>();
        services.AddSingleton<RepoBrowserStore>();

        // View models.
        services.AddSingleton<AnalyzeViewModel>();
        services.AddSingleton<VacuumViewModel>();
        services.AddSingleton<AnonymiseViewModel>();
        services.AddSingleton<FlattenViewModel>();
        services.AddSingleton<ExpungeViewModel>();
        services.AddTransient<RepoBrowserViewModel>();
        services.AddSingleton<MainViewModel>();
    }
}
