using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>Regression tests for defects found in the milestone 5 code review.</summary>
public class CompressionReviewTests
{
    private static short[] Ramp(int n = 37 * 23) => Enumerable.Range(0, n).Select(i => (short)((i * 31) % 4000 - 2000 + (i % 7))).ToArray();

    private static float[] Noisy(int n)
    {
        var rng = new Random(4);
        return Enumerable.Range(0, n).Select(i => (float)(500 + 20 * Math.Sin(i / 17.0) + rng.NextDouble() * 3)).ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10001)]
    public void Invalid_dither_seeds_are_reported_as_FitsException(int seed)
    {
        var comp = CompressedImageHdu.Create<float>(Noisy(64), [8, 8], new CompressionOptions { TileShape = [8, 2] });
        comp.Header.Set("ZDITHER0", seed);
        Assert.Throws<FitsException>(() => comp.Decompress());
    }

    [Fact]
    public void Lossy_hcompress_saturates_instead_of_wrapping_around()
    {
        var pixels = new byte[64 * 64];
        var rng = new Random(9);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 11 == 0 ? 0 : 253 + rng.Next(3));
        var comp = CompressedImageHdu.Create<byte>(pixels, [64, 64],
            new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [64, 64], HCompressScale = 6 });
        byte[] back = comp.ReadPixels<byte>();
        int bad = 0;
        for (int i = 0; i < pixels.Length; i++)
            if (Math.Abs(pixels[i] - back[i]) > 40) bad++;
        Assert.Equal(0, bad);
    }

    [Fact]
    public void Smoothed_lossy_hcompress_streams_are_refused_explicitly()
    {
        var comp = CompressedImageHdu.Create<int>(Enumerable.Range(0, 256).ToArray(), [16, 16],
            new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [16, 16], HCompressScale = 4 });
        comp.Header.Set("ZVAL2", 1); // SMOOTH = 1
        Assert.Throws<NotSupportedException>(() => comp.Decompress());
        comp.Header.Set("ZVAL2", 0);
        Assert.Equal(256, comp.ReadPixels<int>().Length);
    }

    [Fact]
    public void Hcompress_default_tiling_never_leaves_a_one_pixel_thin_tile()
    {
        foreach (int rows in new[] { 17, 33, 49, 16, 15, 3 })
        {
            int[] data = Enumerable.Range(0, 20 * rows).Select(i => i * 7 % 1000).ToArray();
            var comp = CompressedImageHdu.Create<int>(data, [20, rows], new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress });
            Assert.Equal(data, comp.ReadPixels<int>());
        }
        var ex = Assert.Throws<ArgumentException>(() =>
            CompressedImageHdu.Create<int>(new int[20 * 5], [20, 5], new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [20, 4] }));
        Assert.Contains("2 x 2", ex.Message);
        Assert.Throws<ArgumentException>(() =>
            CompressedImageHdu.Create<int>(new int[20], [20, 1], new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress }));
    }

    [Fact]
    public void The_hcompress_scale_in_the_header_is_never_negative()
    {
        var noise = new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, HCompressScale = -2, QuantizeLevel = 8, TileShape = [16, 16] };
        var comp = CompressedImageHdu.Create<float>(Noisy(32 * 32), [32, 32], noise);
        Assert.Equal(0L, comp.Header.Get<long>("ZVAL1"));
        Assert.Equal(1024, comp.ReadPixels<float>().Length);
    }

    [Fact]
    public void Forged_huge_tiles_fail_fast_with_FitsException()
    {
        foreach (var algo in new[] { CompressionAlgorithm.Rice, CompressionAlgorithm.Gzip1, CompressionAlgorithm.Gzip2, CompressionAlgorithm.HCompress })
        {
            var comp = CompressedImageHdu.Create<int>(Enumerable.Range(0, 16 * 16).ToArray(), [16, 16],
                new CompressionOptions { Algorithm = algo, TileShape = [16, 16] });
            comp.Header.Set("ZNAXIS1", 1L << 27).Set("ZNAXIS2", 1).Set("ZTILE1", 1L << 27).Set("ZTILE2", 1);
            var ex = Record.Exception(() => comp.Decompress());
            Assert.IsType<FitsException>(ex);
        }
        var tooBig = CompressedImageHdu.Create<int>(new int[256], [16, 16], new CompressionOptions { TileShape = [16, 16] });
        tooBig.Header.Set("ZNAXIS1", 1L << 40).Set("ZTILE1", 1L << 40);
        Assert.Throws<FitsException>(() => tooBig.Decompress());
    }

    [Fact]
    public void Decompress_then_Compress_gives_a_valid_header_without_duplicates()
    {
        var first = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        first.Header.Set("EXTNAME", "COMPRESSED_IMAGE");
        var image = first.Decompress();
        Assert.False(image.Header.Contains("ZNAXIS"));
        Assert.False(image.Header.Contains("EXTNAME"));
        var again = image.Compress();
        var keys = again.Header.Where(c => c.Kind == FitsCardKind.Value).Select(c => c.Keyword).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(Ramp(), again.ReadPixels<short>());
    }

    [Fact]
    public void Sixty_four_bit_data_with_integer_codecs_is_rejected()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        comp.Header.Set("ZBITPIX", 64);
        Assert.Throws<FitsException>(() => comp.Decompress());
    }

    [Fact]
    public void A_missing_zval_keeps_the_default_instead_of_zero()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        comp.Header.Remove("ZVAL1"); // BLOCKSIZE
        Assert.Equal(Ramp(), comp.ReadPixels<short>());
    }

    [Fact]
    public void Uncompressed_data_is_only_used_when_the_compressed_cell_is_empty()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23], new CompressionOptions { TileShape = [37, 23] });
        // a table with a valid compressed tile and a different (wrong) uncompressed copy: the compressed one wins
        var cols = BinaryTableHdu.Create(
        [
            comp.ReadColumns()[0],
            TableColumn.CreateVariable<byte>("UNCOMPRESSED_DATA", [new byte[37 * 23 * 2]]),
        ]);
        var both = new CompressedImageHdu(cols.Header, cols.Source);
        foreach (var card in comp.Header)
            if (card.Kind == FitsCardKind.Value && (card.Keyword.StartsWith('Z') || card.Keyword == "EXTNAME") && !both.Header.Contains(card.Keyword))
                both.Header.Add(card);
        Assert.Equal(Ramp(), both.ReadPixels<short>());
    }

    [Fact]
    public void Editing_a_compressed_image_as_a_table_is_refused_with_a_clear_message()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        Assert.Throws<NotSupportedException>(() => comp.RemoveColumn(0));
        Assert.Throws<NotSupportedException>(() => comp.RemoveRows(0, 1));
        Assert.Throws<NotSupportedException>(() => comp.AddColumn(TableColumn.Create<int>("X", new int[comp.RowCount])));
        Assert.Throws<NotSupportedException>(() => comp.SetColumns([]));
        Assert.Throws<NotSupportedException>(() => comp.AppendRows([]));
        Assert.Throws<NotSupportedException>(() => CompressedImageHdu.Create([TableColumn.Create<int>("X", [1])]));
        Assert.Equal(Ramp(), comp.ReadPixels<short>()); // still intact
    }

    [Fact]
    public void Without_zblank_no_pixel_is_turned_into_nan()
    {
        float[] pixels = Noisy(64);
        var comp = CompressedImageHdu.Create<float>(pixels, [8, 8], new CompressionOptions { TileShape = [8, 8] });
        Assert.False(comp.Header.Contains("ZBLANK"));
        Assert.DoesNotContain(comp.ReadPixels<float>(), float.IsNaN);
    }

    [Fact]
    public void Huge_tile_shapes_do_not_overflow_the_tiling()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23], new CompressionOptions { TileShape = [37, 23] });
        // a tile larger than the image is legal (one tile covers everything) and must not overflow the tile count
        comp.Header.Set("ZTILE1", long.MaxValue - 1).Set("ZTILE2", long.MaxValue);
        Assert.Equal(Ramp(), comp.ReadPixels<short>());
        // ... but a forged tile count is still caught
        comp.Header.Set("ZTILE1", 1).Set("ZTILE2", 1);
        Assert.Throws<FitsException>(() => comp.Decompress());
    }

    // ---- parallel tile processing ----------------------------------------------------------------------------------

    public static IEnumerable<object[]> AllAlgorithms() =>
        Enum.GetValues<CompressionAlgorithm>().Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void Parallel_and_sequential_compression_give_identical_files(CompressionAlgorithm algorithm)
    {
        int[] data = Enumerable.Range(0, 96 * 80).Select(i => (i * 37) % 900 + (i % 5)).ToArray();
        byte[] Write(int degree)
        {
            var comp = CompressedImageHdu.Create<int>(data, [96, 80],
                new CompressionOptions { Algorithm = algorithm, TileShape = [96, 8], MaxDegreeOfParallelism = degree });
            var ms = new MemoryStream();
            new FitsFile().Add(comp).Save(ms);
            return ms.ToArray();
        }
        byte[] sequential = Write(1);
        Assert.Equal(sequential, Write(-1));
        Assert.Equal(sequential, Write(3));

        using var f = FitsFile.Read(new MemoryStream(sequential));
        var read = (CompressedImageHdu)f[1];
        Assert.Equal(data, read.Decompress(1).ReadPixels<int>());
        Assert.Equal(data, read.Decompress(-1).ReadPixels<int>());
    }

    [Fact]
    public void Quantized_floats_are_identical_whatever_the_parallelism()
    {
        float[] pixels = Noisy(128 * 64);
        CompressedImageHdu Make(int degree) => CompressedImageHdu.Create<float>(pixels, [128, 64],
            new CompressionOptions { TileShape = [128, 4], MaxDegreeOfParallelism = degree, DitherSeed = 33 });
        var a = Make(1);
        var b = Make(-1);
        Assert.Equal(a.Source.ReadAll(), b.Source.ReadAll());
        Assert.Equal(a.Decompress(1).ReadPixels<float>(), b.Decompress(-1).ReadPixels<float>());
    }

    [Fact]
    public void Concurrent_use_of_the_codecs_from_many_threads_is_safe()
    {
        float[] pixels = Noisy(64 * 64);
        byte[] reference = CompressedImageHdu.Create<float>(pixels, [64, 64], new CompressionOptions { TileShape = [64, 4] }).Source.ReadAll();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 32, i =>
        {
            var comp = CompressedImageHdu.Create<float>(pixels, [64, 64], new CompressionOptions { TileShape = [64, 4] });
            if (!comp.Source.ReadAll().AsSpan().SequenceEqual(reference)) failures.Add("different output");
            float[] back = comp.ReadPixels<float>();
            if (back.Length != pixels.Length) failures.Add("wrong length");
        });
        Assert.Empty(failures);
    }

    [Fact]
    public void Exceptions_from_worker_threads_surface_with_their_own_type()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23], new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip1 });
        byte[] data = comp.Source.ReadAll();
        for (int i = (int)(comp.RowCount * comp.RowWidth); i < data.Length; i += 5) data[i] ^= 0x5A;
        comp.Source = new MemoryDataSource(data);
        Assert.IsType<FitsException>(Record.Exception(() => comp.Decompress(-1)));
        Assert.IsType<ArgumentException>(Record.Exception(() => CompressedImageHdu.Create<int>(new int[20 * 5], [20, 5],
            new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [20, 4], MaxDegreeOfParallelism = -1 })));
    }

    // ---- findings of the performance review -------------------------------------------------------------------------

    [Theory]
    [InlineData(1 << 26)]
    [InlineData(1 << 28)]
    [InlineData(1 << 30)]
    public void Hcompress_is_lossless_for_large_pixel_values(int magnitude)
    {
        var rng = new Random(magnitude);
        foreach (var (w, h) in new[] { (8, 8), (64, 64), (33, 17), (4096, 16) })
        {
            int[] data = Enumerable.Range(0, w * h).Select(_ => rng.Next(-magnitude, magnitude)).ToArray();
            var comp = CompressedImageHdu.Create<int>(data, [w, h],
                new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [w, h] });
            Assert.Equal(data, comp.ReadPixels<int>());
        }
    }

    [Fact]
    public void Hcompress_handles_the_extreme_32_bit_values()
    {
        int[] data = [int.MinValue, int.MaxValue, 0, int.MinValue + 1, int.MaxValue - 1, -1, 1, int.MinValue, int.MaxValue];
        var padded = new int[16];
        data.CopyTo(padded, 0);
        var comp = CompressedImageHdu.Create<int>(padded, [4, 4],
            new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [4, 4] });
        Assert.Equal(padded, comp.ReadPixels<int>());
    }

    [Fact]
    public void Quantized_floats_with_a_huge_dynamic_range_work_with_hcompress()
    {
        var rng = new Random(2);
        float[] pixels = Enumerable.Range(0, 64 * 64).Select(i => (float)(1e5 * Math.Sin(i / 400.0) + rng.NextDouble() * 0.01)).ToArray();
        var comp = CompressedImageHdu.Create<float>(pixels, [64, 64],
            new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [64, 16], QuantizeLevel = 4 });
        float[] back = comp.ReadPixels<float>();
        Assert.Equal(pixels.Length, back.Length);
        double max = comp.ReadColumn<double>("ZSCALE").Max();
        for (int i = 0; i < pixels.Length; i++) Assert.InRange(Math.Abs(pixels[i] - back[i]), 0, max * 0.6 + 1e-3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Invalid_parallelism_is_rejected_up_front(int degree)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CompressedImageHdu.Create<int>(new int[16], [4, 4], new CompressionOptions { MaxDegreeOfParallelism = degree }));
        var comp = CompressedImageHdu.Create<int>(new int[16], [4, 4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => comp.Decompress(degree));
    }

    [Fact]
    public void The_reported_error_does_not_depend_on_thread_timing()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(40 * 40), [40, 40], new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip1, TileShape = [40, 1] });
        byte[] data = comp.Source.ReadAll();
        for (int i = (int)(comp.RowCount * comp.RowWidth); i < data.Length; i += 11) data[i] ^= 0x33;
        comp.Source = new MemoryDataSource(data);
        var messages = Enumerable.Range(0, 30).Select(_ => Assert.Throws<FitsException>(() => comp.Decompress(-1)).Message).Distinct().ToList();
        Assert.Single(messages);

        // several thin HCOMPRESS tiles: the same (first) tile is always reported
        var ex = Enumerable.Range(0, 10).Select(_ => Assert.Throws<ArgumentException>(() =>
            CompressedImageHdu.Create<int>(new int[8 * 5], [8, 5], new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [8, 2] })).Message).Distinct();
        Assert.Single(ex);
    }

    [Fact]
    public void A_huge_rice_block_size_does_not_exhaust_memory()
    {
        var comp = CompressedImageHdu.Create<int>(Enumerable.Range(0, 64).ToArray(), [64],
            new CompressionOptions { RiceBlockSize = int.MaxValue });
        Assert.Equal(Enumerable.Range(0, 64), comp.ReadPixels<int>());
    }

    [Fact]
    public void Corrupt_rice_data_made_of_zero_bytes_fails_cleanly()
    {
        // all zero bytes is a valid stream: every difference is zero
        Assert.All(Eleris.FitsNet.Compression.RiceCodec.Decode(new byte[100_000], 1000, 32, 4), v => Assert.Equal(0, v));
        // a Rice code (fs = 3) followed by nothing but zero bytes: the unary part never ends
        var forged = new byte[100_000];
        forged[4] = 0x20;
        Assert.Throws<FitsException>(() => Eleris.FitsNet.Compression.RiceCodec.Decode(forged, 1000, 32, 4));
    }

    [Fact]
    public void Out_of_range_compression_parameters_are_reported_as_FitsException()
    {
        var comp = CompressedImageHdu.Create<short>(Ramp(), [37, 23]);
        comp.Header.Set("ZVAL1", 1e12);
        Assert.Throws<FitsException>(() => comp.Decompress());
    }
}
