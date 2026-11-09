using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Eleris.FitsNet;

/// <summary>The BITPIX data representations defined by the standard.</summary>
public enum BitPix
{
    /// <summary>8-bit unsigned integers.</summary>
    Byte = 8,
    /// <summary>16-bit signed integers.</summary>
    Int16 = 16,
    /// <summary>32-bit signed integers.</summary>
    Int32 = 32,
    /// <summary>64-bit signed integers.</summary>
    Int64 = 64,
    /// <summary>IEEE 754 single precision floating point numbers.</summary>
    Single = -32,
    /// <summary>IEEE 754 double precision floating point numbers.</summary>
    Double = -64,
}

internal static class BitPixExtensions
{
    public static bool IsValid(long value) => value is 8 or 16 or 32 or 64 or -32 or -64;

    public static int BytesPerElement(this BitPix bitPix) => Math.Abs((int)bitPix) / 8;

    public static bool IsFloatingPoint(this BitPix bitPix) => bitPix is BitPix.Single or BitPix.Double;

    /// <summary>The CLR type that stores raw values of this BITPIX.</summary>
    public static Type NativeType(this BitPix bitPix) => bitPix switch
    {
        BitPix.Byte => typeof(byte),
        BitPix.Int16 => typeof(short),
        BitPix.Int32 => typeof(int),
        BitPix.Int64 => typeof(long),
        BitPix.Single => typeof(float),
        BitPix.Double => typeof(double),
        _ => throw new ArgumentOutOfRangeException(nameof(bitPix)),
    };
}

/// <summary>Conversion between big-endian FITS data and CLR arrays.</summary>
internal static class BigEndian
{
    public static T[] ToArray<T>(ReadOnlySpan<byte> data) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        if (data.Length % size != 0) throw new FitsException("Data length is not a multiple of the element size.");
        var result = GC.AllocateUninitializedArray<T>(data.Length / size);
        Read(data, result);
        return result;
    }

    /// <summary>Reads all the values of a data source, a chunk at a time so that nothing but the result is allocated.</summary>
    public static T[] ReadArray<T>(DataSource source, CancellationToken cancellationToken = default) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        var result = GC.AllocateUninitializedArray<T>(source.ElementCount(size));
        source.ReadInChunks(size, (bytes, first) => Read(bytes, result.AsSpan(first, bytes.Length / size)), cancellationToken);
        return result;
    }

    /// <summary>Like <see cref="ReadArray{T}"/>, then flips the bits of <paramref name="mask"/> (the unsigned integer conventions: BZERO = 2^(n-1)).</summary>
    public static T[] ReadArrayXor<T>(DataSource source, T mask, CancellationToken cancellationToken = default) where T : unmanaged, IBitwiseOperators<T, T, T>
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        var result = GC.AllocateUninitializedArray<T>(source.ElementCount(size));
        source.ReadInChunks(size, (bytes, first) =>
        {
            Span<T> part = result.AsSpan(first, bytes.Length / size);
            Read(bytes, part);
            Xor(part, mask);
        }, cancellationToken);
        return result;
    }

    /// <summary>Fills <paramref name="destination"/> with the big-endian values in <paramref name="data"/>, in one pass.</summary>
    public static void Read<T>(ReadOnlySpan<byte> data, Span<T> destination) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        if (data.Length != destination.Length * size) throw new ArgumentException("The destination does not match the data.", nameof(destination));
        if (size == 1 || !BitConverter.IsLittleEndian)
        {
            data.CopyTo(MemoryMarshal.AsBytes(destination));
            return;
        }
        switch (size)
        {
            case 2: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, ushort>(data), MemoryMarshal.Cast<T, ushort>(destination)); break;
            case 4: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, uint>(data), MemoryMarshal.Cast<T, uint>(destination)); break;
            case 8: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, ulong>(data), MemoryMarshal.Cast<T, ulong>(destination)); break;
            default: throw new NotSupportedException($"Unsupported element size {size}.");
        }
    }

    /// <summary>Writes native values as big-endian bytes, in one pass.</summary>
    public static void Write<T>(ReadOnlySpan<T> values, Span<byte> destination) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        if (destination.Length != values.Length * size) throw new ArgumentException("The destination does not match the values.", nameof(destination));
        if (size == 1 || !BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(values).CopyTo(destination);
            return;
        }
        switch (size)
        {
            case 2: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<T, ushort>(values), MemoryMarshal.Cast<byte, ushort>(destination)); break;
            case 4: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<T, uint>(values), MemoryMarshal.Cast<byte, uint>(destination)); break;
            case 8: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<T, ulong>(values), MemoryMarshal.Cast<byte, ulong>(destination)); break;
            default: throw new NotSupportedException($"Unsupported element size {size}.");
        }
    }

    public static void Xor<T>(Span<T> values, T mask) where T : unmanaged, IBitwiseOperators<T, T, T>
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && values.Length >= Vector<T>.Count)
        {
            var vectorMask = new Vector<T>(mask);
            for (; i <= values.Length - Vector<T>.Count; i += Vector<T>.Count)
                (new Vector<T>(values.Slice(i)) ^ vectorMask).CopyTo(values.Slice(i));
        }
        for (; i < values.Length; i++) values[i] ^= mask;
    }

    public static byte[] FromArray<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        var bytes = GC.AllocateUninitializedArray<byte>(checked(values.Length * size));
        Write(values, bytes);   // one pass, and the array is not cleared first
        return bytes;
    }

    // Elements converted at a time: a block that stays in the cache between the swap and the flip.
    private const int WriteBlockBytes = 256 * 1024;

    /// <summary>
    /// The big-endian bytes of unsigned integers (or of <c>sbyte</c>) stored with the <c>BZERO</c> convention, that is with the top bit
    /// of each value flipped (the same as subtracting 2^(n-1)): the swap and the flip are done together, a block at a time.
    /// </summary>
    public static byte[] FromArrayFlippingTopBit<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        var bytes = GC.AllocateUninitializedArray<byte>(checked(values.Length * size));
        int perBlock = WriteBlockBytes / size;
        for (int start = 0, n; start < values.Length; start += n)
        {
            n = Math.Min(perBlock, values.Length - start);   // the step is the size of the block just done: no overflow near int.MaxValue
            Span<byte> block = bytes.AsSpan(start * size, n * size);
            Write(values.Slice(start, n), block);
            FlipTopBits(block, size);
        }
        return bytes;
    }

    /// <summary>Flips the first bit of every big-endian element of <paramref name="size"/> bytes.</summary>
    internal static void FlipTopBits(Span<byte> bytes, int size)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && bytes.Length >= Vector<byte>.Count)
        {
            // the vector holds a whole number of elements (2, 4 or 8 bytes divide 16, 32 and 64): the same pattern at every step
            Span<byte> pattern = stackalloc byte[Vector<byte>.Count];
            pattern.Clear();
            for (int k = 0; k < pattern.Length; k += size) pattern[k] = 0x80;
            var mask = new Vector<byte>(pattern);
            for (; i <= bytes.Length - Vector<byte>.Count; i += Vector<byte>.Count)
                (new Vector<byte>(bytes[i..]) ^ mask).CopyTo(bytes[i..]);
        }
        for (; i < bytes.Length; i += size) bytes[i] ^= 0x80;
    }
}
