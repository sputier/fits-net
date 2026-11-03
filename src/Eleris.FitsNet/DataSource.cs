namespace Eleris.FitsNet;

/// <summary>Where the (unpadded) data bytes of an HDU live.</summary>
internal abstract class DataSource
{
    public abstract long Length { get; }

    public abstract byte[] ReadAll();

    /// <summary>The data without copying when it is already in memory (read-only: callers must not modify it).</summary>
    public virtual ReadOnlyMemory<byte> ReadMemory() => ReadAll();

    /// <summary>Fills <paramref name="destination"/> with the bytes starting at <paramref name="offset"/> within the data.</summary>
    public abstract void ReadRange(long offset, Span<byte> destination);

    /// <summary>Reads <paramref name="length"/> bytes starting at <paramref name="offset"/> within the data.</summary>
    public byte[] ReadRange(long offset, int length)
    {
        var buffer = new byte[length];
        ReadRange(offset, buffer.AsSpan());
        return buffer;
    }

    public abstract void CopyTo(Stream destination);

    /// <summary>The number of values of <paramref name="elementSize"/> bytes that the data holds, as the length of an array.</summary>
    public int ElementCount(int elementSize)
    {
        if (Length % elementSize != 0) throw new FitsException("Data length is not a multiple of the element size.");
        if (Length / elementSize > Array.MaxLength) throw new FitsException($"HDU data holds more than {Array.MaxLength} values, more than an array can hold.");
        return (int)(Length / elementSize);
    }

    /// <summary>Receives a part of the data: whole elements, <paramref name="firstElement"/> being the index of the first.</summary>
    public delegate void ChunkReader(ReadOnlySpan<byte> bytes, int firstElement);

    // Small enough to stay in the cache between the read and the conversion, large enough to make the reads of a file cheap.
    private const int ChunkBytes = 256 * 1024;

    /// <summary>
    /// Passes the data to <paramref name="reader"/> in chunks of whole elements. Data in memory is not copied;
    /// data in a file is read through one small buffer instead of a copy of the whole data.
    /// </summary>
    public void ReadInChunks(int elementSize, ChunkReader reader)
    {
        int count = ElementCount(elementSize);
        if (count == 0) return;
        int perChunk = Math.Max(1, ChunkBytes / elementSize);
        if (this is not StreamDataSource)
        {
            ReadOnlySpan<byte> all = ReadMemory().Span;
            for (int first = 0; first < count; first += perChunk)
                reader(all.Slice(first * elementSize, Math.Min(perChunk, count - first) * elementSize), first);
            return;
        }
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(perChunk * elementSize);
        try
        {
            for (int first = 0; first < count; first += perChunk)
            {
                int length = Math.Min(perChunk, count - first) * elementSize;
                ReadRange((long)first * elementSize, buffer.AsSpan(0, length));
                reader(buffer.AsSpan(0, length), first);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static DataSource Empty { get; } = new MemoryDataSource([]);
}

internal sealed class MemoryDataSource(byte[] data) : DataSource
{
    public override long Length => data.Length;

    public override byte[] ReadAll() => data;

    public override ReadOnlyMemory<byte> ReadMemory() => data;

    public override void ReadRange(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > data.Length || destination.Length > data.Length - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        data.AsSpan((int)offset, destination.Length).CopyTo(destination);
    }

    public override void CopyTo(Stream destination) => destination.Write(data);
}

/// <summary>A slice of a larger buffer shared by all the HDUs of a file read into memory (no per-HDU copy).</summary>
internal sealed class SegmentDataSource(byte[] buffer, int offset, int length) : DataSource
{
    public override long Length => length;

    public override byte[] ReadAll() => buffer.AsSpan(offset, length).ToArray();

    public override ReadOnlyMemory<byte> ReadMemory() => buffer.AsMemory(offset, length);

    public override void ReadRange(long start, Span<byte> destination)
    {
        if (start < 0 || start > length || destination.Length > length - start) throw new ArgumentOutOfRangeException(nameof(start));
        buffer.AsSpan(offset + (int)start, destination.Length).CopyTo(destination);
    }

    public override void CopyTo(Stream destination) => destination.Write(buffer, offset, length);
}

/// <summary>Lazily reads a byte range of a seekable stream (typically the source file). Not safe for concurrent use.</summary>
internal sealed class StreamDataSource(Stream stream, long offset, long length) : DataSource
{
    public Stream Stream => stream;

    public long Offset => offset;

    public override long Length => length;

    public override byte[] ReadAll()
    {
        if (length > Array.MaxLength) throw new FitsException("HDU data is larger than 2 GB and cannot be loaded into one array.");
        return ReadRange(0, (int)length);
    }

    public override void ReadRange(long start, Span<byte> destination)
    {
        if (start < 0 || start > length || destination.Length > length - start) throw new ArgumentOutOfRangeException(nameof(start));
        stream.Position = offset + start;
        try
        {
            stream.ReadExactly(destination);
        }
        catch (EndOfStreamException)
        {
            throw new FitsException("The file ends before the end of the data announced by the header.");
        }
    }

    public override void CopyTo(Stream destination)
    {
        var buffer = new byte[81920];
        stream.Position = offset;
        long remaining = length;
        while (remaining > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) throw new FitsException("Unexpected end of stream while copying HDU data.");
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }
}
