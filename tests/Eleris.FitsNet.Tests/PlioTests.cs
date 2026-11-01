using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

public class PlioTests
{
    // Words produced by astropy (cfitsio) for these inputs; the first 7 words are the header.
    public static IEnumerable<object[]> AstropyCases()
    {
        yield return new object[] { new[] { 0, 0, 0, 0, 0, 0, 0, 0 }, new short[] { 0, 7, -100, 8, 0, 0, 0, 8 } };
        yield return new object[] { new[] { 1, 1, 1, 1, 1, 1, 1, 1 }, new short[] { 0, 7, -100, 8, 0, 0, 0, 16392 } };
        yield return new object[] { new[] { 7, 7, 7, 7, 7 }, new short[] { 0, 7, -100, 9, 0, 0, 0, 8198, 16389 } };
        yield return new object[] { new[] { 0, 0, 0, 1, 1, 0 }, new short[] { 0, 7, -100, 10, 0, 0, 0, 3, 16386, 1 } };
        yield return new object[]
        {
            new[] { 0, 0, 0, 5, 5, 5, 5, 0, 0, 1, 1, 1, 0, 0, 0, 0 },
            new short[] { 0, 7, -100, 14, 0, 0, 0, 8196, 3, 16388, 12292, 2, 16387, 4 },
        };
        yield return new object[]
        {
            new[] { 0, 1, 2, 3, 4, 5, 4, 3, 2, 1, 0 },
            new short[] { 0, 7, -100, 17, 0, 0, 0, 20482, 24577, 24577, 24577, 24577, 28673, 28673, 28673, 28673, 1 },
        };
        yield return new object[]
        {
            new[] { 0, 0, 5000, 5000, 5000, 0, 70000, 70000, 0 },
            new short[] { 0, 7, -100, 16, 0, 0, 0, 5000, 1, 2, 16387, 4464, 17, 1, 16386, 1 },
        };
        yield return new object[] { Enumerable.Repeat(3, 5000).ToArray(), new short[] { 0, 7, -100, 10, 0, 0, 0, 8194, 20479, 17289 } };
        yield return new object[]
        {
            Enumerable.Repeat(0, 5000).Append(2).ToArray(), new short[] { 0, 7, -100, 10, 0, 0, 0, 8193, 4095, 21386 },
        };
        yield return new object[] { new[] { 0, 1, 0, 1, 0, 1, 0, 1 }, new short[] { 0, 7, -100, 11, 0, 0, 0, 20482, 20482, 20482, 20482 } };
        yield return new object[] { new[] { 9 }, new short[] { 0, 7, -100, 8, 0, 0, 0, 24584 } };
        yield return new object[]
        {
            new[] { 10, 9, 8, 7, 6, 5 }, new short[] { 0, 7, -100, 13, 0, 0, 0, 24585, 28673, 28673, 28673, 28673, 28673 },
        };
        yield return new object[] { new[] { 1, 100, 2, 200 }, new short[] { 0, 7, -100, 11, 0, 0, 0, 16385, 24675, 28770, 24774 } };
    }

    [Theory]
    [MemberData(nameof(AstropyCases))]
    public void Decodes_what_cfitsio_produced(int[] expected, short[] words) =>
        Assert.Equal(expected, PlioCodec.Decode(words, expected.Length));

    [Theory]
    [MemberData(nameof(AstropyCases))]
    public void Our_encoding_decodes_back_and_has_a_valid_header(int[] pixels, short[] _)
    {
        short[] words = PlioCodec.Encode(pixels);
        Assert.Equal(7, words[1]);
        Assert.Equal(-100, words[2]);
        Assert.Equal(words.Length, (ushort)words[3] | ((ushort)words[4] << 15));
        Assert.Equal(pixels, PlioCodec.Decode(words, pixels.Length));
    }

    [Fact]
    public void Random_masks_roundtrip()
    {
        var rng = new Random(3);
        for (int round = 0; round < 300; round++)
        {
            int n = rng.Next(1, 3000);
            var data = new int[n];
            int i = 0;
            while (i < n)
            {
                int run = rng.Next(1, 200);
                int v = rng.Next(4) switch { 0 => 0, 1 => rng.Next(1, 4), 2 => rng.Next(0, 6000), _ => rng.Next(0, PlioCodec.MaxValue) };
                for (int k = 0; k < run && i < n; k++, i++) data[i] = v;
            }
            Assert.Equal(data, PlioCodec.Decode(PlioCodec.Encode(data), n));
        }
    }

    [Fact]
    public void Extreme_values_and_negative_pixels()
    {
        int[] data = [0, PlioCodec.MaxValue, 0, PlioCodec.MaxValue, 1, 4096, 4097, 1, 8192, 8191];
        Assert.Equal(data, PlioCodec.Decode(PlioCodec.Encode(data), data.Length));
        // negative pixels are not representable and are stored as 0
        Assert.Equal([0, 0, 3], PlioCodec.Decode(PlioCodec.Encode([-5, 0, 3]), 3));
        Assert.Throws<FitsException>(() => PlioCodec.Encode([PlioCodec.MaxValue + 1]));
    }

    [Fact]
    public void Malformed_line_lists_throw_FitsException()
    {
        Assert.Throws<FitsException>(() => PlioCodec.Decode([0, 7, -100], 5));
        Assert.Throws<FitsException>(() => PlioCodec.Decode([0, 7, -100, 20, 0, 0, 0, 8], 8)); // announces more words than present
        Assert.Throws<FitsException>(() => PlioCodec.Decode([0, 7, -100, 8, 0, 0, 0, unchecked((short)0x9001)], 8)); // opcode 9
        Assert.Throws<FitsException>(() => PlioCodec.Decode([0, 7, -100, 8, 0, 0, 0, 0x1005], 8)); // SH without its second word
    }

    [Fact]
    public void Missing_pixels_are_zero_and_surplus_instructions_are_ignored()
    {
        Assert.Equal([0, 0, 0, 0], PlioCodec.Decode([0, 7, -100, 8, 0, 0, 0, 2], 4));
        Assert.Equal([1, 1], PlioCodec.Decode([0, 7, -100, 8, 0, 0, 0, 16392], 2));
    }
}
