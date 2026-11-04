using System.Buffers.Binary;

namespace Eleris.FitsNet.Tests;

/// <summary>The vectorized sum of 32-bit words against the definition: a ones' complement sum, word by word.</summary>
public class ChecksumSumTests
{
    private static uint Reference(ReadOnlySpan<byte> data)
    {
        ulong sum = 0;
        Span<byte> word = stackalloc byte[4];
        for (int i = 0; i < data.Length; i += 4)
        {
            word.Clear();
            data[i..Math.Min(i + 4, data.Length)].CopyTo(word);
            sum += BinaryPrimitives.ReadUInt32BigEndian(word);
            while (sum > uint.MaxValue) sum = (sum & uint.MaxValue) + (sum >> 32);
        }
        return (uint)sum;
    }

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(2047 * 4)]
    [InlineData(2048 * 4)]
    [InlineData(2048 * 4 + 1)]
    [InlineData(2049 * 4 + 3)]
    [InlineData(3 * 2048 * 4 + 28)]
    [InlineData(1_000_003)]
    public void The_sum_matches_the_definition(int length)
    {
        byte[] data = Random(length, length);
        Assert.Equal(Reference(data), Checksum.Sum(data));
        // a slice that does not start on a word boundary of the array
        if (length > 7) Assert.Equal(Reference(data.AsSpan(3)), Checksum.Sum(data.AsSpan(3)));
    }

    [Fact]
    public void Words_that_all_carry_are_folded_correctly()
    {
        byte[] data = new byte[4 * 100_000];
        Array.Fill(data, (byte)0xFF);   // every word carries; the accumulator bound (under 2^61 for a 2 GB span) is an argument, not a test
        Assert.Equal(Reference(data), Checksum.Sum(data));
        Assert.Equal(0xFFFFFFFFu, Checksum.Sum(data));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(100)]
    [InlineData(2048 * 4 + 4)]
    [InlineData(1_000_000)]
    public void The_scalar_sum_matches_the_vectorized_one(int length)
    {
        byte[] data = Random(length, 3);
        Assert.Equal(Checksum.Fold(Checksum.SumWordsScalar(data)), Checksum.Fold(Checksum.SumWords(data)));
        Assert.Equal(Reference(data), Checksum.Fold(Checksum.SumWordsScalar(data)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4097)]
    [InlineData(100_000)]
    public void The_stream_gives_the_sum_whatever_the_size_of_the_writes(int writeSize)
    {
        byte[] data = Random(1_234_567, 7);
        using var stream = new ChecksumStream();
        for (int i = 0; i < data.Length; i += writeSize)
            stream.Write(data.AsSpan(i, Math.Min(writeSize, data.Length - i)));
        Assert.Equal(Reference(data), stream.Result);
    }

    [Fact]
    public void Empty_writes_change_nothing()
    {
        byte[] data = Random(1001, 11);
        using var stream = new ChecksumStream();
        stream.Write(ReadOnlySpan<byte>.Empty);
        stream.Write(data.AsSpan(0, 1000));
        stream.Write(ReadOnlySpan<byte>.Empty);
        stream.Write(data.AsSpan(1000));
        stream.Write(ReadOnlySpan<byte>.Empty);
        Assert.Equal(Reference(data), stream.Result);
    }
}
