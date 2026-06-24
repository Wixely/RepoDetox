using System.Text;

namespace RepoDetox;

/// <summary>
/// Byte-oriented buffered writer over a <c>git fast-import</c> stdin stream.
/// Writes raw bytes only; never performs text translation (the fast-import stream
/// uses bare line feeds even on Windows).
/// </summary>
public sealed class FastImportWriter(Stream stream)
{
    /// <summary>Writes the given bytes followed by a single line feed.</summary>
    public void WriteLine(ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
    }

    /// <summary>Writes an ASCII string followed by a single line feed.</summary>
    public void WriteLine(string asciiText) => WriteLine(Encoding.ASCII.GetBytes(asciiText));

    /// <summary>Writes raw bytes verbatim.</summary>
    public void WriteRaw(ReadOnlySpan<byte> bytes) => stream.Write(bytes);

    /// <summary>Writes a single raw byte verbatim.</summary>
    public void WriteByte(byte value) => stream.WriteByte(value);

    /// <summary>Streams exactly <paramref name="count"/> raw bytes from the reader through to import.</summary>
    public void CopyDataFrom(FastExportReader reader, long count) => reader.CopyExact(count, stream);

    public void Flush() => stream.Flush();
}
