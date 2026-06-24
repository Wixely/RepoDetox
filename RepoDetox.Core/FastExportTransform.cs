namespace RepoDetox;

/// <summary>
/// Streams a <c>git fast-export</c> data stream through to <c>git fast-import</c>,
/// passing every command through verbatim by default. Subclasses override the hooks to
/// rewrite commit/tag identities (anonymise) or drop file-change operations (vacuum).
/// </summary>
/// <remarks>
/// The fast-import stream format is documented at
/// https://git-scm.com/docs/git-fast-import (Input Format). This is an original
/// implementation derived from that public specification; no third-party code is used.
/// </remarks>
public abstract class FastExportTransform
{
    protected enum IdentityKind
    {
        Author,
        Committer,
        Tagger,
    }

    protected enum DataKind
    {
        Blob,
        CommitMessage,
        TagMessage,
    }

    // Payloads larger than this are streamed through unmodified even when RewritesData is true,
    // to avoid buffering an enormous blob in memory.
    private const long MaxRewritableDataLength = int.MaxValue;

    /// <summary>
    /// Drives the full transform. Returns <c>true</c> if the terminating <c>done</c>
    /// command was seen, which (with <c>--use-done-feature</c>) confirms the export was
    /// not truncated.
    /// </summary>
    public bool Process(FastExportReader reader, FastImportWriter writer)
    {
        var sawDone = false;
        byte[]? line;

        while ((line = reader.ReadRawLine()) is not null)
        {
            if (line.Length == 0)
            {
                writer.WriteLine(line);
                continue;
            }

            if (StartsWith(line, "blob"u8))
            {
                ProcessBlob(line, reader, writer);
            }
            else if (StartsWith(line, "commit "u8))
            {
                ProcessCommit(line, reader, writer);
            }
            else if (StartsWith(line, "tag "u8))
            {
                ProcessTag(line, reader, writer);
            }
            else if (StartsWith(line, "reset "u8))
            {
                ProcessReset(line, reader, writer);
            }
            else if (LineEquals(line, "done"u8))
            {
                writer.WriteLine(line);
                sawDone = true;
            }
            else
            {
                // feature, option, progress, comments, and any other top-level line.
                writer.WriteLine(line);
            }
        }

        return sawDone;
    }

    /// <summary>Rewrites an identity line (<c>author</c>/<c>committer</c>/<c>tagger</c>). Default: unchanged.</summary>
    protected virtual byte[] TransformIdentity(byte[] line, IdentityKind kind) => line;

    /// <summary>Decides whether a file-change line (<c>M</c>/<c>D</c>/<c>C</c>/<c>R</c>) is kept. Default: kept.</summary>
    protected virtual bool ShouldKeepFileChange(byte[] line) => true;

    /// <summary>
    /// When <c>true</c>, every <c>data</c> payload (blob content, commit/tag message) is buffered
    /// and passed to <see cref="TransformData"/> so its bytes can be rewritten. Default <c>false</c>
    /// (payloads stream through untouched).
    /// </summary>
    protected virtual bool RewritesData => false;

    /// <summary>Rewrites a buffered <c>data</c> payload. Default: returned unchanged.</summary>
    protected virtual byte[] TransformData(byte[] data, DataKind kind) => data;

    private void ProcessBlob(byte[] blobLine, FastExportReader reader, FastImportWriter writer)
    {
        writer.WriteLine(blobLine);

        while (true)
        {
            var line = reader.ReadRawLine();
            if (line is null)
            {
                return;
            }

            if (StartsWith(line, "data "u8))
            {
                EmitData(line, DataKind.Blob, reader, writer);
                return;
            }

            // mark, original-oid
            writer.WriteLine(line);
        }
    }

    private void ProcessCommit(byte[] commitLine, FastExportReader reader, FastImportWriter writer)
    {
        writer.WriteLine(commitLine);

        while (true)
        {
            var line = reader.PeekLine();
            if (line is null)
            {
                return;
            }

            if (line.Length == 0)
            {
                reader.ReadRawLine();
                writer.WriteLine(line);
                return;
            }

            if (StartsWith(line, "author "u8))
            {
                reader.ReadRawLine();
                writer.WriteLine(TransformIdentity(line, IdentityKind.Author));
            }
            else if (StartsWith(line, "committer "u8))
            {
                reader.ReadRawLine();
                writer.WriteLine(TransformIdentity(line, IdentityKind.Committer));
            }
            else if (StartsWith(line, "data "u8))
            {
                reader.ReadRawLine();
                EmitData(line, DataKind.CommitMessage, reader, writer);
            }
            else if (StartsWith(line, "M "u8))
            {
                reader.ReadRawLine();
                ProcessModify(line, reader, writer);
            }
            else if (StartsWith(line, "D "u8) || StartsWith(line, "C "u8) || StartsWith(line, "R "u8))
            {
                reader.ReadRawLine();
                if (ShouldKeepFileChange(line))
                {
                    writer.WriteLine(line);
                }
            }
            else if (StartsWith(line, "mark "u8)
                  || StartsWith(line, "original-oid "u8)
                  || StartsWith(line, "encoding "u8)
                  || StartsWith(line, "from "u8)
                  || StartsWith(line, "merge "u8)
                  || LineEquals(line, "deleteall"u8))
            {
                reader.ReadRawLine();
                writer.WriteLine(line);
            }
            else
            {
                // Start of the next top-level command; leave it for the outer loop.
                return;
            }
        }
    }

    private void ProcessModify(byte[] line, FastExportReader reader, FastImportWriter writer)
    {
        var keep = ShouldKeepFileChange(line);
        var isInline = FastExportPaths.IsInlineModify(line);

        if (keep)
        {
            writer.WriteLine(line);
        }

        if (!isInline)
        {
            return;
        }

        var dataLine = reader.ReadRawLine();
        if (dataLine is null)
        {
            return;
        }

        if (keep)
        {
            EmitData(dataLine, DataKind.Blob, reader, writer);
        }
        else
        {
            DiscardData(dataLine, reader, writer);
        }
    }

    private void ProcessTag(byte[] tagLine, FastExportReader reader, FastImportWriter writer)
    {
        writer.WriteLine(tagLine);

        while (true)
        {
            var line = reader.PeekLine();
            if (line is null)
            {
                return;
            }

            if (line.Length == 0)
            {
                reader.ReadRawLine();
                writer.WriteLine(line);
                return;
            }

            if (StartsWith(line, "tagger "u8))
            {
                reader.ReadRawLine();
                writer.WriteLine(TransformIdentity(line, IdentityKind.Tagger));
            }
            else if (StartsWith(line, "data "u8))
            {
                reader.ReadRawLine();
                EmitData(line, DataKind.TagMessage, reader, writer);
            }
            else if (StartsWith(line, "mark "u8)
                  || StartsWith(line, "from "u8)
                  || StartsWith(line, "original-oid "u8))
            {
                reader.ReadRawLine();
                writer.WriteLine(line);
            }
            else
            {
                return;
            }
        }
    }

    private void ProcessReset(byte[] resetLine, FastExportReader reader, FastImportWriter writer)
    {
        writer.WriteLine(resetLine);

        var next = reader.PeekLine();
        if (next is not null && StartsWith(next, "from "u8))
        {
            reader.ReadRawLine();
            writer.WriteLine(next);
        }
    }

    private void EmitData(byte[] dataLine, DataKind kind, FastExportReader reader, FastImportWriter writer)
    {
        var count = ParseDataCount(dataLine);

        if (!RewritesData || count > MaxRewritableDataLength)
        {
            // Stream the payload straight through without buffering.
            writer.WriteLine(dataLine);
            writer.CopyDataFrom(reader, count);
            ConsumeOptionalTrailingNewline(reader, writer, emit: true);
            return;
        }

        using var buffer = new MemoryStream((int)count);
        reader.CopyExact(count, buffer);
        var transformed = TransformData(buffer.ToArray(), kind);

        writer.WriteLine($"data {transformed.Length}");
        writer.WriteRaw(transformed);
        ConsumeOptionalTrailingNewline(reader, writer, emit: true);
    }

    private static void DiscardData(byte[] dataLine, FastExportReader reader, FastImportWriter writer)
    {
        var count = ParseDataCount(dataLine);
        reader.CopyExact(count, Stream.Null);
        ConsumeOptionalTrailingNewline(reader, writer, emit: false);
    }

    private static void ConsumeOptionalTrailingNewline(FastExportReader reader, FastImportWriter writer, bool emit)
    {
        // fast-export emits an optional line feed after each data payload for readability.
        if (reader.PeekByte() == '\n')
        {
            reader.SkipByte();
            if (emit)
            {
                writer.WriteByte((byte)'\n');
            }
        }
    }

    private static long ParseDataCount(byte[] dataLine)
    {
        const int prefixLength = 5; // "data "
        if (dataLine.Length > prefixLength && dataLine[prefixLength] == (byte)'<')
        {
            throw new NotSupportedException(
                "Delimited 'data <<' blocks are not supported; git fast-export emits the counted form.");
        }

        long value = 0;
        for (var index = prefixLength; index < dataLine.Length; index++)
        {
            var digit = dataLine[index];
            if (digit is < (byte)'0' or > (byte)'9')
            {
                break;
            }

            value = (value * 10) + (digit - (byte)'0');
        }

        return value;
    }

    protected static bool StartsWith(byte[] line, ReadOnlySpan<byte> prefix) =>
        line.AsSpan().StartsWith(prefix);

    protected static bool LineEquals(byte[] line, ReadOnlySpan<byte> value) =>
        line.AsSpan().SequenceEqual(value);
}
