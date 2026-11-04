using System.Buffers.Binary;

namespace Eleris.FitsNet.Tests;

/// <summary>The bytes written for every pixel type, against a value by value encoding, with sizes around the blocks of the conversion.</summary>
public class PixelWritingTests
{
    // 256 KB are converted at a time: 131072 shorts, 65536 ints, 32768 longs. Sizes below, on and above, and a few odd ones.
    public static TheoryData<int> Lengths() => [0, 1, 3, 7, 31, 32, 33, 4097, 32767, 32768, 32769, 65535, 65536, 65537, 131071, 131072, 131073, 262144 + 5];

    private static T[] Values<T>(int length, Func<int, T> make) => Enumerable.Range(0, length).Select(make).ToArray();

    private static byte[] Written<T>(T[] pixels) where T : unmanaged
    {
        var image = ImageHdu.Create<T>(pixels, pixels.Length);
        return image.Source.ReadAll();
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Signed_and_floating_pixels_are_written_big_endian(int length)
    {
        short[] shorts = Values(length, i => unchecked((short)(i * 7919 - 12345)));
        byte[] expected = new byte[length * 2];
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteInt16BigEndian(expected.AsSpan(i * 2), shorts[i]);
        Assert.Equal(expected, Written(shorts));

        int[] ints = Values(length, i => unchecked(i * 104729 - 5_000_000));
        expected = new byte[length * 4];
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteInt32BigEndian(expected.AsSpan(i * 4), ints[i]);
        Assert.Equal(expected, Written(ints));

        long[] longs = Values(length, i => unchecked(i * 6_700_417L - 40_000_000_000L));
        expected = new byte[length * 8];
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteInt64BigEndian(expected.AsSpan(i * 8), longs[i]);
        Assert.Equal(expected, Written(longs));

        float[] floats = Values(length, i => i * 0.37f - 1000f);
        expected = new byte[length * 4];
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteSingleBigEndian(expected.AsSpan(i * 4), floats[i]);
        Assert.Equal(expected, Written(floats));

        double[] doubles = Values(length, i => i * 1.0000001 - 12345.678);
        expected = new byte[length * 8];
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteDoubleBigEndian(expected.AsSpan(i * 8), doubles[i]);
        Assert.Equal(expected, Written(doubles));

        byte[] bytes = Values(length, i => unchecked((byte)(i * 31)));
        Assert.Equal(bytes, Written(bytes));
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Unsigned_pixels_are_written_with_the_BZERO_convention(int length)
    {
        // the stored value is the pixel minus 2^(n-1), which is the pixel with its top bit flipped
        sbyte[] sbytes = Values(length, i => unchecked((sbyte)(i * 37)));
        Assert.Equal(sbytes.Select(v => unchecked((byte)(v ^ -128))).ToArray(), Written(sbytes));

        ushort[] ushorts = Values(length, i => unchecked((ushort)(i * 7919)));
        byte[] expected = new byte[length * 2];
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteInt16BigEndian(expected.AsSpan(i * 2), unchecked((short)(ushorts[i] ^ 0x8000)));
        Assert.Equal(expected, Written(ushorts));

        uint[] uints = Values(length, i => unchecked((uint)i * 2_654_435_761u));
        expected = new byte[length * 4];
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteInt32BigEndian(expected.AsSpan(i * 4), unchecked((int)(uints[i] ^ 0x80000000u)));
        Assert.Equal(expected, Written(uints));

        ulong[] ulongs = Values(length, i => unchecked((ulong)i * 11_400_714_819_323_198_485ul));
        expected = new byte[length * 8];
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteInt64BigEndian(expected.AsSpan(i * 8), unchecked((long)(ulongs[i] ^ 0x8000000000000000ul)));
        Assert.Equal(expected, Written(ulongs));
    }

    [Fact]
    public void The_pixels_written_are_the_pixels_read_back()
    {
        ushort[] pixels = Values(300_001, i => unchecked((ushort)(i * 7919)));
        var image = ImageHdu.Create<ushort>(pixels, 300_001);
        Assert.Equal(pixels, image.ReadPixels<ushort>());
        Assert.Equal(32768L, image.Header.Get<long>("BZERO"));
    }
}
