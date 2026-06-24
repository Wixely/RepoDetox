namespace RepoDetox.Gui.Models;

/// <summary>A repository the user recently selected, for quick re-access.</summary>
public sealed record RecentRepository(string Path, DateTime SelectedUtc);
