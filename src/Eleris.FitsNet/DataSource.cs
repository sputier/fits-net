using Microsoft.Win32.SafeHandles;

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

    /// <summary>
    /// Writes the data to <paramref name="destination"/>. A token that can be cancelled is checked between blocks; without one, the data
    /// is written as it always was.
    /// </summary>
    public abstract void CopyTo(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>True when the bytes are held in memory as they are stored, so that <see cref="ReadMemory"/> costs nothing.</summary>
    public virtual bool IsInMemory => false;

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
    /// data in a file (or produced on demand) is read through one small buffer instead of a copy of the whole data.
    /// <paramref name="cancellationToken"/> is checked before each chunk.
    /// </summary>
    public void ReadInChunks(int elementSize, ChunkReader reader, CancellationToken cancellationToken = default)
    {
        int count = ElementCount(elementSize);
        if (count == 0) return;
        int perChunk = Math.Max(1, ChunkBytes / elementSize);
        if (IsInMemory)
        {
            ReadOnlySpan<byte> all = ReadMemory().Span;
            for (int first = 0; first < count; first += perChunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader(all.Slice(first * elementSize, Math.Min(perChunk, count - first) * elementSize), first);
            }
            return;
        }
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(perChunk * elementSize);
        try
        {
            for (int first = 0; first < count; first += perChunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>Blocks in which data held in memory is written when the write can be cancelled.</summary>
    private protected const int CancellableWriteBlock = 1 << 20;

    /// <summary>Writes a buffer at once, or block by block checking <paramref name="cancellationToken"/> when it can be cancelled.</summary>
    private protected static void Write(Stream destination, byte[] buffer, int offset, int length, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            destination.Write(buffer, offset, length);
            return;
        }
        for (int done = 0; done < length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = Math.Min(CancellableWriteBlock, length - done);
            destination.Write(buffer, offset + done, n);
            done += n;
        }
    }
}

internal sealed class MemoryDataSource(byte[] data) : DataSource
{
    public override long Length => data.Length;

    public override byte[] ReadAll() => data;

    public override ReadOnlyMemory<byte> ReadMemory() => data;

    public override bool IsInMemory => true;

    public override void ReadRange(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > data.Length || destination.Length > data.Length - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        data.AsSpan((int)offset, destination.Length).CopyTo(destination);
    }

    public override void CopyTo(Stream destination, CancellationToken cancellationToken = default) =>
        Write(destination, data, 0, data.Length, cancellationToken);
}

/// <summary>A slice of a larger buffer shared by all the HDUs of a file read into memory (no per-HDU copy).</summary>
internal sealed class SegmentDataSource(byte[] buffer, int offset, int length) : DataSource
{
    public override long Length => length;

    public override byte[] ReadAll() => buffer.AsSpan(offset, length).ToArray();

    public override ReadOnlyMemory<byte> ReadMemory() => buffer.AsMemory(offset, length);

    public override bool IsInMemory => true;

    public override void ReadRange(long start, Span<byte> destination)
    {
        if (start < 0 || start > length || destination.Length > length - start) throw new ArgumentOutOfRangeException(nameof(start));
        buffer.AsSpan(offset + (int)start, destination.Length).CopyTo(destination);
    }

    public override void CopyTo(Stream destination, CancellationToken cancellationToken = default) =>
        Write(destination, buffer, offset, length, cancellationToken);
}

/// <summary>Lazily reads a byte range of a seekable stream (typically the source file). Not safe for concurrent use.</summary>
internal sealed class StreamDataSource(Stream stream, long offset, long length) : DataSource
{
    // Asking a FileStream for its handle flushes and seeks it: done once, not for every read.
    private readonly SafeFileHandle? _handle = stream is FileStream { CanSeek: true, IsAsync: false } file ? file.SafeFileHandle : null;

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
        const string truncated = "The file ends before the end of the data announced by the header.";
        if (_handle is not null)
        {
            // A read at an explicit offset: no seek, and no buffer filled around a few bytes (a narrow cutout reads thousands of short runs).
            long position = offset + start;
            while (destination.Length > 0)
            {
                int read = RandomAccess.Read(_handle, destination, position);
                if (read == 0) throw new FitsException(truncated);
                destination = destination[read..];
                position += read;
            }
            return;
        }
        stream.Position = offset + start;
        try
        {
            stream.ReadExactly(destination);
        }
        catch (EndOfStreamException)
        {
            throw new FitsException(truncated);
        }
    }

    public override void CopyTo(Stream destination, CancellationToken cancellationToken = default)
    {
        // Large blocks, read at their offset: a few big reads instead of one every 80 KB.
        const int block = 1 << 20;
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent((int)Math.Min(block, Math.Max(length, 1)));
        try
        {
            for (long done = 0; done < length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int n = (int)Math.Min(buffer.Length, length - done);
                ReadRange(done, buffer.AsSpan(0, n));
                destination.Write(buffer, 0, n);
                done += n;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>
/// The data of an image given as planes of native values, which are not copied: the big-endian bytes are produced from them, a block at a
/// time, each time the data is read or written (see <see cref="ImageHdu.CreateFromPlanes{T}"/>).
/// </summary>
internal sealed class PlaneDataSource<T> : DataSource where T : unmanaged
{
    // Elements encoded at a time: a block that stays in the cache between the swap and the write.
    private const int BlockBytes = 256 * 1024;

    private readonly ReadOnlyMemory<T>[] _planes;
    private readonly long _planeBytes;
    private readonly bool _flipTopBit;
    private readonly int _size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

    /// <param name="planes">Planes of the same length, in file order.</param>
    /// <param name="planeLength">The number of values in each plane.</param>
    /// <param name="flipTopBit">True for the types stored with the <c>BZERO</c> convention (sbyte and the unsigned integers).</param>
    public PlaneDataSource(ReadOnlyMemory<T>[] planes, int planeLength, bool flipTopBit)
    {
        _planes = planes;
        _planeBytes = (long)planeLength * _size;
        _flipTopBit = flipTopBit;
        Length = checked(_planeBytes * planes.Length);
    }

    public override long Length { get; }

    public override byte[] ReadAll()
    {
        if (Length > Array.MaxLength) throw new FitsException("HDU data is larger than 2 GB and cannot be loaded into one array.");
        var bytes = GC.AllocateUninitializedArray<byte>((int)Length);
        ReadRange(0, bytes);
        return bytes;
    }

    /// <summary>The big-endian bytes of <paramref name="values"/> into <paramref name="destination"/>, as <see cref="ImageHdu.Encode{T}"/> writes them.</summary>
    private void Encode(ReadOnlySpan<T> values, Span<byte> destination)
    {
        BigEndian.Write(values, destination);
        if (_flipTopBit) BigEndian.FlipTopBits(destination, _size);
    }

    public override void ReadRange(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > Length || destination.Length > Length - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        Span<byte> one = stackalloc byte[8];
        while (destination.Length > 0)
        {
            int plane = (int)(offset / _planeBytes);
            long within = offset % _planeBytes;
            int element = (int)(within / _size);
            int skip = (int)(within % _size);
            ReadOnlySpan<T> values = _planes[plane].Span;
            int written;
            if (skip == 0 && destination.Length >= _size)
            {
                // whole elements, up to the end of the plane
                int n = (int)Math.Min(values.Length - element, destination.Length / _size);
                written = n * _size;
                Encode(values.Slice(element, n), destination[..written]);
            }
            else
            {
                // a range that starts or ends inside an element
                Encode(values.Slice(element, 1), one[.._size]);
                written = Math.Min(_size - skip, destination.Length);
                one.Slice(skip, written).CopyTo(destination);
            }
            destination = destination[written..];
            offset += written;
        }
    }

    public override void CopyTo(Stream destination, CancellationToken cancellationToken = default)
    {
        if (Length == 0) return;
        int perBlock = Math.Max(1, BlockBytes / _size);
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent((int)Math.Min(perBlock * _size, Length));
        try
        {
            foreach (var plane in _planes)
            {
                for (int start = 0; start < plane.Length; start += perBlock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int n = Math.Min(perBlock, plane.Length - start);
                    Span<byte> block = buffer.AsSpan(0, n * _size);
                    Encode(plane.Span.Slice(start, n), block);
                    destination.Write(block);
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
