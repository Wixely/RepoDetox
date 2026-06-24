namespace RepoDetox;

/// <summary>
/// Helpers for parsing path operands out of fast-export file-change lines
/// (<c>M</c>, <c>D</c>, <c>C</c>, <c>R</c>) and for decoding git's C-style path quoting.
/// All operations work on raw bytes so non-UTF-8 paths are matched faithfully.
/// </summary>
public static class FastExportPaths
{
    /// <summary>
    /// Parses the path from a <c>M &lt;mode&gt; &lt;dataref&gt; &lt;path&gt;</c> line and reports
    /// whether the data reference is <c>inline</c> (meaning a data block follows).
    /// The line is expected to start with <c>"M "</c>.
    /// </summary>
    public static byte[] ParseModifyPath(byte[] line, out bool isInline)
    {
        // Skip "M ", then the mode token, then the dataref token; the remainder is the path.
        var index = 2;
        index = SkipToken(line, index, out _);
        index = SkipSpaces(line, index);
        index = SkipToken(line, index, out var datarefStart);
        var datarefLength = TokenLengthBefore(line, datarefStart, index);
        isInline = SpanEquals(line, datarefStart, datarefLength, "inline"u8);

        index = SkipSpaces(line, index);
        return ReadPathOperand(line, index, out _);
    }

    /// <summary>
    /// Returns true if a <c>M</c> line uses an <c>inline</c> data reference, meaning a
    /// <c>data</c> block follows it in the stream.
    /// </summary>
    public static bool IsInlineModify(byte[] line)
    {
        // Skip "M ", then the mode token; the next token is the dataref.
        var index = 2;
        index = SkipToken(line, index, out _);
        index = SkipSpaces(line, index);
        var datarefStart = index;
        index = SkipToken(line, index, out _);
        return SpanEquals(line, datarefStart, index - datarefStart, "inline"u8);
    }

    /// <summary>Parses the path from a <c>D &lt;path&gt;</c> line. The line starts with <c>"D "</c>.</summary>
    public static byte[] ParseDeletePath(byte[] line) => ReadPathOperand(line, 2, out _);

    /// <summary>
    /// Parses both operands from a <c>C &lt;src&gt; &lt;dest&gt;</c> or
    /// <c>R &lt;src&gt; &lt;dest&gt;</c> line. The line starts with <c>"C "</c> or <c>"R "</c>.
    /// </summary>
    public static (byte[] Source, byte[] Destination) ParseCopyOrRenamePaths(byte[] line)
    {
        var source = ReadPathOperand(line, 2, out var afterSource);
        var destStart = SkipSpaces(line, afterSource);
        var destination = ReadPathOperand(line, destStart, out _);
        return (source, destination);
    }

    /// <summary>
    /// Reads a single path operand starting at <paramref name="start"/>. Handles both the
    /// C-quoted form (<c>"..."</c>) and the bare form (runs to the next space or end of line),
    /// returning the decoded raw path bytes. <paramref name="indexAfter"/> is set to the index
    /// just past the operand.
    /// </summary>
    public static byte[] ReadPathOperand(byte[] line, int start, out int indexAfter)
    {
        if (start < line.Length && line[start] == (byte)'"')
        {
            var index = start + 1;
            while (index < line.Length)
            {
                if (line[index] == (byte)'\\')
                {
                    index += 2;
                    continue;
                }

                if (line[index] == (byte)'"')
                {
                    break;
                }

                index++;
            }

            var quotedLength = index - start + 1; // include the closing quote
            indexAfter = index + 1;
            return Unquote(line, start, quotedLength);
        }

        var bareStart = start;
        var bareIndex = start;
        while (bareIndex < line.Length && line[bareIndex] != (byte)' ')
        {
            bareIndex++;
        }

        indexAfter = bareIndex;
        var bare = new byte[bareIndex - bareStart];
        Array.Copy(line, bareStart, bare, 0, bare.Length);
        return bare;
    }

    /// <summary>
    /// Decodes a git C-style quoted token (including the surrounding double quotes) into raw bytes.
    /// </summary>
    public static byte[] Unquote(byte[] line, int start, int length)
    {
        // Strip the surrounding quotes.
        var contentStart = start + 1;
        var contentEnd = start + length - 1;
        var output = new MemoryStream(length);

        var index = contentStart;
        while (index < contentEnd)
        {
            var current = line[index];
            if (current != (byte)'\\')
            {
                output.WriteByte(current);
                index++;
                continue;
            }

            index++;
            if (index >= contentEnd)
            {
                break;
            }

            var escape = line[index];
            switch (escape)
            {
                case (byte)'a': output.WriteByte(0x07); index++; break;
                case (byte)'b': output.WriteByte(0x08); index++; break;
                case (byte)'f': output.WriteByte(0x0c); index++; break;
                case (byte)'n': output.WriteByte((byte)'\n'); index++; break;
                case (byte)'r': output.WriteByte((byte)'\r'); index++; break;
                case (byte)'t': output.WriteByte((byte)'\t'); index++; break;
                case (byte)'v': output.WriteByte(0x0b); index++; break;
                case (byte)'"': output.WriteByte((byte)'"'); index++; break;
                case (byte)'\\': output.WriteByte((byte)'\\'); index++; break;
                default:
                    if (escape is >= (byte)'0' and <= (byte)'7')
                    {
                        var value = 0;
                        var digits = 0;
                        while (digits < 3 && index < contentEnd && line[index] is >= (byte)'0' and <= (byte)'7')
                        {
                            value = (value * 8) + (line[index] - (byte)'0');
                            index++;
                            digits++;
                        }

                        output.WriteByte((byte)value);
                    }
                    else
                    {
                        // Unknown escape: emit the byte literally.
                        output.WriteByte(escape);
                        index++;
                    }

                    break;
            }
        }

        return output.ToArray();
    }

    private static int SkipToken(byte[] line, int index, out int tokenStart)
    {
        tokenStart = index;
        while (index < line.Length && line[index] != (byte)' ')
        {
            index++;
        }

        return index;
    }

    private static int SkipSpaces(byte[] line, int index)
    {
        while (index < line.Length && line[index] == (byte)' ')
        {
            index++;
        }

        return index;
    }

    private static int TokenLengthBefore(byte[] line, int tokenStart, int tokenEnd) => tokenEnd - tokenStart;

    private static bool SpanEquals(byte[] line, int start, int length, ReadOnlySpan<byte> expected)
    {
        if (length != expected.Length)
        {
            return false;
        }

        for (var offset = 0; offset < length; offset++)
        {
            if (line[start + offset] != expected[offset])
            {
                return false;
            }
        }

        return true;
    }
}
