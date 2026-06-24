namespace RepoDetox;

/// <summary>
/// Byte-oriented buffered reader over a <c>git fast-export</c> stdout stream.
/// The fast-export stream is binary: <c>data &lt;n&gt;</c> blocks are length-prefixed
/// raw bytes that may contain newlines or text resembling stream keywords, so this
/// reader never decodes text. Top-level keywords are always ASCII and are matched on
/// the raw line bytes.
/// </summary>
public sealed class FastExportReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _bufferStart;
    private int _bufferEnd;

    private byte[]? _pushedBackLine;
    private bool _hasPushedBackLine;

    public FastExportReader(Stream stream, int bufferSize = 1 << 16)
    {
        _stream = stream;
        _buffer = new byte[bufferSize];
    }

    /// <summary>
    /// Reads the next line, returning its bytes without the trailing line feed.
    /// Returns <c>null</c> at end of stream.
    /// </summary>
    public byte[]? ReadRawLine()
    {
        if (_hasPushedBackLine)
        {
            _hasPushedBackLine = false;
            var line = _pushedBackLine;
            _pushedBackLine = null;
            return line;
        }

        return ReadLineFromStream();
    }

    /// <summary>
    /// Returns the next line without consuming it. Returns <c>null</c> at end of stream.
    /// </summary>
    public byte[]? PeekLine()
    {
        if (!_hasPushedBackLine)
        {
            _pushedBackLine = ReadLineFromStream();
            _hasPushedBackLine = true;
        }

        return _pushedBackLine;
    }

    /// <summary>
    /// Copies exactly <paramref name="count"/> raw bytes from the stream to
    /// <paramref name="destination"/>. Used to stream <c>data</c> payloads without
    /// materializing large blobs in memory.
    /// </summary>
    public void CopyExact(long count, Stream destination)
    {
        EnsureNoBufferedLine();

        var remaining = count;
        while (remaining > 0)
        {
            if (_bufferStart >= _bufferEnd && !RefillBuffer())
            {
                throw new EndOfStreamException(
                    "Unexpected end of fast-export stream while reading a data block.");
            }

            var available = _bufferEnd - _bufferStart;
            var toCopy = (int)Math.Min(available, remaining);
            destination.Write(_buffer, _bufferStart, toCopy);
            _bufferStart += toCopy;
            remaining -= toCopy;
        }
    }

    /// <summary>
    /// Returns the next raw byte without consuming it, or -1 at end of stream.
    /// Used to detect the optional line feed that fast-export emits after a data block.
    /// </summary>
    public int PeekByte()
    {
        EnsureNoBufferedLine();

        if (_bufferStart >= _bufferEnd && !RefillBuffer())
        {
            return -1;
        }

        return _buffer[_bufferStart];
    }

    /// <summary>Consumes a single raw byte previously observed via <see cref="PeekByte"/>.</summary>
    public void SkipByte()
    {
        EnsureNoBufferedLine();

        if (_bufferStart >= _bufferEnd && !RefillBuffer())
        {
            throw new EndOfStreamException("Unexpected end of fast-export stream.");
        }

        _bufferStart++;
    }

    private void EnsureNoBufferedLine()
    {
        if (_hasPushedBackLine)
        {
            throw new InvalidOperationException(
                "Cannot read raw bytes while a peeked line is buffered.");
        }
    }

    private bool RefillBuffer()
    {
        _bufferStart = 0;
        _bufferEnd = _stream.Read(_buffer, 0, _buffer.Length);
        return _bufferEnd > 0;
    }

    private byte[]? ReadLineFromStream()
    {
        // Fast path: the whole line is already inside the current buffer window.
        if (_bufferStart >= _bufferEnd && !RefillBuffer())
        {
            return null;
        }

        var newlineIndex = IndexOfNewline();
        if (newlineIndex >= 0)
        {
            var line = Slice(_bufferStart, newlineIndex);
            _bufferStart = newlineIndex + 1;
            return line;
        }

        // Slow path: the line spans multiple buffer fills.
        var builder = new MemoryStream();
        builder.Write(_buffer, _bufferStart, _bufferEnd - _bufferStart);
        _bufferStart = _bufferEnd;

        while (true)
        {
            if (!RefillBuffer())
            {
                // End of stream with a final line that has no trailing line feed.
                return builder.ToArray();
            }

            newlineIndex = IndexOfNewline();
            if (newlineIndex >= 0)
            {
                builder.Write(_buffer, _bufferStart, newlineIndex - _bufferStart);
                _bufferStart = newlineIndex + 1;
                return builder.ToArray();
            }

            builder.Write(_buffer, _bufferStart, _bufferEnd - _bufferStart);
            _bufferStart = _bufferEnd;
        }
    }

    private int IndexOfNewline()
    {
        for (var index = _bufferStart; index < _bufferEnd; index++)
        {
            if (_buffer[index] == (byte)'\n')
            {
                return index;
            }
        }

        return -1;
    }

    private byte[] Slice(int start, int endExclusive)
    {
        var line = new byte[endExclusive - start];
        Array.Copy(_buffer, start, line, 0, line.Length);
        return line;
    }
}
