using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RepoDetox.Gui.Views;

public partial class AnonymiseView : UserControl
{
    public AnonymiseView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
