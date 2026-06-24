using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class AnalyzeViewModel : TabViewModelBase
{
    private readonly RepositoryAnalyzer _analyzer;

    [ObservableProperty]
    private string summary = "Run Analyze to scan the selected repository.";

    public AnalyzeViewModel(
        RepositoryAnalyzer analyzer,
        RepositorySession session,
        OperationCoordinator coordinator)
        : base(session, coordinator)
    {
        _analyzer = analyzer;
    }

    public ObservableCollection<string> RemovablePaths { get; } = new();

    [RelayCommand]
    private Task AnalyzeAsync() => Coordinator.RunAsync(
        "Analyzing repository...",
        async (reporter, cancellationToken) =>
        {
            var result = await _analyzer.AnalyzeAsync(Session.RepositoryPath!, cancellationToken);
            Session.LastScan = result;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RemovablePaths.Clear();
                foreach (var entry in result.HistoricalOnlyPathEntries)
                {
                    RemovablePaths.Add($"{entry.Path}{ScanReportFormatter.FormatHistoricalSize(entry.MaxSizeBytes)}");
                }

                Summary =
                    $"Branch '{result.CurrentBranch}' | tracked {result.CurrentTrackedFileCount} | " +
                    $"deleted-in-history {result.DeletedPathCount} | removable {result.HistoricalOnlyPaths.Count} | " +
                    $"est. savings {ScanReportFormatter.FormatAggregateSize(result.EstimatedSavingsBytes)}";
            });

            foreach (var line in ScanReportFormatter.Describe(result))
            {
                reporter.Report(line);
            }

            return "Analysis complete.";
        });
}
