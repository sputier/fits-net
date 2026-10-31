using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace FitsNet;

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
        var result = new T[data.Length / size];
        data.CopyTo(MemoryMarshal.AsBytes(result.AsSpan()));
        if (BitConverter.IsLittleEndian) Swap(MemoryMarshal.AsBytes(result.AsSpan()), size);
        return result;
    }

    public static byte[] FromArray<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        var bytes = new byte[checked(values.Length * size)];
        MemoryMarshal.AsBytes(values).CopyTo(bytes);
        if (BitConverter.IsLittleEndian) Swap(bytes, size);
        return bytes;
    }

    private static void Swap(Span<byte> bytes, int size)
    {
        switch (size)
        {
            case 1: break;
            case 2: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, ushort>(bytes), MemoryMarshal.Cast<byte, ushort>(bytes)); break;
            case 4: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, uint>(bytes), MemoryMarshal.Cast<byte, uint>(bytes)); break;
            case 8: BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, ulong>(bytes), MemoryMarshal.Cast<byte, ulong>(bytes)); break;
            default: throw new NotSupportedException($"Unsupported element size {size}.");
        }
    }
}
