using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RepoDetox.Gui.Views;

public partial class ExpungeView : UserControl
{
    public ExpungeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
