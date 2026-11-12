using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>The Rice decoder returns the pixels of the first decoder (<see cref="ReferenceRiceDecoder"/>), and fails on the same data.</summary>
public class RiceDecoderTests
{
    private static int[] Reference(byte[] data, int count, int block, int bytePix)
    {
        var result = new int[count];
        ReferenceRiceDecoder.Decode(data, result, block, bytePix);
        return result;
    }

    /// <summary>The pixels, or null when the data is refused with a <see cref="FitsException"/>.</summary>
    private static int[]? Outcome(Func<int[]> decode)
    {
        try
        {
            return decode();
        }
        catch (FitsException)
        {
            return null;
        }
    }

    private static int[] Noise(Random rng, int length, int bytePix, long amplitude)
    {
        long span = 1L << (8 * bytePix - 1);
        return Enumerable.Range(0, length)
            .Select(i => bytePix == 1 ? (int)(rng.NextInt64(-amplitude, amplitude + 1) & 0xFF)
                : (int)Math.Clamp(i % 41 == 0 && rng.Next(3) == 0 ? rng.NextInt64(-span, span) : rng.NextInt64(-amplitude, amplitude + 1), -span, span - 1))
            .ToArray();
    }

    [Fact]
    public void Valid_data_is_decoded_like_the_first_decoder()
    {
        var rng = new Random(23);
        for (int round = 0; round < 800; round++)
        {
            int bytePix = new[] { 1, 2, 4 }[rng.Next(3)];
            int length = rng.Next(1, 900);
            int block = new[] { 32, 16, 8, 64, 5, 1, 300 }[rng.Next(7)];
            int[] pixels = Noise(rng, length, bytePix, 1L << rng.Next(0, 8 * bytePix - 1));
            byte[] encoded = RiceCodec.Encode(pixels, block, bytePix);
            Assert.Equal(Reference(encoded, length, block, bytePix), RiceCodec.Decode(encoded, length, block, bytePix));
        }
    }

    [Fact]
    public void Zeros_with_a_rare_spike_are_decoded_like_the_first_decoder()
    {
        // a unary code that goes beyond the 64 bits of the window
        foreach (int bytePix in new[] { 1, 2, 4 })
        {
            long span = 1L << (8 * bytePix - 1);
            int[] pixels = Enumerable.Range(0, 6000).Select(i => i % 997 == 500 ? (int)(span - 1) : 0).ToArray();
            foreach (int block in new[] { 32, 1000 })
            {
                byte[] encoded = RiceCodec.Encode(pixels, block, bytePix);
                Assert.Equal(Reference(encoded, pixels.Length, block, bytePix), RiceCodec.Decode(encoded, pixels.Length, block, bytePix));
            }
        }
    }

    [Fact]
    public void Damaged_data_gives_the_same_pixels_or_the_same_refusal_as_the_first_decoder()
    {
        var rng = new Random(77);
        int refused = 0, decoded = 0;
        for (int round = 0; round < 3000; round++)
        {
            int bytePix = new[] { 1, 2, 4 }[rng.Next(3)];
            int length = rng.Next(1, 400);
            int block = new[] { 32, 16, 7 }[rng.Next(3)];
            byte[] data = RiceCodec.Encode(Noise(rng, length, bytePix, 1L << rng.Next(0, 8 * bytePix - 1)), block, bytePix);
            switch (rng.Next(4))
            {
                case 0: data = data[..rng.Next(0, data.Length)]; break;                          // truncated
                case 1: for (int k = rng.Next(1, 4); k > 0; k--) data[rng.Next(data.Length)] ^= (byte)(1 << rng.Next(8)); break;   // bits flipped
                case 2: for (int k = rng.Next(1, 6); k > 0; k--) data[rng.Next(data.Length)] = (byte)rng.Next(256); break;
                default:                                                                          // a run of zero bytes
                    int start = rng.Next(data.Length);
                    Array.Fill(data, (byte)0, start, rng.Next(1, data.Length - start + 1));
                    break;
            }
            // a count that does not match the data, too
            int count = rng.Next(4) == 0 ? length + rng.Next(1, 200) : length;
            int[]? expected = Outcome(() => Reference(data, count, block, bytePix));
            int[]? actual = Outcome(() => RiceCodec.Decode(data, count, block, bytePix));
            Assert.Equal(expected, actual);
            if (expected is null) refused++; else decoded++;
        }
        // both kinds of outcome happened
        Assert.True(refused > 200 && decoded > 200, $"refused {refused}, decoded {decoded}");
    }

    [Fact]
    public void A_run_of_zeros_through_the_whole_data_is_refused_not_looped_on()
    {
        // the four bytes of the first pixel, then an FS code of 4 (3 + 1, in 5 bits) and nothing but zero bits: the unary code never ends
        byte[] data = new byte[2_000_000];
        data[4] = 0b0010_0000;
        Assert.Throws<FitsException>(() => RiceCodec.Decode(data, 100, 32, 4));
        Assert.Equal(Outcome(() => Reference(data, 100, 32, 4)), Outcome(() => RiceCodec.Decode(data, 100, 32, 4)));
    }
}
