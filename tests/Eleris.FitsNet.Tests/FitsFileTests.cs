namespace Eleris.FitsNet.Tests;

/// <summary>Reads files produced by astropy (reference implementation) and checks Eleris.FitsNet round trips.</summary>
public class FitsFileTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static FitsFile RoundTrip(FitsFile file)
    {
        using var ms = new MemoryStream();
        file.Save(ms);
        Assert.Equal(0, ms.Length % 2880);
        ms.Position = 0;
        return FitsFile.Read(ms, new FitsReadOptions(Strict: true));
    }

    // ---- Reading reference files ---------------------------------------------------------------------------------

    [Fact]
    public void Reads_int16_image_in_fits_axis_order()
    {
        using var f = FitsFile.Open(Fixture("int16.fits"), new FitsReadOptions(true));
        var img = Assert.IsType<ImageHdu>(f.Primary);
        Assert.Equal(BitPix.Int16, img.BitPix);
        Assert.Equal([3L, 2L], img.Axes);
        Assert.Equal(6, img.PixelCount);
        Assert.Equal(new short[] { -3, -2, -1, 0, 1, 2 }, img.ReadPixels<short>());
    }

    [Fact]
    public void Reads_unsigned_conventions()
    {
        using (var f = FitsFile.Open(Fixture("uint16.fits")))
            Assert.Equal(new ushort[] { 0, 1, 32768, 65535, 40000, 7 }, ((ImageHdu)f.Primary).ReadPixels<ushort>());
        using (var f = FitsFile.Open(Fixture("uint32.fits")))
            Assert.Equal(new uint[] { 0, 1, 4294967295, 2147483648 }, ((ImageHdu)f.Primary).ReadPixels<uint>());
        using (var f = FitsFile.Open(Fixture("uint64.fits")))
            Assert.Equal(new ulong[] { 0, 1, 9223372036854775808, 18446744073709551615 }, ((ImageHdu)f.Primary).ReadPixels<ulong>());
        using (var f = FitsFile.Open(Fixture("int8.fits")))
            Assert.Equal(new sbyte[] { -128, -1, 0, 127 }, ((ImageHdu)f.Primary).ReadPixels<sbyte>());
        using (var f = FitsFile.Open(Fixture("int64.fits")))
            Assert.Equal(new long[] { -5, 0, 1L << 40 }, ((ImageHdu)f.Primary).ReadPixels<long>());
    }

    [Fact]
    public void Unsigned_data_can_also_be_read_as_double_and_raw()
    {
        using var f = FitsFile.Open(Fixture("uint16.fits"));
        var img = (ImageHdu)f.Primary;
        Assert.Equal(new double[] { 0, 1, 32768, 65535, 40000, 7 }, img.ReadPixels<double>());
        Assert.Equal((short)-32768, img.ReadRawPixels<short>()[0]);
    }

    [Fact]
    public void Applies_bscale_bzero_and_maps_blank_to_nan()
    {
        using var f = FitsFile.Open(Fixture("scaled.fits"));
        var img = (ImageHdu)f.Primary;
        double[] px = img.ReadPixels<double>();
        Assert.Equal([10.5, 11.0, double.NaN, 12.0, 12.5, 13.0], px);
        Assert.Equal(new short[] { 1, 2, -32768, 4, 5, 6 }, img.ReadRawPixels<short>());
        Assert.Throws<InvalidOperationException>(() => img.ReadPixels<short>());
    }

    [Fact]
    public void Reads_multi_extension_file()
    {
        using var f = FitsFile.Open(Fixture("multi.fits"), new FitsReadOptions(true));
        Assert.Equal(3, f.Count);
        var primary = Assert.IsType<ImageHdu>(f[0]);
        Assert.Empty(primary.Axes);
        Assert.Equal(0, primary.PixelCount);
        Assert.Equal("Fits Net", primary.Header.Get<string>("OBSERVER"));

        var sci = Assert.IsType<ImageHdu>(f[1]);
        Assert.Equal("SCI", sci.Header.Get<string>("EXTNAME"));
        Assert.Equal([4L, 3L], sci.Axes);
        Assert.Equal(30.5, sci.Header.Get<double>("EXPTIME"));
        Assert.Equal("seconds", sci.Header.Find("EXPTIME")!.Comment);
        double[] sciPx = sci.ReadPixels<double>();
        Assert.Equal(12, sciPx.Length);
        Assert.Equal(1.0, sciPx[^1], 12);
        Assert.Equal(1.0 / 11, sciPx[1], 12);

        var cube = Assert.IsType<ImageHdu>(f[2]);
        Assert.Equal([2L, 2L, 2L], cube.Axes);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, cube.ReadPixels<int>());
    }

    [Fact]
    public void Reads_random_groups()
    {
        using var f = FitsFile.Open(Fixture("groups.fits"));
        var g = Assert.IsType<RandomGroupsHdu>(f.Primary);
        Assert.Equal(3, g.GroupCount);
        Assert.Equal(2, g.ParameterCount);
        Assert.Equal(BitPix.Single, g.BitPix);
        Assert.Equal([2L, 2L, 1L, 1L], g.DataAxes);
        Assert.Equal(4, g.ElementsPerGroup);
        Assert.Equal("U", g.ParameterName(0));
        Assert.Equal([2.0, 20.0], g.ReadParameters(1));
        Assert.Equal([4.0, 5.0, 6.0, 7.0], g.ReadData(1));
        Assert.Equal(new float[] { 3f, 30f, 8f, 9f, 10f, 11f }, g.ReadRawGroup<float>(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.ReadData(3));
    }

    [Fact]
    public void Lazy_open_and_eager_read_give_the_same_pixels()
    {
        using var lazy = FitsFile.Open(Fixture("multi.fits"));
        using var stream = File.OpenRead(Fixture("multi.fits"));
        var eager = FitsFile.Read(stream);
        Assert.Equal(((ImageHdu)lazy[1]).ReadPixels<double>(), ((ImageHdu)eager[1]).ReadPixels<double>());
    }

    // ---- Writing and round trips ---------------------------------------------------------------------------------

    [Fact]
    public void Roundtrips_every_reference_file_through_save_and_read()
    {
        foreach (string name in new[] { "int16", "uint16", "scaled", "multi", "uint32", "int64", "uint64", "int8", "groups" })
        {
            using var original = FitsFile.Open(Fixture(name + ".fits"));
            using var copy = RoundTrip(original);
            Assert.Equal(original.Count, copy.Count);
            for (int i = 0; i < original.Count; i++)
                Assert.Equal(original[i].Source.ReadAll(), copy[i].Source.ReadAll());
        }
    }

    [Theory]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    public void Created_images_roundtrip_for_every_pixel_type(Type type)
    {
        var method = typeof(FitsFileTests).GetMethod(nameof(CreatedRoundTrip), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.MakeGenericMethod(type).Invoke(null, null);
    }

    private static void CreatedRoundTrip<T>() where T : unmanaged, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
    {
        T[] pixels = [T.MinValue, T.CreateTruncating(1), T.Zero, T.MaxValue, T.CreateTruncating(7), T.CreateTruncating(3)];
        var file = new FitsFile().Add(ImageHdu.Create<T>(pixels, 3, 2));
        using var back = RoundTrip(file);
        var img = Assert.IsType<ImageHdu>(back.Primary);
        Assert.Equal([3L, 2L], img.Axes);
        Assert.Equal(pixels, img.ReadPixels<T>());
    }

    [Fact]
    public void Written_unsigned_images_carry_the_standard_bzero()
    {
        var img = ImageHdu.Create<ushort>([1, 2, 65535], 3);
        Assert.Equal(BitPix.Int16, img.BitPix);
        Assert.Equal(32768L, img.Header.Get<long>("BZERO"));
        var u64 = ImageHdu.Create<ulong>([ulong.MaxValue], 1);
        Assert.Equal(9223372036854775808.0, u64.Header.Get<double>("BZERO"));
    }

    [Fact]
    public void Primary_and_extensions_get_the_right_mandatory_cards()
    {
        var file = new FitsFile().Add(ImageHdu.Create<float>([1f, 2f], 2)).Add(ImageHdu.Create<int>([5], 1));
        using var back = RoundTrip(file);
        Assert.Equal("SIMPLE", back[0].Header[0].Keyword);
        Assert.True(back[0].Header.Get<bool>("EXTEND"));
        var ext = back[1].Header;
        Assert.Equal(["XTENSION", "BITPIX", "NAXIS", "NAXIS1", "PCOUNT", "GCOUNT"], ext.Take(6).Select(c => c.Keyword));
        Assert.Equal("IMAGE", ext.Get<string>("XTENSION"));
        Assert.Equal(0L, ext.Get<long>("PCOUNT"));
        Assert.Equal(1L, ext.Get<long>("GCOUNT"));
    }

    [Fact]
    public void Adding_an_image_as_first_hdu_then_moving_it_converts_primary_to_extension()
    {
        var first = ImageHdu.Create<byte>([1, 2], 2);
        var second = ImageHdu.Create<byte>([3], 1);
        var file = new FitsFile().Add(second).Add(first);
        file.Insert(0, new ImageHdu());
        using var back = RoundTrip(file);
        Assert.Equal(3, back.Count);
        Assert.Equal(new byte[] { 3 }, ((ImageHdu)back[1]).ReadPixels<byte>());
        Assert.Equal(new byte[] { 1, 2 }, ((ImageHdu)back[2]).ReadPixels<byte>());
    }

    [Fact]
    public void Extension_that_cannot_be_primary_gets_an_empty_primary()
    {
        var generic = new GenericHdu(
            new FitsHeader().Set("XTENSION", "FOO").Set("BITPIX", 8).Set("NAXIS", 0).Set("PCOUNT", 0).Set("GCOUNT", 1),
            DataSource.Empty);
        var file = new FitsFile().Add(generic);
        Assert.Equal(2, file.Count);
        Assert.IsType<ImageHdu>(file[0]);
        using var back = RoundTrip(file);
        Assert.IsType<GenericHdu>(back[1]);
    }

    [Fact]
    public void Header_cards_are_preserved_in_order_after_the_mandatory_block()
    {
        var img = ImageHdu.Create<short>([1, 2, 3, 4], 2, 2);
        img.Header.Set("OBJECT", "M31", "target").AddHistory("created").Set("EXPTIME", 12.5);
        using var back = RoundTrip(new FitsFile().Add(img));
        var h = back.Primary.Header;
        Assert.Equal(["SIMPLE", "BITPIX", "NAXIS", "NAXIS1", "NAXIS2"], h.Take(5).Select(c => c.Keyword));
        Assert.Equal("M31", h.Get<string>("OBJECT"));
        Assert.Equal("target", h.Find("OBJECT")!.Comment);
        Assert.Contains(h, c => c.Kind == FitsCardKind.Commentary && c.Keyword == "HISTORY");
        Assert.Equal(12.5, h.Get<double>("EXPTIME"));
    }

    [Fact]
    public void SetPixels_replaces_data_axes_and_scaling()
    {
        var img = ImageHdu.Create<short>([1, 2, 3, 4], 2, 2);
        img.Header.Set("BSCALE", 2.0).Set("BZERO", 3.0).Set("BLANK", -1);
        img.SetPixels<float>([0.5f, 1.5f, 2.5f], 3);
        Assert.Equal(BitPix.Single, img.BitPix);
        Assert.Equal([3L], img.Axes);
        Assert.False(img.Header.Contains("BSCALE"));
        Assert.False(img.Header.Contains("BLANK"));
        Assert.Equal(new[] { 0.5f, 1.5f, 2.5f }, img.ReadPixels<float>());
    }

    [Fact]
    public void Wrong_pixel_count_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ImageHdu.Create<byte>([1, 2, 3], 2, 2));
        Assert.Throws<ArgumentException>(() => ImageHdu.Create<byte>([1], -1));
    }

    [Fact]
    public void Header_edits_that_contradict_the_data_are_caught_on_save()
    {
        var img = ImageHdu.Create<byte>([1, 2, 3, 4], 4);
        img.Header.Set("NAXIS1", 5);
        Assert.Throws<FitsException>(() => new FitsFile().Add(img).Save(new MemoryStream()));
    }

    [Fact]
    public void Random_groups_roundtrip()
    {
        float[] values = [1f, 10f, 0f, 1f, 2f, 3f, 2f, 20f, 4f, 5f, 6f, 7f];
        var g = RandomGroupsHdu.Create<float>(values, groupCount: 2, parameterCount: 2, 2, 2);
        using var back = RoundTrip(new FitsFile().Add(g));
        var r = Assert.IsType<RandomGroupsHdu>(back.Primary);
        Assert.Equal(2, r.GroupCount);
        Assert.Equal(values[6..], r.ReadRawGroup<float>(1));
        Assert.Equal([1.0, 10.0], r.ReadParameters(0));
        Assert.Throws<ArgumentException>(() => new FitsFile().Add(g).Insert(1, g));
        Assert.Throws<ArgumentException>(() => new FitsFile().Add(new ImageHdu()).Add(RandomGroupsHdu.Create<float>(values, 2, 2, 2, 2)));
    }

    [Fact]
    public void Empty_file_saves_as_an_empty_primary()
    {
        using var back = RoundTrip(new FitsFile());
        Assert.Single(back);
        Assert.Equal(0, ((ImageHdu)back.Primary).PixelCount);
    }

    [Fact]
    public void Saving_over_the_file_that_was_opened_lazily_works()
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            File.Copy(Fixture("multi.fits"), path);
            using (var f = FitsFile.Open(path))
            {
                f[1].Header.Set("EDITED", true);
                f.Add(ImageHdu.Create<byte>([9, 8, 7], 3));
                f.Save(path);
            }
            using var back = FitsFile.Open(path, new FitsReadOptions(true));
            Assert.Equal(4, back.Count);
            Assert.True(back[1].Header.Get<bool>("EDITED"));
            Assert.Equal(new byte[] { 9, 8, 7 }, ((ImageHdu)back[3]).ReadPixels<byte>());
            Assert.Equal(1.0, ((ImageHdu)back[1]).ReadPixels<double>()[^1], 12);
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".tmp-*"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Malformed input -----------------------------------------------------------------------------------------

    [Fact]
    public void Non_fits_input_is_rejected()
    {
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(new byte[5760])));
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream([])));
    }

    [Fact]
    public void Truncated_data_is_rejected()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("int16.fits"));
        var header = FitsHeader.Read(new MemoryStream(bytes));
        header.Set("NAXIS1", 5000);
        header.Write(new MemoryStream(bytes, 0, 2880, writable: true) { Position = 0 });
        byte[] hb = header.ToBytes();
        byte[] broken = hb.Concat(bytes.Skip(2880).Take(100)).ToArray();
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(broken)));
    }

    [Fact]
    public void Missing_final_padding_is_tolerated()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("int16.fits"));
        var file = FitsFile.Read(new MemoryStream(bytes[..(2880 + 12)]));
        Assert.Equal(new short[] { -3, -2, -1, 0, 1, 2 }, ((ImageHdu)file.Primary).ReadPixels<short>());
    }

    [Fact]
    public void Trailing_garbage_is_ignored_when_lenient_and_rejected_when_strict()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("int16.fits")).Concat(new byte[2880]).ToArray();
        Assert.Single(FitsFile.Read(new MemoryStream(bytes)));
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(true)));
    }

    [Fact]
    public void Invalid_bitpix_and_missing_mandatory_keywords_are_rejected()
    {
        var bad = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 12).Set("NAXIS", 0);
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(bad.ToBytes())));
        var noNaxis = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8);
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(noNaxis.ToBytes())));
    }

    [Fact]
    public void Header_sizes_that_overflow_are_rejected()
    {
        var h = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 64).Set("NAXIS", 3)
            .Set("NAXIS1", 2_000_000_000L).Set("NAXIS2", 2_000_000_000L).Set("NAXIS3", 2_000_000_000L);
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(h.ToBytes())));
    }

    [Fact]
    public void Data_size_formula_handles_zero_length_axes()
    {
        var h = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 16).Set("NAXIS", 2).Set("NAXIS1", 0).Set("NAXIS2", 5);
        using var f = FitsFile.Read(new MemoryStream(h.ToBytes()));
        Assert.Equal(0, f.Primary.DataLength);
    }
}
