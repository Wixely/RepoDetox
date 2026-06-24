using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDetox.Gui.Views;

namespace RepoDetox.Gui.Services;

/// <summary>
/// Runs one history operation at a time, owns the shared output log, busy state, and the
/// cancellation token, and serves as the <see cref="IOperationReporter"/> for the core services
/// (marshalling progress to the UI thread and showing confirmation dialogs).
/// </summary>
public sealed partial class OperationCoordinator : ObservableObject, IOperationReporter
{
    public ObservableCollection<string> Log { get; } = new();

    [ObservableProperty]
    private bool isBusy;

    private CancellationTokenSource? _cancellationSource;

    /// <summary>Runs an operation if none is in progress, surfacing the start/result/errors in the log.</summary>
    public async Task RunAsync(string? startMessage, Func<IOperationReporter, CancellationToken, Task<string?>> operation)
    {
        if (IsBusy)
        {
            Report("Another operation is already running.");
            return;
        }

        IsBusy = true;
        _cancellationSource = new CancellationTokenSource();

        try
        {
            if (!string.IsNullOrEmpty(startMessage))
            {
                Report(startMessage);
            }

            var message = await operation(this, _cancellationSource.Token);
            if (!string.IsNullOrEmpty(message))
            {
                Report(message);
            }
        }
        catch (OperationCanceledException)
        {
            Report("Operation cancelled.");
        }
        catch (Exception ex)
        {
            Report($"Error: {ex.Message}");
        }
        finally
        {
            var source = _cancellationSource;
            _cancellationSource = null;
            IsBusy = false;
            source?.Dispose();
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellationSource?.Cancel();

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    public void Report(string message) =>
        Dispatcher.UIThread.Post(() => Log.Add(message));

    public async Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken) =>
        await Dispatcher.UIThread.InvokeAsync(() => ShowConfirmAsync(question));

    private static async Task<bool> ShowConfirmAsync(string question)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
        {
            return false;
        }

        return await ConfirmDialog.ShowAsync(owner, "Confirm history rewrite", question);
    }
}
