using CommunityToolkit.Mvvm.ComponentModel;

namespace RepoDetox.Gui.Services;

/// <summary>
/// Shared repository selection state held outside the feature tabs, so every tab operates on
/// the one repository the user has chosen.
/// </summary>
public sealed partial class RepositorySession : ObservableObject
{
    [ObservableProperty]
    private string? repositoryPath;

    [ObservableProperty]
    private string? repositoryRoot;

    [ObservableProperty]
    private bool isValidRepository;

    [ObservableProperty]
    private string statusLine = "No repository selected.";

    [ObservableProperty]
    private RepositoryScanResult? lastScan;
}
