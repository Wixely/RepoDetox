using System.Security.Cryptography;
using System.Text;

namespace RepoDetox;

/// <summary>
/// Rewrites the name and/or email in every <c>author</c>, <c>committer</c>, and
/// <c>tagger</c> line. Each side can be kept, replaced with a deterministic hash, or
/// replaced with a fixed literal value. The hashing reproduces the previous
/// git-filter-repo callbacks byte-for-byte:
/// <list type="bullet">
/// <item><c>anonymous-user-</c> + first 12 lowercase hex chars of SHA-256(name bytes)</item>
/// <item><c>anonymous-email-</c> + first 12 lowercase hex chars of SHA-256(email bytes) + <c>@example.invalid</c></item>
/// </list>
/// </summary>
public sealed class AnonymiseFastExportTransform : FastExportTransform
{
    private static readonly byte[] UserPrefix = "anonymous-user-"u8.ToArray();
    private static readonly byte[] EmailPrefix = "anonymous-email-"u8.ToArray();
    private static readonly byte[] EmailSuffix = "@example.invalid"u8.ToArray();

    private readonly IdentityRewriteMode _nameMode;
    private readonly IdentityRewriteMode _emailMode;
    private readonly byte[]? _fixedName;
    private readonly byte[]? _fixedEmail;
    private readonly Dictionary<(string Name, string Email), IdentityMapping> _mappings;

    public AnonymiseFastExportTransform(
        IdentityRewriteMode nameMode,
        IdentityRewriteMode emailMode,
        string? fixedName = null,
        string? fixedEmail = null,
        IReadOnlyList<IdentityMapping>? mappings = null)
    {
        _nameMode = nameMode;
        _emailMode = emailMode;
        _fixedName = nameMode == IdentityRewriteMode.Fixed
            ? Encoding.UTF8.GetBytes(fixedName ?? throw new ArgumentNullException(nameof(fixedName)))
            : null;
        _fixedEmail = emailMode == IdentityRewriteMode.Fixed
            ? Encoding.UTF8.GetBytes(fixedEmail ?? throw new ArgumentNullException(nameof(fixedEmail)))
            : null;

        _mappings = new Dictionary<(string, string), IdentityMapping>();
        if (mappings is not null)
        {
            foreach (var mapping in mappings)
            {
                _mappings[(mapping.SourceName, mapping.SourceEmail)] = mapping;
            }
        }
    }

    protected override byte[] TransformIdentity(byte[] line, IdentityKind kind)
    {
        if (_mappings.Count == 0
            && _nameMode == IdentityRewriteMode.Keep
            && _emailMode == IdentityRewriteMode.Keep)
        {
            return line;
        }

        var valueStart = IndexOf(line, (byte)' ', 0);
        if (valueStart < 0)
        {
            return line;
        }

        valueStart += 1;

        var emailStart = IndexOf(line, (byte)'<', valueStart);
        if (emailStart < 0)
        {
            return line;
        }

        var emailEnd = IndexOf(line, (byte)'>', emailStart + 1);
        if (emailEnd < 0)
        {
            return line;
        }

        // Move a single name/email separator space into the middle segment so the
        // preserved side keeps its exact original spacing.
        var nameEnd = emailStart;
        if (nameEnd > valueStart && line[nameEnd - 1] == (byte)' ')
        {
            nameEnd -= 1;
        }

        var name = Slice(line, valueStart, nameEnd);
        var middle = Slice(line, nameEnd, emailStart + 1); // separator space(s) + '<'
        var email = Slice(line, emailStart + 1, emailEnd);
        var suffix = Slice(line, emailEnd, line.Length);   // '>' + ' ' + timestamp + tz

        byte[] newName;
        byte[] newEmail;

        if (_mappings.Count > 0
            && _mappings.TryGetValue((Encoding.UTF8.GetString(name), Encoding.UTF8.GetString(email)), out var mapping))
        {
            // A specific-contributor mapping matched: replace with the target (keeping a side when blank).
            newName = string.IsNullOrEmpty(mapping.TargetName) ? name : Encoding.UTF8.GetBytes(mapping.TargetName);
            newEmail = string.IsNullOrEmpty(mapping.TargetEmail) ? email : Encoding.UTF8.GetBytes(mapping.TargetEmail);
        }
        else
        {
            newName = RewriteName(name);
            newEmail = RewriteEmail(email);
        }

        using var output = new MemoryStream(line.Length + 32);
        output.Write(line, 0, valueStart); // keyword + ' '
        WriteBytes(output, newName);
        WriteBytes(output, middle);
        WriteBytes(output, newEmail);
        WriteBytes(output, suffix);

        return output.ToArray();
    }

    private byte[] RewriteName(byte[] name) => _nameMode switch
    {
        IdentityRewriteMode.Hash => Concat(UserPrefix, ShortHashAscii(name)),
        IdentityRewriteMode.Fixed => _fixedName!,
        _ => name,
    };

    private byte[] RewriteEmail(byte[] email) => _emailMode switch
    {
        IdentityRewriteMode.Hash => Concat(EmailPrefix, ShortHashAscii(email), EmailSuffix),
        IdentityRewriteMode.Fixed => _fixedEmail!,
        _ => email,
    };

    private static byte[] ShortHashAscii(byte[] input)
    {
        var hash = SHA256.HashData(input);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return Encoding.ASCII.GetBytes(hex[..12]);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
        {
            length += part.Length;
        }

        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }

    private static void WriteBytes(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

    private static byte[] Slice(byte[] source, int start, int endExclusive)
    {
        var slice = new byte[endExclusive - start];
        Array.Copy(source, start, slice, 0, slice.Length);
        return slice;
    }

    private static int IndexOf(byte[] source, byte value, int start)
    {
        for (var index = start; index < source.Length; index++)
        {
            if (source[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
