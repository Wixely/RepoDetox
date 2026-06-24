using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepoDetox.Gui.Services;
using RepoDetox.Gui.Views;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GitCommandRunner _gitCommandRunner;
    private readonly IServiceProvider _services;
    private readonly RepoBrowserStore _browserStore;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private int selectedTabIndex;

    public MainViewModel(
        RepositorySession session,
        OperationCoordinator coordinator,
        GitCommandRunner gitCommandRunner,
        IServiceProvider services,
        RepoBrowserStore browserStore,
        AnalyzeViewModel analyze,
        VacuumViewModel vacuum,
        AnonymiseViewModel anonymise,
        FlattenViewModel flatten,
        ExpungeViewModel expunge,
        ILogger<MainViewModel> logger)
    {
        Session = session;
        Coordinator = coordinator;
        _gitCommandRunner = gitCommandRunner;
        _services = services;
        _browserStore = browserStore;
        Analyze = analyze;
        Vacuum = vacuum;
        Anonymise = anonymise;
        Flatten = flatten;
        Expunge = expunge;
        _logger = logger;
    }

    public RepositorySession Session { get; }

    public OperationCoordinator Coordinator { get; }

    public AnalyzeViewModel Analyze { get; }

    public VacuumViewModel Vacuum { get; }

    public AnonymiseViewModel Anonymise { get; }

    public FlattenViewModel Flatten { get; }

    public ExpungeViewModel Expunge { get; }

    public string? RepositoryPath
    {
        get => Session.RepositoryPath;
        set
        {
            if (Session.RepositoryPath == value)
            {
                return;
            }

            Session.RepositoryPath = value;
            OnPropertyChanged();
            _ = ValidateRepositoryAsync(value);
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var window = MainWindow;
        if (window is null)
        {
            return;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a git repository",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            RepositoryPath = folders[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task OpenRepoBrowserAsync()
    {
        var window = MainWindow;
        if (window is null)
        {
            return;
        }

        var browser = _services.GetRequiredService<RepoBrowserViewModel>();
        var path = await RepoBrowserDialog.ShowAsync(window, browser);

        if (!string.IsNullOrEmpty(path))
        {
            await SelectRepositoryAndAnalyzeAsync(path);
        }
    }

    private async Task SelectRepositoryAndAnalyzeAsync(string path)
    {
        Session.RepositoryPath = path;
        OnPropertyChanged(nameof(RepositoryPath));

        await ValidateRepositoryAsync(path);
        _browserStore.AddRecent(path);
        SelectedTabIndex = 0;

        if (Session.IsValidRepository)
        {
            await Analyze.AnalyzeCommand.ExecuteAsync(null);
        }
    }

    private async Task ValidateRepositoryAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            Session.IsValidRepository = false;
            Session.RepositoryRoot = null;
            Session.StatusLine = "No repository selected.";
            return;
        }

        try
        {
            var rootResult = await _gitCommandRunner.RunAsync(path, ["rev-parse", "--show-toplevel"]);
            if (rootResult.ExitCode != 0)
            {
                Session.IsValidRepository = false;
                Session.RepositoryRoot = null;
                Session.StatusLine = "Not a git repository.";
                return;
            }

            var root = rootResult.StandardOutput.Trim();
            var branchResult = await _gitCommandRunner.RunAsync(root, ["rev-parse", "--abbrev-ref", "HEAD"]);
            var branch = branchResult.ExitCode == 0 ? branchResult.StandardOutput.Trim() : "(unknown)";

            Session.RepositoryRoot = root;
            Session.IsValidRepository = true;
            Session.StatusLine = $"Valid git repository on branch '{branch}'.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate repository at {Path}.", path);
            Session.IsValidRepository = false;
            Session.RepositoryRoot = null;
            Session.StatusLine = $"Could not read repository: {ex.Message}";
        }
    }

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
