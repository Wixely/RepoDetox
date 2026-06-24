using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RepoDetox.Gui.ViewModels;

namespace RepoDetox.Gui.Views;

public partial class RepoBrowserDialog : Window
{
    public RepoBrowserDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private RepoBrowserViewModel? ViewModel => DataContext as RepoBrowserViewModel;

    private void OnReposDoubleTapped(object? sender, TappedEventArgs e) => ViewModel?.ChooseSelectedRepository();

    private void OnRecentDoubleTapped(object? sender, TappedEventArgs e) => ViewModel?.ChooseSelectedRecent();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ViewModel?.CancelScan();
        base.OnClosing(e);
    }

    public static async Task<string?> ShowAsync(Window owner, RepoBrowserViewModel viewModel)
    {
        var dialog = new RepoBrowserDialog { DataContext = viewModel };

        void OnCloseRequested(string? path) => dialog.Close(path);
        viewModel.CloseRequested += OnCloseRequested;

        try
        {
            _ = viewModel.BeginSessionAsync();
            return await dialog.ShowDialog<string?>(owner);
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.CancelScan();
        }
    }
}
