using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GitCommandRunner _gitCommandRunner;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        RepositorySession session,
        OperationCoordinator coordinator,
        GitCommandRunner gitCommandRunner,
        AnalyzeViewModel analyze,
        VacuumViewModel vacuum,
        AnonymiseViewModel anonymise,
        FlattenViewModel flatten,
        ILogger<MainViewModel> logger)
    {
        Session = session;
        Coordinator = coordinator;
        _gitCommandRunner = gitCommandRunner;
        Analyze = analyze;
        Vacuum = vacuum;
        Anonymise = anonymise;
        Flatten = flatten;
        _logger = logger;
    }

    public RepositorySession Session { get; }

    public OperationCoordinator Coordinator { get; }

    public AnalyzeViewModel Analyze { get; }

    public VacuumViewModel Vacuum { get; }

    public AnonymiseViewModel Anonymise { get; }

    public FlattenViewModel Flatten { get; }

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
