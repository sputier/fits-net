namespace Eleris.FitsNet.Tests;

/// <summary>Regression tests for defects found in the milestone 2 code review.</summary>
public class FitsFileReviewTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static FitsHeader PrimaryHeader() => new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 0).Set("EXTEND", true);

    private static byte[] TableFile(out int extensionStart)
    {
        var ext = new FitsHeader().Set("XTENSION", "TABLE").Set("BITPIX", 8).Set("NAXIS", 2)
            .Set("NAXIS1", 10).Set("NAXIS2", 1).Set("PCOUNT", 0).Set("GCOUNT", 1).Set("TFIELDS", 1)
            .Set("TTYPE1", "TXT").Set("TFORM1", "A10").Set("TBCOL1", 1);
        var ms = new MemoryStream();
        PrimaryHeader().Write(ms);
        extensionStart = (int)ms.Length;
        ext.Write(ms);
        ms.Write("0123456789"u8);
        ms.Write(Enumerable.Repeat((byte)' ', 2880 - 10).ToArray());
        return ms.ToArray();
    }

    [Fact]
    public void Ascii_table_padding_is_blank_and_extension_roundtrips_byte_for_byte()
    {
        byte[] input = TableFile(out int ext);
        using var file = FitsFile.Read(new MemoryStream(input));
        var output = new MemoryStream();
        file.Save(output);
        byte[] result = output.ToArray();
        Assert.Equal(input[^2880..], result[^2880..]);
        Assert.Equal((byte)' ', result[^1]);
        Assert.IsType<AsciiTableHdu>(file[1]);
    }

    [Fact]
    public void Binary_extension_padding_is_zero()
    {
        var ext = new FitsHeader().Set("XTENSION", "BINTABLE").Set("BITPIX", 8).Set("NAXIS", 2)
            .Set("NAXIS1", 4).Set("NAXIS2", 1).Set("PCOUNT", 0).Set("GCOUNT", 1).Set("TFIELDS", 1)
            .Set("TFORM1", "4B");
        var ms = new MemoryStream();
        PrimaryHeader().Write(ms);
        ext.Write(ms);
        ms.Write(new byte[2880]);
        using var file = FitsFile.Read(new MemoryStream(ms.ToArray()));
        var output = new MemoryStream();
        file.Save(output);
        Assert.Equal((byte)0, output.ToArray()[^1]);
    }

    [Fact]
    public void Hostile_sizes_throw_FitsException_on_both_read_paths()
    {
        var h = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 1).Set("NAXIS1", 9223372036854775000L);
        byte[] bytes = h.ToBytes();
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(bytes)));

        string path = Path.Combine(Path.GetTempPath(), "fitsnet-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.Throws<FitsException>(() => FitsFile.Open(path));
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } // file handle was released
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Equal(2880, HduLayout.Padded(2880));
        Assert.Equal(2880, HduLayout.Padded(1));
        Assert.Throws<FitsException>(() => HduLayout.Padded(long.MaxValue));
    }

    [Fact]
    public void Zero_length_axis_makes_the_array_empty_even_when_other_axes_would_overflow()
    {
        var h = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 3)
            .Set("NAXIS1", 1L << 62).Set("NAXIS2", 1L << 62).Set("NAXIS3", 0);
        using var file = FitsFile.Read(new MemoryStream(h.ToBytes()));
        Assert.Equal(0, file.Primary.DataLength);
        Assert.Equal(0, ((ImageHdu)file.Primary).PixelCount);
        Assert.Empty(ImageHdu.Create<byte>([], 1L << 40, 0).ReadPixels<byte>());
    }

    [Fact]
    public void Failed_save_leaves_headers_untouched_and_writes_nothing()
    {
        var good = ImageHdu.Create<byte>([1, 2], 2);
        good.Header.Set("BITPIX", 8, "my comment");
        var bad = ImageHdu.Create<byte>([1, 2], 2);
        bad.Header.Set("NAXIS1", 99);
        var file = new FitsFile().Add(good).Add(bad);
        var before = good.Header.Select(c => c.ToString()).ToList();

        string path = Path.Combine(Path.GetTempPath(), "fitsnet-" + Guid.NewGuid().ToString("N") + ".fits");
        Assert.Throws<FitsException>(() => file.Save(path));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".tmp-*"));
        Assert.Equal(before, good.Header.Select(c => c.ToString()).ToList());
    }

    [Fact]
    public void Normalization_preserves_comments_of_unchanged_mandatory_cards()
    {
        var img = ImageHdu.Create<short>([1, 2, 3], 3);
        img.Header.SetComment("BITPIX", "orig").SetComment("NAXIS1", "length");
        img.Header.Set("SIMPLE", true, "my own");
        using var back = FitsFile.Read(SaveToStream(new FitsFile().Add(img)));
        Assert.Equal("orig", back.Primary.Header.Find("BITPIX")!.Comment);
        Assert.Equal("length", back.Primary.Header.Find("NAXIS1")!.Comment);
        Assert.Equal("my own", back.Primary.Header.Find("SIMPLE")!.Comment);
    }

    private static MemoryStream SaveToStream(FitsFile f)
    {
        var ms = new MemoryStream();
        f.Save(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Image_extension_with_pcount_or_gcount_out_of_range_is_rejected()
    {
        var ms = new MemoryStream();
        PrimaryHeader().Write(ms);
        new FitsHeader().Set("XTENSION", "IMAGE").Set("BITPIX", 8).Set("NAXIS", 1).Set("NAXIS1", 4)
            .Set("PCOUNT", 0).Set("GCOUNT", 2).Write(ms);
        ms.Write(new byte[2880]);
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(ms.ToArray())));
    }

    [Fact]
    public void Strict_mode_rejects_non_integer_mandatory_values_wrong_order_and_missing_padding()
    {
        var strict = new FitsReadOptions(true);

        var floaty = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8.0).Set("NAXIS", 0);
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(floaty.ToBytes()), strict));
        Assert.NotNull(FitsFile.Read(new MemoryStream(floaty.ToBytes())));

        var misordered = new FitsHeader().Set("SIMPLE", true).Set("NAXIS", 0).Set("BITPIX", 8);
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(misordered.ToBytes()), strict));
        Assert.NotNull(FitsFile.Read(new MemoryStream(misordered.ToBytes())));

        byte[] unpadded = File.ReadAllBytes(Fixture("int16.fits"))[..(2880 + 12)];
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(unpadded), strict));
    }

    [Fact]
    public void Strict_mode_requires_blank_padding_after_END()
    {
        byte[] bytes = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 0).ToBytes();
        bytes[2000] = (byte)'X';
        Assert.Throws<FitsException>(() => FitsHeader.Read(new MemoryStream(bytes), new FitsReadOptions(true)));
        Assert.NotNull(FitsHeader.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void ReadRawData_returns_a_copy_for_in_memory_and_lazy_files()
    {
        var ms = new MemoryStream();
        PrimaryHeader().Write(ms);
        new FitsHeader().Set("XTENSION", "FOO").Set("BITPIX", 8).Set("NAXIS", 1).Set("NAXIS1", 10)
            .Set("PCOUNT", 0).Set("GCOUNT", 1).Write(ms);
        ms.Write("0123456789"u8);
        ms.Write(new byte[2880 - 10]);
        byte[] bytes = ms.ToArray();

        using var eager = FitsFile.Read(new MemoryStream(bytes));
        var custom = (GenericHdu)eager[1];
        custom.ReadRawData()[0] = 42;
        Assert.Equal((byte)'0', custom.ReadRawData()[0]);

        string path = Path.Combine(Path.GetTempPath(), "fitsnet-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            File.WriteAllBytes(path, bytes);
            using var lazy = FitsFile.Open(path);
            var t = (GenericHdu)lazy[1];
            t.ReadRawData()[0] = 42;
            Assert.Equal((byte)'0', t.ReadRawData()[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Random_groups_with_a_single_axis_hold_parameters_plus_one_element()
    {
        var h = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", -32).Set("NAXIS", 1).Set("NAXIS1", 0)
            .Set("GROUPS", true).Set("PCOUNT", 2).Set("GCOUNT", 3);
        using var f = FitsFile.Read(new MemoryStream(Concat(h.ToBytes(), new byte[2880])));
        Assert.Equal(3 * 3 * 4, f.Primary.DataLength);
        Assert.Equal(1, ((RandomGroupsHdu)f.Primary).ElementsPerGroup);
    }

    private static byte[] Concat(byte[] a, byte[] b) => [.. a, .. b];

    [Fact]
    public void Type_mismatch_throws_InvalidOperationException_consistently()
    {
        var img = ImageHdu.Create<short>([1], 1);
        Assert.Throws<InvalidOperationException>(() => img.ReadRawPixels<int>());
        Assert.Throws<InvalidOperationException>(() => img.ReadPixels<ulong>());
    }

    [Fact]
    public void Lazy_hdus_stay_readable_after_saving_over_the_opened_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            File.Copy(Fixture("multi.fits"), path);
            using var f = FitsFile.Open(path);
            f[1].Header.Set("LONGKEY", "x", "grows the header a bit");
            f.Save(path);
            Assert.Equal(1.0, ((ImageHdu)f[1]).ReadPixels<double>()[^1], 12);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, ((ImageHdu)f[2]).ReadPixels<int>());
            f.Save(path); // a second save keeps working
            using var reopened = FitsFile.Open(path, new FitsReadOptions(true));
            Assert.Equal(3, reopened.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Failed_overwrite_of_the_opened_file_keeps_it_usable()
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            File.Copy(Fixture("multi.fits"), path);
            using var f = FitsFile.Open(path);
            f[1].Header.Set("NAXIS1", 99); // contradicts the data
            Assert.Throws<FitsException>(() => f.Save(path));
            f[1].Header.Set("NAXIS1", 4);
            Assert.Equal(1.0, ((ImageHdu)f[1]).ReadPixels<double>()[^1], 12);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Hdu_from_another_lazy_file_survives_that_files_disposal()
    {
        FitsFile target = new();
        using (var other = FitsFile.Open(Fixture("multi.fits")))
        {
            var hdu = (ImageHdu)other[1];
            other.RemoveAt(1);
            target.Add(hdu);
        }
        Assert.Equal(1.0, ((ImageHdu)target[0]).ReadPixels<double>()[^1], 12);
    }

    [Fact]
    public void Disposed_file_rejects_edits_and_saves()
    {
        var f = FitsFile.Open(Fixture("int16.fits"));
        f.Dispose();
        Assert.Throws<ObjectDisposedException>(() => f.Save(new MemoryStream()));
        Assert.Throws<ObjectDisposedException>(() => f.Add(new ImageHdu()));
    }

    [Fact]
    public void Remove_by_reference_works_and_protects_the_primary_invariant()
    {
        var a = ImageHdu.Create<byte>([1], 1);
        var b = ImageHdu.Create<byte>([2], 1);
        var file = new FitsFile().Add(a).Add(b);
        Assert.True(file.Remove(b));
        Assert.False(file.Remove(b));
        Assert.Single(file);
    }
}
