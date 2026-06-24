using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDetox.Gui.Models;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class RepoBrowserViewModel : ObservableObject
{
    private readonly RepositoryDiscoveryService _discovery;
    private readonly RepoBrowserStore _store;
    private readonly StringComparer _pathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Dictionary<string, DiscoveredRepository> _byPath;
    private HashSet<string> _seen;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private string statusText = "Ready.";

    [ObservableProperty]
    private DiscoveredRepository? selectedRepository;

    [ObservableProperty]
    private RecentRepository? selectedRecent;

    public RepoBrowserViewModel(RepositoryDiscoveryService discovery, RepoBrowserStore store)
    {
        _discovery = discovery;
        _store = store;
        _byPath = new Dictionary<string, DiscoveredRepository>(_pathComparer);
        _seen = new HashSet<string>(_pathComparer);

        Repositories = new ObservableCollection<DiscoveredRepository>();
        RecentRepositories = new ObservableCollection<RecentRepository>();

        ReposView = new DataGridCollectionView(Repositories);
        ReposView.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(DiscoveredRepository.LastChangedUtc), ListSortDirection.Descending));
    }

    public ObservableCollection<DiscoveredRepository> Repositories { get; }

    public DataGridCollectionView ReposView { get; }

    public ObservableCollection<RecentRepository> RecentRepositories { get; }

    /// <summary>Raised when the dialog should close; the argument is the chosen path, or null if cancelled.</summary>
    public event Action<string?>? CloseRequested;

    public async Task BeginSessionAsync()
    {
        RecentRepositories.Clear();
        foreach (var recent in _store.LoadRecent())
        {
            RecentRepositories.Add(recent);
        }

        foreach (var entry in _store.LoadCache())
        {
            // Drop stale cache entries that are no longer real repositories (e.g. a folder
            // simply named "git", or a repo that has since been removed).
            if (_byPath.ContainsKey(entry.Path) || !RepositoryDiscoveryService.IsGitRepository(entry.Path))
            {
                continue;
            }

            var repo = new DiscoveredRepository(entry.Path, entry.LastChangedUtc);
            _byPath[entry.Path] = repo;
            Repositories.Add(repo);
        }

        StatusText = Repositories.Count > 0
            ? $"{Repositories.Count} cached — rescanning…"
            : "Scanning…";

        await StartScanAsync();
    }

    [RelayCommand]
    private Task RescanAsync() => StartScanAsync();

    [RelayCommand]
    private void Cancel()
    {
        CancelScan();
        CloseRequested?.Invoke(null);
    }

    [RelayCommand]
    private void Select() => ChooseSelectedRepository();

    public void ChooseSelectedRepository()
    {
        if (SelectedRepository is { } repo)
        {
            Choose(repo.Path);
        }
    }

    public void ChooseSelectedRecent()
    {
        if (SelectedRecent is { } recent)
        {
            Choose(recent.Path);
        }
    }

    public void CancelScan()
    {
        try
        {
            _scanCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
    }

    private void Choose(string path)
    {
        CancelScan();
        CloseRequested?.Invoke(path);
    }

    private async Task StartScanAsync()
    {
        CancelScan();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        _seen = new HashSet<string>(_pathComparer);

        IsScanning = true;
        var progress = new Progress<DiscoveredRepository>(OnFound);

        try
        {
            await _discovery.ScanAsync(progress, token);
            PruneUnseen();
            SaveCache();
            StatusText = $"{Repositories.Count} repositories.";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Scan cancelled — {Repositories.Count} repositories.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void OnFound(DiscoveredRepository repo)
    {
        _seen.Add(repo.Path);

        if (_byPath.TryGetValue(repo.Path, out var existing))
        {
            existing.LastChangedUtc = repo.LastChangedUtc;
        }
        else
        {
            _byPath[repo.Path] = repo;
            Repositories.Add(repo);
        }

        if (IsScanning)
        {
            StatusText = $"Scanning… {Repositories.Count} found";
        }
    }

    private void PruneUnseen()
    {
        var stale = Repositories.Where(repo => !_seen.Contains(repo.Path)).ToList();
        foreach (var repo in stale)
        {
            Repositories.Remove(repo);
            _byPath.Remove(repo.Path);
        }
    }

    private void SaveCache() =>
        _store.SaveCache(Repositories.Select(repo => new RepoBrowserStore.CacheEntry(repo.Path, repo.LastChangedUtc)));
}
