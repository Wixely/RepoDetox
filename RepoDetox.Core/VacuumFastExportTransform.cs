using System.Text;

namespace RepoDetox;

/// <summary>
/// Removes a fixed set of paths from history by dropping the <c>M</c>/<c>D</c>/<c>C</c>/<c>R</c>
/// file-change operations that reference them. Matching is exact, full-path and case-sensitive
/// (the <c>literal:</c> semantics the previous git-filter-repo invocation used), performed on
/// raw unquoted path bytes so non-UTF-8 paths are handled faithfully.
/// </summary>
/// <remarks>
/// Commits that become empty are left in place; the blobs of removed paths become unreferenced
/// and are reclaimed by the <c>git gc --prune=now</c> that runs after the rewrite.
/// </remarks>
public sealed class VacuumFastExportTransform : FastExportTransform
{
    private readonly HashSet<byte[]> _removedPaths;

    public VacuumFastExportTransform(IEnumerable<string> removedPaths)
    {
        _removedPaths = new HashSet<byte[]>(ByteArrayEqualityComparer.Instance);
        foreach (var path in removedPaths)
        {
            _removedPaths.Add(Encoding.UTF8.GetBytes(path));
        }
    }

    protected override bool ShouldKeepFileChange(byte[] line)
    {
        switch (line[0])
        {
            case (byte)'M':
                return !IsRemoved(FastExportPaths.ParseModifyPath(line, out _));
            case (byte)'D':
                return !IsRemoved(FastExportPaths.ParseDeletePath(line));
            case (byte)'C':
            case (byte)'R':
                var (source, destination) = FastExportPaths.ParseCopyOrRenamePaths(line);
                return !IsRemoved(source) && !IsRemoved(destination);
            default:
                return true;
        }
    }

    private bool IsRemoved(byte[] path) => _removedPaths.Contains(path);

    private sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayEqualityComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}
