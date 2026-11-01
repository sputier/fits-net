namespace Eleris.FitsNet.Tests;

[CollectionDefinition("StaticChunkSettings", DisableParallelization = true)]
public class StaticChunkSettings;

[Collection("StaticChunkSettings")]
public class RegionAndChunkTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "fitsnet-" + Guid.NewGuid().ToString("N") + ".fits");

    private static T[] Slice3D<T>(T[] data, long[] axes, long[] origin, long[] shape)
    {
        var result = new List<T>();
        for (long z = origin[2]; z < origin[2] + shape[2]; z++)
            for (long y = origin[1]; y < origin[1] + shape[1]; y++)
                for (long x = origin[0]; x < origin[0] + shape[0]; x++)
                    result.Add(data[x + axes[0] * (y + axes[1] * z)]);
        return result.ToArray();
    }

    [Theory]
    [InlineData(0, 0, 0, 7, 5, 4)]   // the whole cube
    [InlineData(2, 1, 1, 3, 2, 2)]   // an interior block
    [InlineData(0, 0, 1, 7, 5, 2)]   // whole planes
    [InlineData(0, 2, 0, 7, 2, 4)]   // whole rows, partial planes
    [InlineData(6, 4, 3, 1, 1, 1)]   // the last pixel
    [InlineData(3, 0, 0, 0, 5, 4)]   // an empty region
    public void Regions_of_a_cube_match_slicing_the_whole_array(long x, long y, long z, long w, long h, long d)
    {
        long[] axes = [7, 5, 4];
        int[] data = Enumerable.Range(0, 7 * 5 * 4).Select(i => i * 3 - 50).ToArray();
        string path = TempFile();
        try
        {
            new FitsFile().Add(ImageHdu.Create<int>(data, axes)).Save(path);
            using var f = FitsFile.Open(path);
            var image = (ImageHdu)f[0];
            long[] origin = [x, y, z], shape = [w, h, d];
            Assert.Equal(Slice3D(data, axes, origin, shape), image.ReadRawRegion<int>(origin, shape));
            Assert.Equal(shape, image.Subimage(origin, shape).Axes.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Regions_apply_scaling_and_unsigned_conventions()
    {
        ushort[] data = [0, 1, 2, 65535, 40000, 7];
        var image = ImageHdu.Create<ushort>(data, 3, 2);
        Assert.Equal(new ushort[] { 65535, 40000 }, image.ReadRegion<ushort>([0, 1], [2, 1]));
        Assert.Equal(new[] { 1.0, 2.0 }, image.ReadRegion<double>([1, 0], [2, 1]));
    }

    [Fact]
    public void Regions_shift_crpix_for_every_alternate_wcs_and_keep_the_other_cards()
    {
        var image = ImageHdu.Create<short>(new short[100], 10, 10);
        image.Header.Set("CRPIX1", 5.5).Set("CRPIX2", 3).Set("CRPIX1A", 8.0).Set("CRPIX3", 9.0).Set("OBJECT", "M31")
            .Set("CRVAL1", 10.0).Set("CHECKSUM", "AAAAAAAAAAAAAAAA").Set("DATASUM", "1");
        var sub = image.Subimage([2, 1], [4, 4]);
        Assert.Equal(3.5, sub.Header.Get<double>("CRPIX1"));
        Assert.Equal(2.0, sub.Header.Get<double>("CRPIX2"));
        Assert.Equal(6.0, sub.Header.Get<double>("CRPIX1A"));
        Assert.Equal(9.0, sub.Header.Get<double>("CRPIX3")); // no third axis: untouched
        Assert.Equal(10.0, sub.Header.Get<double>("CRVAL1"));
        Assert.Equal("M31", sub.Header.Get<string>("OBJECT"));
        Assert.False(sub.Header.Contains("CHECKSUM") || sub.Header.Contains("DATASUM"));
        Assert.Equal(5.5, image.Header.Get<double>("CRPIX1")); // the source is not modified
    }

    [Fact]
    public void Invalid_regions_are_rejected()
    {
        var image = ImageHdu.Create<short>(new short[20], 5, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Subimage([0, 0], [6, 4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Subimage([4, 0], [2, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Subimage([-1, 0], [1, 1]));
        Assert.Throws<ArgumentException>(() => image.Subimage([0], [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Subimage([long.MaxValue, 0], [1, 1]));
        Assert.Empty(image.ReadRawRegion<short>([5, 0], [0, 4]));
    }

    [Fact]
    public void A_region_of_a_lazy_file_does_not_read_the_whole_image()
    {
        string path = TempFile();
        try
        {
            new FitsFile().Add(ImageHdu.Create<short>(Enumerable.Range(0, 2000 * 2000).Select(i => (short)i).ToArray(), 2000, 2000)).Save(path);
            using var f = FitsFile.Open(path);
            var image = (ImageHdu)f[0];
            short[] cut = image.ReadRawRegion<short>([1990, 1995], [10, 5]);
            Assert.Equal(50, cut.Length);
            Assert.Equal(unchecked((short)(1990 + 2000 * 1995)), cut[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- block reading of table columns ----------------------------------------------------------------------------

    [Fact]
    public void Block_reading_of_fixed_columns_gives_the_same_values_as_whole_reading()
    {
        const int rows = 50_000;
        var table = BinaryTableHdu.Create(
        [
            TableColumn.Create<int>("ID", Enumerable.Range(0, rows).ToArray()),
            TableColumn.Create<double>("X", Enumerable.Range(0, rows * 3).Select(i => i * 0.25).ToArray(), repeat: 3),
            TableColumn.CreateString("NAME", Enumerable.Range(0, rows).Select(i => "n" + i % 977).ToArray(), 6),
            TableColumn.CreateLogical("OK", Enumerable.Range(0, rows).Select(i => (bool?)(i % 3 == 0)).ToArray()),
            TableColumn.CreateVariable<short>("V", Enumerable.Range(0, rows).Select(i => new short[] { (short)i, (short)(i + 1) }).ToArray()),
        ]);
        string path = TempFile();
        long old = BinaryTableHdu.ChunkedReadThresholdBytes;
        try
        {
            new FitsFile().Add(table).Save(path);
            using var f = FitsFile.Open(path);
            var lazy = (BinaryTableHdu)f[1];
            BinaryTableHdu.ChunkedReadThresholdBytes = long.MaxValue;
            var whole = new[] { lazy.ReadRaw(0), lazy.ReadRaw(1), lazy.ReadRaw(2), lazy.ReadRaw(3), lazy.ReadRaw(4) };
            BinaryTableHdu.ChunkedReadThresholdBytes = 0;
            for (int i = 0; i < 5; i++) Assert.Equal(whole[i], lazy.ReadRaw(i));
            Assert.Equal(Enumerable.Range(0, rows), lazy.ReadColumn<int>("ID"));
        }
        finally
        {
            BinaryTableHdu.ChunkedReadThresholdBytes = old;
            File.Delete(path);
        }
    }

    [Fact]
    public void Multi_block_and_wide_row_reads_match_whole_reads_for_every_fixed_type()
    {
        const int rows = 1500;
        var table = BinaryTableHdu.Create(
        [
            TableColumn.Create<byte>("B", Enumerable.Range(0, rows).Select(i => (byte)i).ToArray()),
            TableColumn.Create<short>("I2", Enumerable.Range(0, rows * 2).Select(i => (short)(i - 1000)).ToArray(), repeat: 2),
            TableColumn.Create<long>("K", Enumerable.Range(0, rows).Select(i => (long)i * 1_000_000_007).ToArray()),
            TableColumn.Create<float>("E", Enumerable.Range(0, rows).Select(i => i * 0.5f).ToArray()),
            TableColumn.Create<double>("D3", Enumerable.Range(0, rows * 3).Select(i => i * 0.125).ToArray(), repeat: 3),
            TableColumn.CreateComplex("M", Enumerable.Range(0, rows).Select(i => new System.Numerics.Complex(i, -i)).ToArray()),
            TableColumn.CreateComplex("C", Enumerable.Range(0, rows).Select(i => new System.Numerics.Complex(i, 0.5)).ToArray(), singlePrecision: true),
            TableColumn.CreateBits("X", Enumerable.Range(0, rows * 12).Select(i => i % 5 == 0).ToArray(), 12),
            TableColumn.CreateString("S2", Enumerable.Range(0, rows * 2).Select(i => "s" + i % 97).ToArray(), 4, stringsPerRow: 2),
            TableColumn.CreateLogical("L", Enumerable.Range(0, rows).Select(i => i % 3 == 0 ? (bool?)null : i % 2 == 0).ToArray()),
        ]);
        string path = TempFile();
        long oldThreshold = BinaryTableHdu.ChunkedReadThresholdBytes, oldChunk = BinaryTableHdu.ChunkBytes;
        try
        {
            new FitsFile().Add(table).Save(path);
            using var f = FitsFile.Open(path);
            var lazy = (BinaryTableHdu)f[1];
            BinaryTableHdu.ChunkedReadThresholdBytes = long.MaxValue;
            var whole = Enumerable.Range(0, lazy.ColumnCount).Select(lazy.ReadRaw).ToArray();
            BinaryTableHdu.ChunkedReadThresholdBytes = 0;
            // blocks of about 1 row, 7 rows, hundreds of rows, and rows wider than a block
            foreach (long chunk in new long[] { lazy.RowWidth, 7 * lazy.RowWidth, 333 * lazy.RowWidth, 1 << 20, lazy.RowWidth - 1, 3 })
            {
                BinaryTableHdu.ChunkBytes = chunk;
                for (int c = 0; c < lazy.ColumnCount; c++) Assert.Equal(whole[c], lazy.ReadRaw(c));
            }
        }
        finally
        {
            BinaryTableHdu.ChunkedReadThresholdBytes = oldThreshold;
            BinaryTableHdu.ChunkBytes = oldChunk;
            File.Delete(path);
        }
    }

    // ---- Subimage robustness ------------------------------------------------------------------------------------------

    [Fact]
    public void Regions_work_in_one_and_four_dimensions_and_with_blank_values()
    {
        short[] line = Enumerable.Range(0, 20).Select(i => (short)(i * 3)).ToArray();
        Assert.Equal(line[5..9], ImageHdu.Create<short>(line, 20).ReadRawRegion<short>([5], [4]));

        long[] axes = [3, 4, 2, 5];
        int[] data = Enumerable.Range(0, 3 * 4 * 2 * 5).ToArray();
        var cube = ImageHdu.Create<int>(data, axes);
        var got = cube.ReadRawRegion<int>([1, 1, 0, 2], [2, 3, 2, 2]);
        var expected = new List<int>();
        for (int w = 2; w < 4; w++)
            for (int z = 0; z < 2; z++)
                for (int y = 1; y < 4; y++)
                    for (int x = 1; x < 3; x++)
                        expected.Add(data[x + 3 * (y + 4 * (z + 2 * w))]);
        Assert.Equal(expected, got);

        var withBlank = ImageHdu.Create<short>([1, 2, -1, 4], 4);
        withBlank.Header.Set("BLANK", -1);
        Assert.Equal([2.0, double.NaN], withBlank.ReadRegion<double>([1], [2]));
    }

    [Fact]
    public void Regions_shift_the_ltv_offsets_and_report_which_argument_is_wrong()
    {
        var image = ImageHdu.Create<short>(new short[100], 10, 10);
        image.Header.Set("LTV1", -3.0).Set("LTV2", 0.0);
        var sub = image.Subimage([2, 4], [3, 3]);
        Assert.Equal(-5.0, sub.Header.Get<double>("LTV1"));
        Assert.Equal(-4.0, sub.Header.Get<double>("LTV2"));
        Assert.Equal("origin", Assert.Throws<ArgumentOutOfRangeException>(() => image.Subimage([11, 0], [0, 1])).ParamName);
        Assert.Equal("shape", Assert.Throws<ArgumentOutOfRangeException>(() => image.Subimage([5, 0], [6, 1])).ParamName);
    }

    [Fact]
    public void A_file_that_ends_early_gives_FitsException_not_EndOfStream()
    {
        var source = new StreamDataSource(new MemoryStream(new byte[10]), 0, 100);
        Assert.Throws<FitsException>(() => source.ReadRange(0, new byte[50]));
        Assert.Throws<FitsException>(() => source.ReadAll());
    }
}
