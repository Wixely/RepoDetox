using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RepoDetox.Gui.Views;

public partial class FlattenView : UserControl
{
    public FlattenView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
