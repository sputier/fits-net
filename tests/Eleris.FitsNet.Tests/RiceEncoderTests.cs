using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>The Rice encoder writes the same bytes as the first one (<see cref="ReferenceRiceEncoder"/>), whatever the data and the block.</summary>
public class RiceEncoderTests
{
    public static TheoryData<int, int> Widths() =>
        new() { { 1, 32 }, { 1, 7 }, { 2, 32 }, { 2, 16 }, { 2, 1 }, { 2, 300 }, { 4, 32 }, { 4, 64 }, { 4, 2 }, { 4, 1 }, { 4, 1000 } };

    private static int Clamp(long value, int bytePix) =>
        bytePix switch { 1 => (int)(value & 0xFF), 2 => (int)(short)value, _ => (int)value };

    private static IEnumerable<int[]> Cases(int bytePix, int seed)
    {
        var rng = new Random(seed);
        long span = 1L << (8 * bytePix - 1);
        yield return new int[100];                                                                  // low entropy: every difference is zero
        yield return [5];                                                                           // one pixel
        yield return Enumerable.Range(0, 1000).Select(i => Clamp(i % 17, bytePix)).ToArray();
        yield return Enumerable.Range(0, 5000).Select(_ => Clamp(rng.NextInt64(-span, span), bytePix)).ToArray();                  // noise: high entropy
        yield return Enumerable.Range(0, 5000).Select(i => Clamp((long)(100 * Math.Sin(i / 9.0)) + rng.Next(-3, 4), bytePix)).ToArray();   // a smooth sky
        yield return Enumerable.Range(0, 5000).Select(i => Clamp((long)(span / 3 * Math.Sin(i / 40.0)) + rng.Next(-60, 61), bytePix)).ToArray();
        // zeros with a rare spike: a unary code too long for 32 bits
        yield return Enumerable.Range(0, 4000).Select(i => i % 997 == 500 ? Clamp(span - 1, bytePix) : 0).ToArray();
        yield return Enumerable.Range(0, 4000).Select(i => i % 61 == 7 ? Clamp(-span, bytePix) : Clamp(i / 700, bytePix)).ToArray();
        yield return [Clamp(-span, bytePix), Clamp(span - 1, bytePix), Clamp(-span, bytePix), Clamp(span - 1, bytePix), 0, Clamp(span - 1, bytePix)];
        // sizes around the blocks
        foreach (int length in new[] { 2, 31, 32, 33, 63, 64, 65, 255, 256, 257, 301 })
            yield return Enumerable.Range(0, length).Select(_ => Clamp(rng.NextInt64(-300, 301), bytePix)).ToArray();
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void The_bytes_are_those_of_the_first_encoder(int bytePix, int block)
    {
        foreach (int[] data in Cases(bytePix, 17 + bytePix * 100 + block))
            Assert.Equal(ReferenceRiceEncoder.Encode(data, block, bytePix), RiceCodec.Encode(data, block, bytePix));
    }

    [Fact]
    public void Random_blocks_of_every_kind_are_encoded_like_the_first_encoder_and_decoded_back()
    {
        var rng = new Random(99);
        for (int round = 0; round < 600; round++)
        {
            int bytePix = new[] { 1, 2, 4 }[rng.Next(3)];
            int length = rng.Next(1, 700);
            int block = new[] { 32, 16, 8, 64, 5, 300 }[rng.Next(6)];
            long span = 1L << (8 * bytePix - 1);
            // the amplitude of the noise changes from one round to the next, and sometimes inside one
            long amplitude = 1L << rng.Next(0, 8 * bytePix);
            int[] data = Enumerable.Range(0, length)
                .Select(i => Clamp(i % 53 == 0 && rng.Next(4) == 0 ? rng.NextInt64(-span, span) : rng.NextInt64(-amplitude, amplitude + 1), bytePix))
                .ToArray();
            byte[] encoded = RiceCodec.Encode(data, block, bytePix);
            Assert.Equal(ReferenceRiceEncoder.Encode(data, block, bytePix), encoded);
            Assert.Equal(data.Select(v => bytePix switch { 4 => v, 2 => (int)(short)v, _ => v & 0xFF }), RiceCodec.Decode(encoded, length, block, bytePix));
        }
    }

    [Fact]
    public void A_block_larger_than_the_data_or_than_a_million_values_is_encoded_like_the_first_encoder()
    {
        var rng = new Random(4);
        int[] data = Enumerable.Range(0, 1_200_000).Select(i => (int)(short)(500 * Math.Sin(i / 90.0) + rng.Next(-40, 41))).ToArray();
        Assert.Equal(ReferenceRiceEncoder.Encode(data, 1_150_000, 2), RiceCodec.Encode(data, 1_150_000, 2));
        Assert.Equal(ReferenceRiceEncoder.Encode(data[..100], 5000, 2), RiceCodec.Encode(data[..100], 5000, 2));
    }
}
