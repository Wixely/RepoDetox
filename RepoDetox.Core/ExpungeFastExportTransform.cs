using System.Text;

namespace RepoDetox;

/// <summary>
/// Replaces literal secret strings with a fixed token everywhere they appear in history — in file
/// (blob) contents and, optionally, commit/tag messages. Matching is exact, case-sensitive byte
/// matching. Used to scrub accidentally-committed secrets from a repository's git history.
/// </summary>
public sealed class ExpungeFastExportTransform : FastExportTransform
{
    private readonly byte[][] _patterns;
    private readonly byte[] _replacement;
    private readonly bool _includeMessages;

    public ExpungeFastExportTransform(IReadOnlyList<string> secrets, string replacement, bool includeMessages)
    {
        _patterns = secrets
            .Where(secret => !string.IsNullOrEmpty(secret))
            .Select(secret => Encoding.UTF8.GetBytes(secret))
            .Where(pattern => pattern.Length > 0)
            .OrderByDescending(pattern => pattern.Length) // longest match wins on overlap
            .ToArray();

        _replacement = Encoding.UTF8.GetBytes(replacement);
        _includeMessages = includeMessages;
    }

    protected override bool RewritesData => _patterns.Length > 0;

    protected override byte[] TransformData(byte[] data, DataKind kind)
    {
        if (kind != DataKind.Blob && !_includeMessages)
        {
            return data;
        }

        if (!ContainsAnyPattern(data))
        {
            return data;
        }

        return ReplaceAll(data);
    }

    private bool ContainsAnyPattern(byte[] data)
    {
        var span = data.AsSpan();
        foreach (var pattern in _patterns)
        {
            if (span.IndexOf(pattern) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private byte[] ReplaceAll(byte[] data)
    {
        using var output = new MemoryStream(data.Length);

        var index = 0;
        while (index < data.Length)
        {
            var matchLength = MatchLengthAt(data, index);
            if (matchLength > 0)
            {
                output.Write(_replacement, 0, _replacement.Length);
                index += matchLength;
            }
            else
            {
                output.WriteByte(data[index]);
                index++;
            }
        }

        return output.ToArray();
    }

    private int MatchLengthAt(byte[] data, int index)
    {
        var remaining = data.Length - index;
        foreach (var pattern in _patterns)
        {
            if (pattern.Length <= remaining
                && data.AsSpan(index, pattern.Length).SequenceEqual(pattern))
            {
                return pattern.Length;
            }
        }

        return 0;
    }
}
