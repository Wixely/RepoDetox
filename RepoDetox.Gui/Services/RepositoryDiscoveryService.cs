using System.Collections.Concurrent;
using RepoDetox.Gui.Models;

namespace RepoDetox.Gui.Services;

/// <summary>
/// Discovers git repositories by walking the filesystem looking only for <c>.git</c> markers.
/// Optimised for speed: one batched directory enumeration per folder (attributes, <c>.git</c>
/// detection, and the <c>.git</c> mtime all come from that single pass), found repositories are
/// pruned (never descended into), inaccessible entries and reparse points are skipped without
/// exception cost, and the walk runs across parallel workers. Cross-platform: roots come from
/// <see cref="DriveInfo"/> plus the user's home, with pseudo filesystems excluded.
/// </summary>
public sealed class RepositoryDiscoveryService
{
    private static readonly string[] PriorityNames = ["git", "repos", "dev"];

    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".svn", ".hg",
        ".cache", ".npm", ".nuget", ".cargo", ".gradle", ".m2",
        // Windows system / noise folders:
        "$Recycle.Bin", "System Volume Information", "Windows",
        "Program Files", "Program Files (x86)", "ProgramData", "AppData", "$WinREAgent",
    };

    private static readonly HashSet<string> PseudoFilesystemFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "sysfs", "tmpfs", "devtmpfs", "devpts", "cgroup", "cgroup2", "overlay",
        "squashfs", "autofs", "mqueue", "debugfs", "tracefs", "fusectl", "configfs",
        "pstore", "bpf", "hugetlbfs", "ramfs", "securityfs", "binfmt_misc",
    };

    private static readonly string[] PseudoMountPrefixes =
        ["/proc", "/sys", "/dev", "/run", "/snap", "/boot"];

    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public Task ScanAsync(IProgress<DiscoveredRepository> onFound, CancellationToken cancellationToken) =>
        ScanAsync(GetScanRoots(), onFound, cancellationToken);

    public Task ScanAsync(
        IReadOnlyList<string> roots,
        IProgress<DiscoveredRepository> onFound,
        CancellationToken cancellationToken) =>
        Task.Run(() => ScanCore(roots, onFound, cancellationToken), cancellationToken);

    private async Task ScanCore(
        IReadOnlyList<string> roots,
        IProgress<DiscoveredRepository> onFound,
        CancellationToken cancellationToken)
    {
        var priorityQueue = new ConcurrentQueue<string>();
        var normalQueue = new ConcurrentQueue<string>();
        var available = new SemaphoreSlim(0);
        var pending = 0;
        var done = false;

        var workerCount = Math.Max(2, Environment.ProcessorCount);

        void Enqueue(string directory, bool priority)
        {
            Interlocked.Increment(ref pending);
            if (priority)
            {
                priorityQueue.Enqueue(directory);
            }
            else
            {
                normalQueue.Enqueue(directory);
            }

            available.Release();
        }

        foreach (var root in roots)
        {
            Enqueue(root, priority: true);
        }

        // Nothing to scan.
        if (Volatile.Read(ref pending) == 0)
        {
            return;
        }

        async Task WorkerAsync()
        {
            while (true)
            {
                await available.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (Volatile.Read(ref done))
                {
                    return;
                }

                if (!priorityQueue.TryDequeue(out var directory) && !normalQueue.TryDequeue(out directory))
                {
                    continue;
                }

                try
                {
                    ProcessDirectory(directory, onFound, Enqueue, cancellationToken);
                }
                catch
                {
                    // Per-directory failures are non-fatal; keep scanning.
                }
                finally
                {
                    if (Interlocked.Decrement(ref pending) == 0)
                    {
                        done = true;
                        available.Release(workerCount); // wake all workers so they observe completion
                    }
                }
            }
        }

        var workers = Enumerable.Range(0, workerCount).Select(_ => WorkerAsync()).ToArray();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled: workers exited via the cancellation token.
        }
    }

    private static void ProcessDirectory(
        string directory,
        IProgress<DiscoveredRepository> onFound,
        Action<string, bool> enqueue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var childDirectories = new List<string>();
        DateTime? gitLastWriteUtc = null;

        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", EnumOptions))
        {
            if (string.Equals(entry.Name, ".git", StringComparison.OrdinalIgnoreCase))
            {
                // Repo marker (either a .git directory or a .git file). Prune: stop here.
                gitLastWriteUtc = entry.LastWriteTimeUtc;
                break;
            }

            if ((entry.Attributes & FileAttributes.Directory) != 0 && !ExcludedNames.Contains(entry.Name))
            {
                childDirectories.Add(entry.FullName);
            }
        }

        if (gitLastWriteUtc is not null)
        {
            onFound.Report(new DiscoveredRepository(directory, gitLastWriteUtc.Value));
            return; // do not descend into a repository
        }

        foreach (var child in childDirectories)
        {
            var name = Path.GetFileName(child);
            var priority = PriorityNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            enqueue(child, priority);
        }
    }

    public static IReadOnlyList<string> GetScanRoots()
    {
        var roots = new List<string>();

        foreach (var drive in SafeGetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                var mountPath = drive.RootDirectory.FullName;

                if (!OperatingSystem.IsWindows())
                {
                    if (IsPseudoFormat(drive) || IsPseudoMount(mountPath))
                    {
                        continue;
                    }
                }

                roots.Add(mountPath);
            }
            catch
            {
                // Skip drives that throw while being inspected.
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
        {
            roots.Add(home);
        }

        return Deduplicate(roots);
    }

    private static DriveInfo[] SafeGetDrives()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsPseudoFormat(DriveInfo drive)
    {
        try
        {
            return PseudoFilesystemFormats.Contains(drive.DriveFormat);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPseudoMount(string mountPath) =>
        PseudoMountPrefixes.Any(prefix =>
            mountPath.Equals(prefix, StringComparison.Ordinal) ||
            mountPath.StartsWith(prefix + "/", StringComparison.Ordinal));

    private static IReadOnlyList<string> Deduplicate(IEnumerable<string> roots)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // Keep shortest paths first so nested roots are dropped in favour of their parent.
        var ordered = roots
            .Select(NormalizeRoot)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderBy(path => path.Length)
            .ToList();

        var accepted = new List<string>();
        foreach (var root in ordered)
        {
            var covered = accepted.Any(parent =>
                root.Equals(parent, comparison) ||
                root.StartsWith(EnsureTrailingSeparator(parent), comparison));

            if (!covered)
            {
                accepted.Add(root);
            }
        }

        return accepted;
    }

    private static string NormalizeRoot(string path)
    {
        // Preserve a drive/filesystem root's trailing separator (e.g. "C:\" or "/"), otherwise trim it.
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? path : trimmed;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
