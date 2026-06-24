using System.Security.Cryptography;
using System.Text;

namespace RepoDetox;

/// <summary>
/// Rewrites the name and/or email in every <c>author</c>, <c>committer</c>, and
/// <c>tagger</c> line so commit/tag identities are anonymised. The hashing reproduces the
/// previous git-filter-repo callbacks byte-for-byte:
/// <list type="bullet">
/// <item><c>anonymous-user-</c> + first 12 lowercase hex chars of SHA-256(name bytes)</item>
/// <item><c>anonymous-email-</c> + first 12 lowercase hex chars of SHA-256(email bytes) + <c>@example.invalid</c></item>
/// </list>
/// </summary>
public sealed class AnonymiseFastExportTransform(bool anonymiseUsers, bool anonymiseEmails) : FastExportTransform
{
    private static readonly byte[] UserPrefix = "anonymous-user-"u8.ToArray();
    private static readonly byte[] EmailPrefix = "anonymous-email-"u8.ToArray();
    private static readonly byte[] EmailSuffix = "@example.invalid"u8.ToArray();

    protected override byte[] TransformIdentity(byte[] line, IdentityKind kind)
    {
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

        using var output = new MemoryStream(line.Length + 32);
        output.Write(line, 0, valueStart); // keyword + ' '
        WriteBytes(output, anonymiseUsers ? AnonymiseName(name) : name);
        WriteBytes(output, middle);
        WriteBytes(output, anonymiseEmails ? AnonymiseEmail(email) : email);
        WriteBytes(output, suffix);

        return output.ToArray();
    }

    private static byte[] AnonymiseName(byte[] name)
    {
        var hash = ShortHashAscii(name);
        var result = new byte[UserPrefix.Length + hash.Length];
        Buffer.BlockCopy(UserPrefix, 0, result, 0, UserPrefix.Length);
        Buffer.BlockCopy(hash, 0, result, UserPrefix.Length, hash.Length);
        return result;
    }

    private static byte[] AnonymiseEmail(byte[] email)
    {
        var hash = ShortHashAscii(email);
        var result = new byte[EmailPrefix.Length + hash.Length + EmailSuffix.Length];
        Buffer.BlockCopy(EmailPrefix, 0, result, 0, EmailPrefix.Length);
        Buffer.BlockCopy(hash, 0, result, EmailPrefix.Length, hash.Length);
        Buffer.BlockCopy(EmailSuffix, 0, result, EmailPrefix.Length + hash.Length, EmailSuffix.Length);
        return result;
    }

    private static byte[] ShortHashAscii(byte[] input)
    {
        var hash = SHA256.HashData(input);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return Encoding.ASCII.GetBytes(hex[..12]);
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
