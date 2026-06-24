using System.Text.Json;
using RepoDetox.Gui.Models;

namespace RepoDetox.Gui.Services;

/// <summary>
/// Persists the Repo Browser's discovered-repository cache and the recent-selection list under
/// <c>%LOCALAPPDATA%/RepoDetox</c> (cross-platform via <see cref="Environment.SpecialFolder.LocalApplicationData"/>).
/// </summary>
public sealed class RepoBrowserStore
{
    private const int MaxRecent = 15;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _cacheFile;
    private readonly string _recentFile;
    private readonly object _recentLock = new();

    public RepoBrowserStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RepoDetox");
        Directory.CreateDirectory(directory);
        _cacheFile = Path.Combine(directory, "repo-cache.json");
        _recentFile = Path.Combine(directory, "recent-repos.json");
    }

    public sealed record CacheEntry(string Path, DateTime LastChangedUtc);

    public IReadOnlyList<CacheEntry> LoadCache() => Load<CacheEntry>(_cacheFile);

    public void SaveCache(IEnumerable<CacheEntry> entries) => Save(_cacheFile, entries.ToList());

    public IReadOnlyList<RecentRepository> LoadRecent() => Load<RecentRepository>(_recentFile);

    public void AddRecent(string path)
    {
        lock (_recentLock)
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var recent = Load<RecentRepository>(_recentFile)
                .Where(entry => !comparer.Equals(entry.Path, path))
                .ToList();

            recent.Insert(0, new RecentRepository(path, DateTime.UtcNow));

            if (recent.Count > MaxRecent)
            {
                recent = recent.Take(MaxRecent).ToList();
            }

            Save(_recentFile, recent);
        }
    }

    private static IReadOnlyList<T> Load<T>(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return [];
            }

            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<List<T>>(json) ?? [];
        }
        catch
        {
            // A corrupt or unreadable cache is non-fatal; start fresh.
            return [];
        }
    }

    private static void Save<T>(string file, List<T> items)
    {
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch
        {
            // Persisting the cache is best-effort.
        }
    }
}
