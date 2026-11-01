using System.Buffers.Binary;
using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

public class CompressionTests
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "compressed");

    private static string Path_(string name) => Path.Combine(Dir, name);

    public static IEnumerable<object[]> Manifest(params string[] algorithms)
    {
        foreach (string line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "compressed", "compressed_manifest.txt")))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && algorithms.Any(a => parts[0].Contains("_" + a)))
                yield return [parts[0], parts[1], int.Parse(parts[2]), int.Parse(parts[3])];
        }
    }

    public static IEnumerable<object[]> Supported() => Manifest("rice", "gzip", "none");

    public static IEnumerable<object[]> AllCodecs() => Manifest("rice", "gzip", "none", "plio", "hcomp");

    /// <summary>Reads astropy's decoded pixels (big-endian dump) as doubles.</summary>
    private static double[] Expected(string file, string dtype)
    {
        byte[] b = File.ReadAllBytes(Path_(file + ".expected"));
        string kind = dtype.TrimStart('<', '>', '|', '=');
        switch (kind)
        {
            case "u1": return b.Select(x => (double)x).ToArray();
            case "i1": return b.Select(x => (double)(sbyte)x).ToArray();
            case "i2": return Enumerable.Range(0, b.Length / 2).Select(i => (double)BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(i * 2))).ToArray();
            case "u2": return Enumerable.Range(0, b.Length / 2).Select(i => (double)BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i * 2))).ToArray();
            case "i4": return Enumerable.Range(0, b.Length / 4).Select(i => (double)BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(i * 4))).ToArray();
            case "u4": return Enumerable.Range(0, b.Length / 4).Select(i => (double)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i * 4))).ToArray();
            case "f4": return Enumerable.Range(0, b.Length / 4).Select(i => (double)BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(i * 4))).ToArray();
            case "f8": return Enumerable.Range(0, b.Length / 8).Select(i => BinaryPrimitives.ReadDoubleBigEndian(b.AsSpan(i * 8))).ToArray();
            default: throw new NotSupportedException(dtype);
        }
    }

    [Theory]
    [MemberData(nameof(Supported))]
    public void Decompresses_files_written_by_astropy_to_the_same_pixels(string file, string dtype, int ny, int nx)
    {
        using var f = FitsFile.Open(Path_(file), new FitsReadOptions(true));
        var comp = Assert.IsType<CompressedImageHdu>(f[1]);
        Assert.Equal([(long)nx, ny], comp.Axes);
        double[] expected = Expected(file, dtype);
        double[] actual = comp.ReadPixels<double>();
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
    }

    public static IEnumerable<object[]> HcompFiles() => Manifest("hcomp");

    [Theory]
    [MemberData(nameof(HcompFiles))]
    public void Decompresses_HCOMPRESS_files_written_by_astropy(string file, string dtype, int ny, int nx)
    {
        using var f = FitsFile.Open(Path_(file), new FitsReadOptions(true));
        var comp = Assert.IsType<CompressedImageHdu>(f[1]);
        Assert.Equal("HCOMPRESS_1", comp.Algorithm);
        Assert.Equal(nx * ny, comp.ReadPixels<double>().Length);
        Assert.Equal(Expected(file, dtype), comp.ReadPixels<double>());
    }

    public static IEnumerable<object[]> PlioFiles() => Manifest("plio");

    [Theory]
    [MemberData(nameof(PlioFiles))]
    public void Decompresses_PLIO_masks_written_by_astropy(string file, string dtype, int ny, int nx)
    {
        Assert.Contains("plio", file);
        using var f = FitsFile.Open(Path_(file), new FitsReadOptions(true));
        var comp = Assert.IsType<CompressedImageHdu>(f[1]);
        Assert.Equal("PLIO_1", comp.Algorithm);
        Assert.Equal(Expected(file, dtype), comp.ReadPixels<double>());
        Assert.Equal(nx * ny, comp.ReadPixels<int>().Length);
    }

    [Fact]
    public void PLIO_roundtrips_masks_with_our_own_encoder_in_a_file()
    {
        int[] mask = new int[37 * 23];
        for (int i = 0; i < mask.Length; i++) mask[i] = (i % 37 < 10 ? 0 : (i / 37) % 3 == 0 ? 7 : 300);
        var comp = CompressedImageHdu.Create<int>(mask, [37, 23], new CompressionOptions { Algorithm = CompressionAlgorithm.Plio, TileShape = [16, 8] });
        Assert.Equal("1PI", comp.Columns[0].Format[..3]);
        var ms = new MemoryStream();
        new FitsFile().Add(comp).Save(ms);
        ms.Position = 0;
        using var back = FitsFile.Read(ms, new FitsReadOptions(true));
        Assert.Equal(mask, ((CompressedImageHdu)back[1]).ReadPixels<int>());
        Assert.Throws<NotSupportedException>(() =>
            CompressedImageHdu.Create<float>([1f, 2f], [2], new CompressionOptions { Algorithm = CompressionAlgorithm.Plio }));
    }

    [Theory]
    [MemberData(nameof(Supported))]
    public void Compressed_files_survive_a_save_and_read_cycle_unchanged(string file, string dtype, int ny, int nx)
    {
        _ = (dtype, ny, nx);
        using var f = FitsFile.Open(Path_(file));
        var ms = new MemoryStream();
        f.Save(ms);
        ms.Position = 0;
        using var back = FitsFile.Read(ms, new FitsReadOptions(true));
        var a = (CompressedImageHdu)f[1];
        var b = (CompressedImageHdu)back[1];
        Assert.Equal(a.Algorithm, b.Algorithm);
        Assert.Equal(a.ReadPixels<double>(), b.ReadPixels<double>());
    }

    [Fact]
    public void Decompress_gives_an_ordinary_image_with_the_original_header_cards()
    {
        using var f = FitsFile.Open(Path_("comp_i16_rice.fits"));
        var comp = (CompressedImageHdu)f[1];
        var image = comp.Decompress();
        Assert.Equal(BitPix.Int16, image.BitPix);
        Assert.Equal(new long[] { 37, 23 }, image.Axes.ToArray());
        Assert.Equal("IMG", image.Header.Get<string>("EXTNAME"));
        Assert.False(image.Header.Contains("ZIMAGE"));
        Assert.False(image.Header.Contains("TFIELDS"));
        Assert.False(image.Header.Contains("ZTILE1"));
        Assert.Equal(comp.ReadRawPixels<short>(), image.ReadRawPixels<short>());
    }

    // ---- Rice codec -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(4)]
    [InlineData(2)]
    [InlineData(1)]
    public void Rice_roundtrips_random_smooth_and_extreme_data(int bytePix)
    {
        var rng = new Random(5);
        int bits = 8 * bytePix;
        long lo = bytePix == 1 ? 0 : -(1L << (bits - 1)), hi = bytePix == 1 ? 255 : (1L << (bits - 1)) - 1;
        var cases = new List<int[]>
        {
            new int[100],                                                                // all zero: low entropy
            Enumerable.Range(0, 1000).Select(i => (int)Math.Clamp(i % 17, lo, hi)).ToArray(),
            Enumerable.Range(0, 1000).Select(_ => (int)rng.NextInt64(lo, hi + 1)).ToArray(), // pure noise: high entropy
            Enumerable.Range(0, 1000).Select(i => (int)Math.Clamp((long)(100 * Math.Sin(i / 9.0)) + rng.Next(-3, 4), lo, hi)).ToArray(),
            new[] { (int)lo, (int)hi, (int)lo, (int)hi, 0, (int)hi },                    // extreme jumps
            new[] { 5 },                                                                 // a single pixel
            Enumerable.Range(0, 33).Select(i => i == 32 ? 7 : 0).ToArray(),             // block boundary
        };
        foreach (int[] data in cases)
        {
            foreach (int block in new[] { 32, 16, 7, 1 })
            {
                byte[] encoded = RiceCodec.Encode(data, block, bytePix);
                int[] decoded = RiceCodec.Decode(encoded, data.Length, block, bytePix);
                // two-byte pixels come back signed, one-byte pixels as 0..255
                Assert.Equal(data.Select(v => bytePix switch { 4 => v, 2 => (int)(short)v, _ => v & 0xFF }), decoded);
            }
        }
    }

    [Fact]
    public void Rice_truncated_data_throws_FitsException()
    {
        byte[] encoded = RiceCodec.Encode(Enumerable.Range(0, 500).Select(i => i * 7919 % 1000).ToArray(), 32, 4);
        Assert.Throws<FitsException>(() => RiceCodec.Decode(encoded[..(encoded.Length / 2)], 500, 32, 4));
        Assert.Throws<FitsException>(() => RiceCodec.Decode([], 5, 32, 4));
        Assert.Throws<FitsException>(() => RiceCodec.Decode(encoded, 500, 32, 3));
    }

    // ---- Writing ---------------------------------------------------------------------------------------------------

    private static short[] Ramp() => Enumerable.Range(0, 37 * 23).Select(i => (short)((i * 31) % 4000 - 2000 + (i % 7))).ToArray();

    [Theory]
    [InlineData(CompressionAlgorithm.Rice)]
    [InlineData(CompressionAlgorithm.Gzip1)]
    [InlineData(CompressionAlgorithm.Gzip2)]
    [InlineData(CompressionAlgorithm.NoCompress)]
    public void Integer_images_roundtrip_exactly_with_every_lossless_algorithm(CompressionAlgorithm algorithm)
    {
        short[] pixels = Ramp();
        var comp = CompressedImageHdu.Create<short>(pixels, [37, 23], new CompressionOptions { Algorithm = algorithm, TileShape = [16, 8] });
        var file = new FitsFile().Add(comp);
        var ms = new MemoryStream();
        file.Save(ms);
        ms.Position = 0;
        using var back = FitsFile.Read(ms, new FitsReadOptions(true));
        var read = Assert.IsType<CompressedImageHdu>(back[1]);
        Assert.Equal(pixels, read.ReadPixels<short>());
        Assert.Equal(9, read.RowCount); // 3 x 3 tiles
    }

    [Fact]
    public void Tile_count_matches_the_tile_shape()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23], new CompressionOptions { TileShape = [16, 8] });
        Assert.Equal(3 * 3, comp.RowCount);
        var rows = CompressedImageHdu.Create<short>(Ramp(), [37, 23]); // default: one row per tile
        Assert.Equal(23, rows.RowCount);
    }

    [Theory]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    public void Every_integer_type_roundtrips_through_rice(Type type)
    {
        typeof(CompressionTests).GetMethod(nameof(RiceRoundTrip), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(type).Invoke(null, null);
    }

    private static void RiceRoundTrip<T>() where T : unmanaged, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
    {
        T[] data = [T.MinValue, T.CreateTruncating(1), T.Zero, T.MaxValue, T.CreateTruncating(7), T.CreateTruncating(3), T.MaxValue, T.MinValue];
        var comp = CompressedImageHdu.Create<T>(data, [4, 2], new CompressionOptions { TileShape = [4, 1] });
        Assert.Equal(data, comp.ReadPixels<T>());
    }

    [Fact]
    public void Sixty_four_bit_images_work_with_gzip_and_are_refused_by_rice()
    {
        long[] data = [long.MinValue, -1, 0, long.MaxValue, 1L << 40, 5];
        var comp = CompressedImageHdu.Create<long>(data, [3, 2], new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip2 });
        Assert.Equal(data, comp.ReadPixels<long>());
        Assert.Throws<NotSupportedException>(() => CompressedImageHdu.Create<long>(data, [3, 2]));
    }

    [Fact]
    public void Header_cards_of_the_original_image_are_kept()
    {
        var image = ImageHdu.Create<ushort>([1, 2, 65535, 4], 2, 2);
        image.Header.Set("OBJECT", "M31", "target");
        var comp = image.Compress();
        Assert.Equal("M31", comp.Header.Get<string>("OBJECT"));
        Assert.Equal(32768L, comp.Header.Get<long>("BZERO"));
        Assert.Equal(new ushort[] { 1, 2, 65535, 4 }, comp.ReadPixels<ushort>());
        Assert.Equal("M31", comp.Decompress().Header.Get<string>("OBJECT"));
    }

    [Fact]
    public void Compressed_integer_data_is_smaller_than_the_raw_data_for_smooth_images()
    {
        short[] smooth = Enumerable.Range(0, 64 * 64).Select(i => (short)(1000 + (i % 64) / 4 + (i / 64) / 8)).ToArray();
        var comp = CompressedImageHdu.Create<short>(smooth, [64, 64], new CompressionOptions { TileShape = [64, 16] });
        Assert.True(comp.DataLength < smooth.Length * 2 / 3, $"{comp.DataLength} bytes");
    }

    // ---- Floating point ---------------------------------------------------------------------------------------------

    private static float[] Noisy()
    {
        var rng = new Random(11);
        return Enumerable.Range(0, 64 * 32).Select(i => (float)(1000 + 50 * Math.Sin(i / 40.0) + rng.NextDouble() * 4)).ToArray();
    }

    [Theory]
    [InlineData(QuantizationMethod.NoDither)]
    [InlineData(QuantizationMethod.SubtractiveDither1)]
    [InlineData(QuantizationMethod.SubtractiveDither2)]
    public void Quantized_floats_come_back_within_the_quantization_step(QuantizationMethod method)
    {
        float[] pixels = Noisy();
        var options = new CompressionOptions { Quantization = method, QuantizeLevel = 16, TileShape = [64, 8], DitherSeed = 17 };
        var comp = CompressedImageHdu.Create<float>(pixels, [64, 32], options);
        float[] back = comp.ReadPixels<float>();
        var scales = comp.ReadColumn<double>("ZSCALE");
        Assert.All(scales, s => Assert.InRange(s, 1e-6, 5));
        double max = scales.Max();
        for (int i = 0; i < pixels.Length; i++) Assert.InRange(Math.Abs(pixels[i] - back[i]), 0, max * 0.51 + 1e-4);
        Assert.Equal("RICE_1", comp.Algorithm);
    }

    [Fact]
    public void Dithered_and_undithered_files_decode_differently_but_both_reproduce_the_image()
    {
        float[] pixels = Noisy();
        var a = CompressedImageHdu.Create<float>(pixels, [64, 32], new CompressionOptions { Quantization = QuantizationMethod.NoDither });
        var b = CompressedImageHdu.Create<float>(pixels, [64, 32], new CompressionOptions { Quantization = QuantizationMethod.SubtractiveDither1 });
        Assert.False(a.Header.Contains("ZDITHER0"));
        Assert.Equal(1L, b.Header.Get<long>("ZDITHER0"));
        Assert.NotEqual(a.ReadPixels<float>(), b.ReadPixels<float>());
    }

    [Fact]
    public void Nan_pixels_survive_quantization_and_exact_zeros_survive_dither_two()
    {
        float[] pixels = Noisy();
        pixels[3] = float.NaN;
        pixels[100] = float.NaN;
        pixels[200] = 0f;
        var comp = CompressedImageHdu.Create<float>(pixels, [64, 32],
            new CompressionOptions { Quantization = QuantizationMethod.SubtractiveDither2, TileShape = [64, 8] });
        Assert.True(comp.Header.Contains("ZBLANK"));
        float[] back = comp.ReadPixels<float>();
        Assert.True(float.IsNaN(back[3]) && float.IsNaN(back[100]));
        Assert.Equal(0f, back[200]);
        Assert.Equal(2, back.Count(float.IsNaN));
    }

    [Fact]
    public void Floats_without_quantization_are_stored_losslessly_with_gzip()
    {
        float[] pixels = Noisy();
        var comp = CompressedImageHdu.Create<float>(pixels, [64, 32],
            new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip2, Quantization = QuantizationMethod.None });
        Assert.Equal(pixels, comp.ReadPixels<float>());
        var d = CompressedImageHdu.Create<double>(pixels.Select(x => x * 1.0000001).ToArray(), [64, 32],
            new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip1, Quantization = QuantizationMethod.None });
        Assert.Equal(pixels.Select(x => x * 1.0000001), d.ReadPixels<double>());
        Assert.Throws<InvalidOperationException>(() =>
            CompressedImageHdu.Create<float>(pixels, [64, 32], new CompressionOptions { Quantization = QuantizationMethod.None }));
    }

    [Fact]
    public void Tiles_that_cannot_be_quantized_fall_back_to_lossless_gzip()
    {
        var pixels = new float[64 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = i * 0.25f;
        pixels[70] = float.PositiveInfinity; // infinity cannot be mapped to an integer
        var comp = CompressedImageHdu.Create<float>(pixels, [64, 4], new CompressionOptions { TileShape = [64, 1] });
        Assert.True(comp.IndexOf("GZIP_COMPRESSED_DATA") >= 0);
        float[] back = comp.ReadPixels<float>();
        Assert.Equal(pixels[64..128], back[64..128]); // the tile with the infinity is stored exactly
        for (int i = 0; i < pixels.Length; i++)
            if (i is < 64 or >= 128) Assert.InRange(Math.Abs(back[i] - pixels[i]), 0, 1e-5);
    }

    [Fact]
    public void Noise_free_ramps_are_quantized_finely_instead_of_stored_losslessly()
    {
        var pixels = new float[64 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = i * 0.25f; // no measurable noise
        var comp = CompressedImageHdu.Create<float>(pixels, [64, 4], new CompressionOptions { TileShape = [64, 1] });
        Assert.True(comp.IndexOf("GZIP_COMPRESSED_DATA") < 0);
        float[] back = comp.ReadPixels<float>();
        for (int i = 0; i < pixels.Length; i++) Assert.InRange(Math.Abs(back[i] - pixels[i]), 0, 1e-5);
    }

    [Fact]
    public void Very_narrow_tiles_are_quantized_too()
    {
        float[] pixels = Enumerable.Range(0, 4 * 40).Select(i => (float)(100 + Math.Sin(i * 1.7) * 3)).ToArray();
        var comp = CompressedImageHdu.Create<float>(pixels, [4, 40], new CompressionOptions { TileShape = [4, 4] });
        Assert.True(comp.IndexOf("GZIP_COMPRESSED_DATA") < 0);
        Assert.Equal(160, comp.ReadPixels<float>().Length);
    }

    [Fact]
    public void Constant_tiles_and_one_dimensional_images_work()
    {
        var comp = CompressedImageHdu.Create<float>(Enumerable.Repeat(3.5f, 50).ToArray(), [50]);
        Assert.Equal(Enumerable.Repeat(3.5f, 50), comp.ReadPixels<float>());
        var line = CompressedImageHdu.Create<int>(Enumerable.Range(0, 1000).ToArray(), [1000], new CompressionOptions { TileShape = [128] });
        Assert.Equal(Enumerable.Range(0, 1000), line.ReadPixels<int>());
    }

    [Fact]
    public void Three_dimensional_images_with_edge_tiles_roundtrip()
    {
        int[] data = Enumerable.Range(0, 7 * 5 * 3).Select(i => i * 13 % 997).ToArray();
        var comp = CompressedImageHdu.Create<int>(data, [7, 5, 3], new CompressionOptions { TileShape = [4, 3, 2] });
        Assert.Equal(2 * 2 * 2, comp.RowCount);
        Assert.Equal(data, comp.ReadPixels<int>());
    }

    [Fact]
    public void Quantizer_dither_sequence_matches_the_specification()
    {
        // The specification states that the 10000th value of the sequence comes from seed 1043618065.
        double seed = 1043618065.0 / 2147483647.0;
        Assert.Equal((float)seed, Quantizer.RandomValue(Quantizer.RandomCount - 1));
        Assert.All(Enumerable.Range(0, Quantizer.RandomCount), i => Assert.InRange(Quantizer.RandomValue(i), 0f, 1f));
    }

    // ---- Malformed input ----------------------------------------------------------------------------------------

    [Fact]
    public void Missing_or_inconsistent_compression_keywords_are_reported()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        comp.Header.Set("ZNAXIS1", 40);
        Assert.Throws<FitsException>(() => comp.Decompress()); // 23 tiles expected but different layout
        var noType = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        noType.Header.Remove("ZCMPTYPE");
        Assert.Throws<FitsException>(() => noType.Decompress());
        var unknown = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        unknown.Header.Set("ZCMPTYPE", "FOO_1");
        Assert.Throws<FitsException>(() => unknown.Decompress());
    }

    [Fact]
    public void Corrupt_compressed_tiles_throw_FitsException()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23], new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip1 });
        byte[] data = comp.Source.ReadAll();
        int heapStart = (int)(comp.RowCount * comp.RowWidth);
        for (int i = heapStart; i < data.Length; i += 7) data[i] ^= 0xFF; // damage the compressed tiles in the heap
        comp.Source = new MemoryDataSource(data);
        Assert.Throws<FitsException>(() => comp.Decompress());
    }

    [Fact]
    public void Absurd_image_sizes_are_rejected_before_allocating()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        comp.Header.Set("ZNAXIS1", 2_000_000_000L).Set("ZNAXIS2", 2_000_000_000L).Set("ZTILE1", 2_000_000_000L).Set("ZTILE2", 2_000_000_000L);
        Assert.Throws<FitsException>(() => comp.Decompress());
    }
}
