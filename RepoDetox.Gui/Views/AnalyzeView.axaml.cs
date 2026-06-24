using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RepoDetox.Gui.Views;

public partial class AnalyzeView : UserControl
{
    public AnalyzeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
