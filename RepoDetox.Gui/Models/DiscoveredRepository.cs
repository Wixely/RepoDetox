using CommunityToolkit.Mvvm.ComponentModel;

namespace RepoDetox.Gui.Models;

/// <summary>A git repository found by the Repo Browser scan.</summary>
public sealed partial class DiscoveredRepository : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastChangedDisplay))]
    private DateTime lastChangedUtc;

    public DiscoveredRepository(string path, DateTime lastChangedUtc)
    {
        Path = path;
        var leaf = System.IO.Path.GetFileName(
            path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        Name = string.IsNullOrEmpty(leaf) ? path : leaf;
        this.lastChangedUtc = lastChangedUtc;
    }

    public string Path { get; }

    public string Name { get; }

    /// <summary>The "last changed" timestamp shown in the grid (local time).</summary>
    public string LastChangedDisplay => LastChangedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
