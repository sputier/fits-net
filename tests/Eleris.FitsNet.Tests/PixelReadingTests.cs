namespace Eleris.FitsNet.Tests;

/// <summary>
/// Reading the pixels goes through chunks (of the file, then of the conversion): these tests use sizes around the chunk boundaries and
/// both kinds of source, a file read into memory and a file opened lazily, and compare with a naive element by element conversion.
/// </summary>
public class PixelReadingTests
{
    // 32768 values are converted at a time and 256 KB are read at a time: sizes below, on and above those limits.
    public static TheoryData<int, bool> Cases()
    {
        var data = new TheoryData<int, bool>();
        foreach (int length in new[] { 0, 1, 7, 32767, 32768, 32769, 65536 + 5, 131072, 131073, 262144, 262144 + 11 })
            foreach (bool lazy in new[] { false, true })
                data.Add(length, lazy);
        return data;
    }

    private static T[] Values<T>(int length, Func<int, T> make) => Enumerable.Range(0, length).Select(make).ToArray();

    private static short[] Shorts(int length) => Values(length, i => unchecked((short)(i * 7919 - 12345)));

    /// <summary>Saves the HDU and gives it back from a file read into memory or from a file opened lazily.</summary>
    private static T WithSource<T>(ImageHdu image, bool lazy, Func<ImageHdu, T> use)
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-pixels-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            new FitsFile().Add(image).Save(path);
            if (lazy)
            {
                using var file = FitsFile.Open(path);
                return use((ImageHdu)file.Primary);
            }
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var memory = FitsFile.Read(stream);
            return use((ImageHdu)memory.Primary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Raw_values_of_every_BITPIX_come_back_unchanged(int length, bool lazy)
    {
        byte[] bytes = Values(length, i => unchecked((byte)(i * 31)));
        short[] shorts = Shorts(length);
        int[] ints = Values(length, i => unchecked(i * 104729 - 5_000_000));
        long[] longs = Values(length, i => unchecked(i * 6_700_417L - 40_000_000_000L));
        float[] floats = Values(length, i => i * 0.37f - 1000f);
        double[] doubles = Values(length, i => i * 1.0000001 - 12345.678);

        Assert.Equal(bytes, WithSource(ImageHdu.Create<byte>(bytes, length), lazy, h => h.ReadRawPixels<byte>()));
        Assert.Equal(shorts, WithSource(ImageHdu.Create<short>(shorts, length), lazy, h => h.ReadRawPixels<short>()));
        Assert.Equal(ints, WithSource(ImageHdu.Create<int>(ints, length), lazy, h => h.ReadRawPixels<int>()));
        Assert.Equal(longs, WithSource(ImageHdu.Create<long>(longs, length), lazy, h => h.ReadRawPixels<long>()));
        Assert.Equal(floats, WithSource(ImageHdu.Create<float>(floats, length), lazy, h => h.ReadRawPixels<float>()));
        Assert.Equal(doubles, WithSource(ImageHdu.Create<double>(doubles, length), lazy, h => h.ReadRawPixels<double>()));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Unsigned_conventions_come_back_unchanged(int length, bool lazy)
    {
        sbyte[] sbytes = Values(length, i => unchecked((sbyte)(i * 37)));
        ushort[] ushorts = Values(length, i => unchecked((ushort)(i * 7919)));
        uint[] uints = Values(length, i => unchecked((uint)i * 2_654_435_761u));
        ulong[] ulongs = Values(length, i => unchecked((ulong)i * 11_400_714_819_323_198_485ul));

        Assert.Equal(sbytes, WithSource(ImageHdu.Create<sbyte>(sbytes, length), lazy, h => h.ReadPixels<sbyte>()));
        Assert.Equal(ushorts, WithSource(ImageHdu.Create<ushort>(ushorts, length), lazy, h => h.ReadPixels<ushort>()));
        Assert.Equal(uints, WithSource(ImageHdu.Create<uint>(uints, length), lazy, h => h.ReadPixels<uint>()));
        Assert.Equal(ulongs, WithSource(ImageHdu.Create<ulong>(ulongs, length), lazy, h => h.ReadPixels<ulong>()));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Physical_values_match_an_element_by_element_conversion(int length, bool lazy)
    {
        short[] stored = Shorts(length);
        var image = ImageHdu.Create<short>(stored, length);
        image.Header.Set("BSCALE", 0.0123).Set("BZERO", 4.5).Set("BLANK", (long)stored.DefaultIfEmpty((short)0).First());

        long blank = stored.Length > 0 ? stored[0] : 0;
        double[] expectedDouble = stored.Select(v => v == blank ? double.NaN : 4.5 + 0.0123 * v).ToArray();
        float[] expectedFloat = expectedDouble.Select(v => (float)v).ToArray();

        Assert.Equal(expectedDouble, WithSource(image, lazy, h => h.ReadPixels<double>()));
        Assert.Equal(expectedFloat, WithSource(image, lazy, h => h.ReadPixels<float>()));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Unscaled_values_convert_without_changing_them(int length, bool lazy)
    {
        short[] shorts = Shorts(length);
        Assert.Equal(shorts.Select(v => (double)v), WithSource(ImageHdu.Create<short>(shorts, length), lazy, h => h.ReadPixels<double>()));
        Assert.Equal(shorts.Select(v => (float)v), WithSource(ImageHdu.Create<short>(shorts, length), lazy, h => h.ReadPixels<float>()));

        int[] ints = Values(length, i => unchecked(i * 104729 - 5_000_000));
        Assert.Equal(ints.Select(v => (float)(double)v), WithSource(ImageHdu.Create<int>(ints, length), lazy, h => h.ReadPixels<float>()));

        long[] longs = Values(length, i => unchecked(i * 6_700_417_000_001L + 9_007_199_254_740_993L));
        Assert.Equal(longs.Select(v => (float)(double)v), WithSource(ImageHdu.Create<long>(longs, length), lazy, h => h.ReadPixels<float>()));

        float[] floats = Values(length, i => i * 0.37f - 1000f);
        Assert.Equal(floats.Select(v => (double)v), WithSource(ImageHdu.Create<float>(floats, length), lazy, h => h.ReadPixels<double>()));

        byte[] bytes = Values(length, i => unchecked((byte)(i * 31)));
        Assert.Equal(bytes.Select(v => (float)v), WithSource(ImageHdu.Create<byte>(bytes, length), lazy, h => h.ReadPixels<float>()));
    }

    [Fact]
    public void Floating_point_pixels_ignore_BLANK()
    {
        float[] floats = [1.5f, float.NaN, -2.25f];
        var image = ImageHdu.Create<float>(floats, 3);
        image.Header.Set("BLANK", 1L);
        Assert.Equal([1.5, double.NaN, -2.25], WithSource(image, false, h => h.ReadPixels<double>()));
    }

    [Fact]
    public void A_file_opened_lazily_can_be_read_again_and_again()
    {
        short[] shorts = Shorts(70_000);
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-pixels-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            new FitsFile().Add(ImageHdu.Create<short>(shorts, 70_000)).Save(path);
            using var file = FitsFile.Open(path);
            var image = (ImageHdu)file.Primary;
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(shorts, image.ReadRawPixels<short>());
                Assert.Equal(shorts.Select(v => (float)v), image.ReadPixels<float>());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Scaling applies to every BITPIX, with BZERO alone as well as with BSCALE.
    [Theory]
    [InlineData(1.0, 100.5)]
    [InlineData(2.5, 0.0)]
    [InlineData(-0.75, 12.25)]
    public void Scaling_applies_to_every_BITPIX(double bscale, double bzero)
    {
        const int length = 70_001;
        byte[] bytes = Values(length, i => unchecked((byte)(i * 31)));
        int[] ints = Values(length, i => unchecked(i * 104729 - 5_000_000));
        long[] longs = Values(length, i => unchecked(i * 6_700_417L - 40_000_000_000L));
        float[] floats = Values(length, i => i * 0.37f - 1000f);
        double[] doubles = Values(length, i => i * 1.0000001 - 12345.678);

        void Check<T>(T[] stored, Func<T, double> toDouble, Func<T, long>? toLong = null, long blank = 0)
            where T : unmanaged
        {
            var image = ImageHdu.Create<T>(stored, length);
            image.Header.Set("BSCALE", bscale).Set("BZERO", bzero);
            if (toLong is not null) image.Header.Set("BLANK", blank);
            double[] expected = stored.Select(v => toLong is not null && toLong(v) == blank ? double.NaN : bzero + bscale * toDouble(v)).ToArray();
            foreach (bool lazy in new[] { false, true })
            {
                Assert.Equal(expected, WithSource(image, lazy, h => h.ReadPixels<double>()));
                Assert.Equal(expected.Select(v => (float)v), WithSource(image, lazy, h => h.ReadPixels<float>()));
            }
        }

        Check(bytes, v => v, v => v, blank: bytes[100]);
        Check(ints, v => v, v => v, blank: ints[100]);
        Check(longs, v => v, v => v, blank: longs[100]);
        Check(floats, v => v);
        Check(doubles, v => v);
    }

    [Fact]
    public void Data_that_ends_early_fails_instead_of_returning_a_partly_filled_array()
    {
        // 300 000 bytes announced, 100 000 present: the failure happens in the middle of the chunks.
        using var stream = new MemoryStream(new byte[100_000]);
        var source = new StreamDataSource(stream, 0, 300_000);
        Assert.Throws<FitsException>(() => BigEndian.ReadArray<short>(source));
        Assert.Throws<FitsException>(() => PixelConvert.ToFloating<float>(source, BitPix.Int16, null, 1, 0));
        Assert.Throws<FitsException>(() => BigEndian.ReadArrayXor<ushort>(source, 0x8000));
    }

    [Fact]
    public void Data_that_is_not_a_whole_number_of_elements_is_refused()
    {
        var source = new MemoryDataSource(new byte[7]);
        Assert.Throws<FitsException>(() => BigEndian.ReadArray<short>(source));
        Assert.Throws<FitsException>(() => PixelConvert.ToFloating<double>(source, BitPix.Int32, null, 1, 0));
        Assert.Equal(7, BigEndian.ReadArray<byte>(source).Length);
    }
}
