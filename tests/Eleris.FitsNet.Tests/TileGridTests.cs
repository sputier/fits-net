using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>The tile copies work on the big-endian bytes of the whole image: checked against a naive element by element copy.</summary>
public class TileGridTests
{
    public static TheoryData<long[], long[]> Shapes() => new()
    {
        { new long[] { 7, 5, 4 }, new long[] { 3, 2, 3 } },   // tiles that do not divide the axes
        { new long[] { 7, 5, 4 }, new long[] { 7, 1, 1 } },   // one row per tile
        { new long[] { 7, 5, 4 }, new long[] { 7, 5, 4 } },   // a single tile
        { new long[] { 12 }, new long[] { 5 } },              // one axis
        { new long[] { 6, 9 }, new long[] { 6, 4 } },         // whole rows
    };

    private static int Index(long[] axes, long[] position)
    {
        long index = 0, stride = 1;
        for (int d = 0; d < axes.Length; d++)
        {
            index += position[d] * stride;
            stride *= axes[d];
        }
        return (int)index;
    }

    /// <summary>The positions of a tile, the first axis varying fastest.</summary>
    private static IEnumerable<long[]> Positions(long[] origin, long[] extent)
    {
        var position = new long[extent.Length];
        long count = extent.Aggregate(1L, (a, b) => a * b);
        for (long i = 0; i < count; i++)
        {
            long rest = i;
            var absolute = new long[extent.Length];
            for (int d = 0; d < extent.Length; d++)
            {
                absolute[d] = origin[d] + rest % extent[d];
                rest /= extent[d];
            }
            yield return absolute;
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Tiles_read_and_write_the_big_endian_image(long[] axes, long[] tileShape)
    {
        var grid = new TileGrid(axes, tileShape);
        int count = (int)axes.Aggregate(1L, (a, b) => a * b);
        short[] image = Enumerable.Range(0, count).Select(i => unchecked((short)(i * 977 - 3000))).ToArray();
        byte[] bytes = BigEndian.FromArray<short>(image);

        var rebuilt = new byte[bytes.Length];
        var rebuiltRaw = new byte[bytes.Length];
        for (long t = 0; t < grid.TileCount; t++)
        {
            grid.GetTile(t, out var origin, out var extent);
            int n = TileGrid.CountInt(extent);
            short[] expected = Positions(origin, extent).Select(p => image[Index(axes, p)]).ToArray();

            var tile = new short[n];
            grid.ReadTile<short>(bytes, tile, origin, extent);
            Assert.Equal(expected, tile);

            grid.WriteTile<short>(rebuilt, tile, origin, extent);

            var raw = new byte[n * 2];
            grid.ReadTileBytes(bytes, raw, 2, origin, extent);
            Assert.Equal(BigEndian.FromArray<short>(expected), raw);
            grid.WriteTileBytes(rebuiltRaw, raw, 2, origin, extent);
        }
        Assert.Equal(bytes, rebuilt);
        Assert.Equal(bytes, rebuiltRaw);
    }

    [Fact]
    public void Every_element_size_is_swapped()
    {
        var grid = new TileGrid([5, 3], [2, 2]);
        double[] doubles = Enumerable.Range(0, 15).Select(i => i * 1.25 - 7).ToArray();
        float[] floats = doubles.Select(v => (float)v).ToArray();
        long[] longs = Enumerable.Range(0, 15).Select(i => i * 4_000_000_007L - 9).ToArray();
        var fromDoubles = new byte[15 * 8];
        var fromFloats = new byte[15 * 4];
        var fromLongs = new byte[15 * 8];
        for (long t = 0; t < grid.TileCount; t++)
        {
            grid.GetTile(t, out var origin, out var extent);
            int n = TileGrid.CountInt(extent);
            var d = new double[n];
            var f = new float[n];
            var l = new long[n];
            grid.ReadTile<double>(BigEndian.FromArray<double>(doubles), d, origin, extent);
            grid.ReadTile<float>(BigEndian.FromArray<float>(floats), f, origin, extent);
            grid.ReadTile<long>(BigEndian.FromArray<long>(longs), l, origin, extent);
            grid.WriteTile<double>(fromDoubles, d, origin, extent);
            grid.WriteTile<float>(fromFloats, f, origin, extent);
            grid.WriteTile<long>(fromLongs, l, origin, extent);
        }
        Assert.Equal(BigEndian.FromArray<double>(doubles), fromDoubles);
        Assert.Equal(BigEndian.FromArray<float>(floats), fromFloats);
        Assert.Equal(BigEndian.FromArray<long>(longs), fromLongs);
    }

    [Fact]
    public void Decoding_into_a_buffer_gives_what_decoding_into_an_array_gives()
    {
        int[] pixels = Enumerable.Range(0, 1000).Select(i => (i * 37) % 301 - 150).ToArray();
        foreach (int bytePix in new[] { 2, 4 })
        {
            byte[] encoded = RiceCodec.Encode(pixels, 32, bytePix);
            var buffer = new int[pixels.Length];
            RiceCodec.Decode(encoded, buffer, 32, bytePix);
            Assert.Equal(RiceCodec.Decode(encoded, pixels.Length, 32, bytePix), buffer);
            Assert.Equal(pixels, buffer);
        }

        int[] ints = Enumerable.Range(0, 64).Select(i => i * 13 - 200).ToArray();
        foreach (var method in new[] { QuantizationMethod.NoDither, QuantizationMethod.SubtractiveDither1, QuantizationMethod.SubtractiveDither2 })
        {
            var doubles = new double[ints.Length];
            Quantizer.Dequantize(ints, doubles, 0.37, 11.5, ints[3], method, 5, 7);
            Assert.Equal(Quantizer.Dequantize(ints, 0.37, 11.5, ints[3], method, 5, 7), doubles);
            var singles = new float[ints.Length];
            Quantizer.DequantizeToSingle(ints, singles, 0.37, 11.5, ints[3], method, 5, 7);
            Assert.Equal(Quantizer.DequantizeToSingle(ints, 0.37, 11.5, ints[3], method, 5, 7), singles);
        }
    }

    [Fact]
    public void Quantizing_checks_the_size_of_its_output()
    {
        Assert.Throws<ArgumentException>(() =>
            Quantizer.TryQuantize(new double[10], new int[9], 10, 4, QuantizationMethod.NoDither, 0, 1, out _, out _, out _));
    }
}
