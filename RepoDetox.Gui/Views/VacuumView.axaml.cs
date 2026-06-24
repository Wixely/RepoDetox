using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RepoDetox.Gui.Views;

public partial class VacuumView : UserControl
{
    public VacuumView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
