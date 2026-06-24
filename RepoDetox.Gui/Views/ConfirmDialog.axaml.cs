using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace RepoDetox.Gui.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Yes_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void No_Click(object? sender, RoutedEventArgs e) => Close(false);

    public static async Task<bool> ShowAsync(Window owner, string title, string message)
    {
        var dialog = new ConfirmDialog { Title = title };
        var messageText = dialog.FindControl<TextBlock>("MessageText");
        if (messageText is not null)
        {
            messageText.Text = message;
        }

        return await dialog.ShowDialog<bool>(owner);
    }
}
