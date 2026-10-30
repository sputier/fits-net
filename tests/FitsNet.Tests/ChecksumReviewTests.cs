namespace FitsNet.Tests;

/// <summary>Regression tests for defects found in the milestone 4 code review.</summary>
public class ChecksumReviewTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] AsciiTableFile(ChecksumPolicy policy = ChecksumPolicy.Always)
    {
        var file = new FitsFile()
            .Add(new ImageHdu())
            .Add(AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [1, 22, 333], 6)]));
        var ms = new MemoryStream();
        file.Save(ms, policy);
        return ms.ToArray();
    }

    private static ChecksumResult[] Verify(byte[] bytes)
    {
        using var f = FitsFile.Read(new MemoryStream(bytes));
        return f.VerifyChecksums().ToArray();
    }

    private static readonly ChecksumResult Good = new(ChecksumStatus.Valid, ChecksumStatus.Valid);

    [Fact]
    public void A_damaged_fill_byte_is_detected()
    {
        byte[] bytes = AsciiTableFile();
        Assert.All(Verify(bytes), r => Assert.Equal(Good, r));
        bytes[^1] = (byte)'A'; // the last byte of the file is fill
        var results = Verify(bytes);
        Assert.Equal(Good, results[0]);
        Assert.Equal(new ChecksumResult(ChecksumStatus.Invalid, ChecksumStatus.Invalid), results[1]);
    }

    [Fact]
    public void A_file_cut_inside_the_fill_is_reported_invalid()
    {
        byte[] bytes = AsciiTableFile();
        var results = Verify(bytes[..^100]);
        Assert.Equal(new ChecksumResult(ChecksumStatus.Invalid, ChecksumStatus.Invalid), results[1]);
    }

    [Fact]
    public void A_header_edited_in_memory_no_longer_verifies()
    {
        using var f = FitsFile.Read(new MemoryStream(AsciiTableFile()));
        Assert.Equal(Good, f[1].VerifyChecksum());
        f[1].Header.Set("FOO", 1);
        Assert.Equal(new ChecksumResult(ChecksumStatus.Invalid, ChecksumStatus.Valid), f[1].VerifyChecksum());
    }

    [Fact]
    public void Data_replaced_in_memory_no_longer_verifies()
    {
        using var f = FitsFile.Read(new MemoryStream(AsciiTableFile()));
        ((AsciiTableHdu)f[1]).AddColumn(TableColumn.CreateAsciiInteger("M", [1, 2, 3]));
        var r = f[1].VerifyChecksum();
        Assert.Equal(ChecksumStatus.Invalid, r.DataSum);
        Assert.Equal(ChecksumStatus.Invalid, r.Checksum);
    }

    [Fact]
    public void Auto_does_not_hide_corruption_but_Always_deliberately_refreshes()
    {
        using var corrupt = FitsFile.Open(Fixture("checksum_corrupt.fits"));
        var ms = new MemoryStream();
        corrupt.Save(ms, ChecksumPolicy.Auto);
        var afterAuto = Verify(ms.ToArray());
        Assert.Equal(new ChecksumResult(ChecksumStatus.Invalid, ChecksumStatus.Invalid), afterAuto[0]);
        Assert.Equal(Good, afterAuto[1]);

        using var again = FitsFile.Open(Fixture("checksum_corrupt.fits"));
        ms = new MemoryStream();
        again.Save(ms, ChecksumPolicy.Always);
        Assert.All(Verify(ms.ToArray()), r => Assert.Equal(Good, r));
    }

    [Fact]
    public void Auto_refreshes_an_edited_hdu_even_when_its_old_sums_were_wrong()
    {
        using var corrupt = FitsFile.Open(Fixture("checksum_corrupt.fits"));
        corrupt[0].Header.Set("TOUCHED", true);
        var ms = new MemoryStream();
        corrupt.Save(ms, ChecksumPolicy.Auto);
        Assert.Equal(Good, Verify(ms.ToArray())[0]);
    }

    private sealed class FailingStream(int allowed) : MemoryStream
    {
        private int _left = allowed;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > _left) throw new IOException("disk full");
            _left -= buffer.Length;
            base.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    }

    [Theory]
    [InlineData(ChecksumPolicy.Always)]
    [InlineData(ChecksumPolicy.Never)]
    public void A_failing_output_leaves_the_model_untouched(ChecksumPolicy policy)
    {
        using var f = FitsFile.Open(Fixture("checksum.fits"));
        var before = f.Select(h => h.Header.Select(c => c.ToString()).ToList()).ToList();
        Assert.Throws<IOException>(() => f.Save(new FailingStream(3000), policy));
        var after = f.Select(h => h.Header.Select(c => c.ToString()).ToList()).ToList();
        Assert.Equal(before, after);
        Assert.All(f.VerifyChecksums(), r => Assert.Equal(Good, r)); // the original state is still the reference
    }

    [Fact]
    public void A_successful_save_makes_the_saved_form_the_reference()
    {
        using var f = FitsFile.Open(Fixture("checksum.fits"));
        f.Save(new MemoryStream(), ChecksumPolicy.Auto);
        Assert.All(f.VerifyChecksums(), r => Assert.Equal(Good, r));
    }

    [Fact]
    public void Only_datasum_present_is_handled_by_auto()
    {
        var img = ImageHdu.Create<short>([1, 2, 3], 3);
        var ms = new MemoryStream();
        new FitsFile().Add(img).Save(ms, ChecksumPolicy.Always);
        using var back = FitsFile.Read(new MemoryStream(ms.ToArray()));
        back[0].Header.Remove("CHECKSUM");
        var again = new MemoryStream();
        back.Save(again, ChecksumPolicy.Auto);
        using var reread = FitsFile.Read(new MemoryStream(again.ToArray()));
        Assert.Equal(new ChecksumResult(ChecksumStatus.Valid, ChecksumStatus.Valid), reread[0].VerifyChecksum());
    }

    [Fact]
    public void Datasum_stored_as_integer_or_with_blanks_is_understood()
    {
        var img = ImageHdu.Create<short>([1, 2, 3], 3);
        var ms = new MemoryStream();
        new FitsFile().Add(img).Save(ms, ChecksumPolicy.Always);
        using var f = FitsFile.Read(new MemoryStream(ms.ToArray()));
        string text = f[0].Header.Get<string>("DATASUM")!;
        f[0].Header.Set("DATASUM", long.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(ChecksumStatus.Valid, f[0].VerifyChecksum().DataSum);
        f[0].Header.Set("DATASUM", "  " + text + "  ");
        Assert.Equal(ChecksumStatus.Valid, f[0].VerifyChecksum().DataSum);
        f[0].Header.Set("DATASUM", "garbage");
        Assert.Equal(ChecksumStatus.Invalid, f[0].VerifyChecksum().DataSum);
    }

    [Fact]
    public void Zero_length_data_with_always_has_datasum_zero()
    {
        var ms = new MemoryStream();
        new FitsFile().Add(new ImageHdu()).Save(ms, ChecksumPolicy.Always);
        using var f = FitsFile.Read(new MemoryStream(ms.ToArray()));
        Assert.Equal("0", f[0].Header.Get<string>("DATASUM"));
        Assert.Equal(Good, f[0].VerifyChecksum());
    }

    [Fact]
    public void Large_data_with_many_all_ones_words_does_not_overflow()
    {
        var data = new byte[20 * 1024 * 1024];
        Array.Fill(data, (byte)0xFF);
        using var s = new ChecksumStream();
        s.Write(data);
        Assert.Equal(0xFFFFFFFFu, s.Result);
        Assert.Equal(0xFFFFFFFFu, Checksum.Sum(data));
    }

    // ---- table-side review points -----------------------------------------------------------------------------------

    [Fact]
    public void An_empty_variable_length_table_with_a_stale_theap_can_be_read()
    {
        var t = BinaryTableHdu.Create([TableColumn.CreateVariable<int>("V", [[1], [2]])]);
        t.RemoveRows(0, 2);
        t.Header.Set("THEAP", 1_000_000);
        Assert.Empty(t.ReadVariableColumn<int>(0));
    }

    [Fact]
    public void Unsupported_or_lowercase_extra_keywords_are_rejected_up_front()
    {
        var c = TableColumn.Create<int>("A", [1]);
        c.Extras["tdmin"] = 1.0;
        Assert.Throws<ArgumentException>(() => BinaryTableHdu.Create([c]));
        c.Extras.Clear();
        c.Extras["TFOO"] = 1.0;
        Assert.Throws<ArgumentException>(() => BinaryTableHdu.Create([c]));
    }

    [Theory]
    [InlineData("I4 extra", 'I', 4)]
    [InlineData("E12.", 'E', 12)]
    [InlineData("F5.5", 'F', 5)]
    public void Ascii_tforms_with_trailing_text_or_missing_decimals_are_accepted(string tform, char code, int width)
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [1], 6)]);
        t.Header.Set("TFORM1", tform);
        t.Header.Set("NAXIS1", Math.Max(width, 6));
        Assert.Equal(code, tform[0]);
        _ = t.Columns; // parsing the metadata alone must not throw
        Assert.Equal(1, t.ColumnCount);
    }

    [Fact]
    public void Cloned_columns_do_not_share_the_dimensions_array()
    {
        var c = TableColumn.Create<int>("A", [1, 2, 3, 4], repeat: 4, dimensions: [2, 2]);
        var t = BinaryTableHdu.Create([c]);
        c.Dimensions![0] = 99;
        Assert.Equal(new long[] { 2, 2 }, t.Columns[0].Dimensions!);
    }
}
