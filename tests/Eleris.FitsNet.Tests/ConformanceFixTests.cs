using System.Numerics;
using System.Text;

namespace Eleris.FitsNet.Tests;

/// <summary>Regression tests for the conformance and robustness fixes made after the full review of milestones 1 to 5.</summary>
public class ConformanceFixTests
{
    private static byte[] HeaderBytes(params string[] cards)
    {
        var bytes = new byte[2880];
        Array.Fill(bytes, (byte)' ');
        int at = 0;
        foreach (string card in cards.Append("END"))
        {
            Encoding.Latin1.GetBytes(card, 0, card.Length, bytes, at);
            at += 80;
        }
        return bytes;
    }

    private static FitsHeader Read(byte[] bytes, bool strict) => FitsHeader.Read(new MemoryStream(bytes), new FitsReadOptions(strict));

    // ---- fixed format of the mandatory keywords ----------------------------------------------------------------------

    [Theory]
    [InlineData("BITPIX  =  16")]
    [InlineData("NAXIS   = 0 ")]
    [InlineData("EXTEND  =T")]
    public void Strict_mode_requires_fixed_format_for_mandatory_keywords(string badCard)
    {
        byte[] bytes = HeaderBytes("SIMPLE  =                    T", badCard);
        Assert.Throws<FitsException>(() => Read(bytes, strict: true));
        Assert.NotNull(Read(bytes, strict: false)); // tolerant mode accepts free format
    }

    [Fact]
    public void Strict_mode_requires_the_xtension_string_to_start_in_column_11()
    {
        byte[] bad = HeaderBytes("XTENSION=  'IMAGE   '");
        Assert.Throws<FitsException>(() => Read(bad, strict: true));
        Assert.Equal("IMAGE", Read(bad, strict: false).Get<string>("XTENSION"));
        Assert.Equal("IMAGE", Read(HeaderBytes("XTENSION= 'IMAGE   '"), strict: true).Get<string>("XTENSION"));
    }

    [Fact]
    public void Free_format_is_accepted_for_other_keywords_even_in_strict_mode()
    {
        var h = Read(HeaderBytes("SIMPLE  =                    T", "EXPTIME = 5", "OBJECT  = 'M31'", "FLAG    = T"), strict: true);
        Assert.Equal(5L, h.Get<long>("EXPTIME"));
        Assert.Equal("M31", h.Get<string>("OBJECT"));
        Assert.True(h.Get<bool>("FLAG"));
    }

    // ---- tolerant mode keeps every byte --------------------------------------------------------------------------------

    [Fact]
    public void Tolerant_reading_keeps_non_ascii_bytes_so_a_round_trip_loses_nothing()
    {
        byte[] original = HeaderBytes("SIMPLE  =                    T", "COMMENT café °C", "OBJECT  = 'Méssier1'");
        var h = Read(original, strict: false);
        Assert.Equal(original, h.ToBytes());
        Assert.Throws<FitsException>(() => Read(original, strict: true));
    }

    // ---- long strings --------------------------------------------------------------------------------------------------

    [Fact]
    public void Comments_spread_over_the_continue_cards_of_a_long_string_are_joined()
    {
        var h = Read(HeaderBytes("SIMPLE  =                    T", "LONG    = 'abc&'                / first part", "CONTINUE  'def&'           / second part", "CONTINUE  'ghi'"), strict: true);
        Assert.Equal("abcdefghi", h.Get<string>("LONG"));
        Assert.Equal("first part second part", h.Find("LONG")!.Comment);
    }

    [Fact]
    public void The_long_string_convention_card_is_not_duplicated_and_only_added_when_needed()
    {
        var h = new FitsHeader().Set("LONGSTRN", "OGIP 1.0").Set("TEXT", new string('x', 300));
        string text = Encoding.ASCII.GetString(h.ToBytes());
        Assert.Equal(1, text.Split("LONGSTRN").Length - 1);
        string plain = Encoding.ASCII.GetString(new FitsHeader().Set("TEXT", "short").ToBytes());
        Assert.DoesNotContain("LONGSTRN", plain);
    }

    // ---- HIERARCH is explicit -------------------------------------------------------------------------------------------

    [Fact]
    public void Non_standard_keywords_need_the_explicit_hierarch_api()
    {
        var ex = Assert.Throws<ArgumentException>(() => new FitsCard("VERY_LONG_KEYWORD", 1));
        Assert.Contains("Hierarch", ex.Message);
        Assert.Throws<ArgumentException>(() => new FitsCard("A.B", 1));
        var card = FitsCard.Hierarch("ESO TEL ALT", 45.5, "altitude");
        Assert.True(card.IsHierarch);
        var h = new FitsHeader().Add(card);
        Assert.Equal(45.5, new FitsHeader().SetHierarch("ESO TEL ALT", 45.5).Get<double>("ESO TEL ALT"));
        Assert.Equal("altitude", h.Find("ESO TEL ALT")!.Comment);
        Assert.Throws<ArgumentException>(() => FitsCard.Hierarch("bad = key", 1));
    }

    // ---- tables ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Scaled_complex_columns_apply_tscal_and_tzero_to_both_parts()
    {
        var column = TableColumn.CreateComplex("Z", [new Complex(1, 2), new Complex(-3, 4)]);
        column.Scale = 2;
        column.Zero = 1;
        var t = BinaryTableHdu.Create([column]);
        Assert.Equal(new[] { new Complex(3, 5), new Complex(-5, 9) }, t.ReadColumn<Complex>("Z"));
    }

    [Fact]
    public void Ascii_fields_with_an_implied_decimal_point_are_read_but_never_written()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [0, 0], "F8.3")]);
        t.Source = new MemoryDataSource(Encoding.ASCII.GetBytes("12345678" + "-1234567"));
        Assert.Equal([12345.678, -1234.567], t.ReadColumn<double>(0)); // the standard reads these as 12345.678 and -1234.567

        // Writing them back without a point would be read as 12345678 by software that ignores implied decimals (astropy):
        // the value is refused with a clear message instead of being written ambiguously.
        var ex = Assert.Throws<FitsException>(() => t.AddColumn(TableColumn.CreateAsciiInteger("N", [1, 2])));
        Assert.Contains("wider", ex.Message);
        // a wider format keeps the value
        var wide = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [12345.678, -1234.567], "F9.3")]);
        Assert.Equal([12345.678, -1234.567], wide.ReadColumn<double>(0));
        Assert.Contains("12345.678", Encoding.ASCII.GetString(wide.Source.ReadAll()));
    }

    [Fact]
    public void Ascii_values_without_room_for_the_leading_zero_drop_it()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [0.5, 0.25], "F4.3")]);
        Assert.Equal([0.5, 0.25], t.ReadColumn<double>(0));
        Assert.Contains(".500", Encoding.ASCII.GetString(t.Source.ReadAll()));
        Assert.Throws<FitsException>(() => AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [-0.25], "F4.3")])); // "-.250" needs 5
    }

    // ---- random groups ------------------------------------------------------------------------------------------------

    [Fact]
    public void Random_groups_support_the_unsigned_conventions_for_data_and_parameters()
    {
        var g = RandomGroupsHdu.Create<ushort>([7, 65535, 3, 40000, 65535, 0], groupCount: 2, parameterCount: 1, 2);
        Assert.Equal(32768L, g.Header.Get<long>("BZERO"));
        Assert.Equal(32768L, g.Header.Get<long>("PZERO1"));
        Assert.Equal([7.0], g.ReadParameters(0));
        Assert.Equal([65535.0, 3.0], g.ReadData(0));
        Assert.Equal([40000.0], g.ReadParameters(1));
        Assert.Equal([65535.0, 0.0], g.ReadData(1));
        var ms = new MemoryStream();
        new FitsFile().Add(g).Save(ms);
        ms.Position = 0;
        using var back = FitsFile.Read(ms, new FitsReadOptions(true));
        Assert.Equal([65535.0, 0.0], ((RandomGroupsHdu)back.Primary).ReadData(1));
    }

    [Fact]
    public void Random_group_parameters_with_the_same_name_are_added_together()
    {
        var g = RandomGroupsHdu.Create<float>([1.5f, 2.5f, 10f, 0f, 0f], groupCount: 1, parameterCount: 3, 2);
        g.Header.Set("PTYPE1", "DATE").Set("PTYPE2", "date").Set("PTYPE3", "BASELINE");
        var named = g.ReadNamedParameters(0);
        Assert.Equal(4.0, named["DATE"]);
        Assert.Equal(10.0, named["BASELINE"]);
        Assert.Equal(2, named.Count);
        var unnamed = RandomGroupsHdu.Create<float>([1f, 2f, 0f, 0f], 1, 2, 2).ReadNamedParameters(0);
        Assert.Equal(1.0, unnamed["PARAM1"]);
    }

    // ---- memory ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_read_into_memory_shares_one_buffer_and_still_gives_private_copies()
    {
        var file = new FitsFile()
            .Add(ImageHdu.Create<short>([1, 2, 3, 4], 2, 2))
            .Add(BinaryTableHdu.Create([TableColumn.Create<int>("A", [10, 20, 30])]))
            .Add(new GenericHdu(new FitsHeader().Set("XTENSION", "FOO").Set("BITPIX", 8).Set("NAXIS", 1).Set("NAXIS1", 4).Set("PCOUNT", 0).Set("GCOUNT", 1),
                new MemoryDataSource([9, 8, 7, 6])));
        var ms = new MemoryStream();
        file.Save(ms);
        ms.Position = 0;
        using var back = FitsFile.Read(ms, new FitsReadOptions(true));
        Assert.All(back, h => Assert.IsType<SegmentDataSource>(h.Source));
        Assert.Equal(new short[] { 1, 2, 3, 4 }, ((ImageHdu)back[0]).ReadPixels<short>());
        Assert.Equal(new[] { 10, 20, 30 }, ((BinaryTableHdu)back[1]).ReadColumn<int>("A"));

        var raw = ((GenericHdu)back[2]).ReadRawData();
        raw[0] = 0;
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, ((GenericHdu)back[2]).ReadRawData());

        // saving a file read this way writes the same bytes again
        var again = new MemoryStream();
        back.Save(again, ChecksumPolicy.Never);
        Assert.Equal(ms.ToArray(), again.ToArray());
    }

    [Fact]
    public void Reading_a_non_seekable_stream_works()
    {
        var ms = new MemoryStream();
        new FitsFile().Add(ImageHdu.Create<byte>([1, 2, 3], 3)).Save(ms);
        using var back = FitsFile.Read(new NonSeekable(ms.ToArray()));
        Assert.Equal(new byte[] { 1, 2, 3 }, ((ImageHdu)back[0]).ReadPixels<byte>());
    }

    private sealed class NonSeekable(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    // ---- saving ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Saving_through_a_symbolic_link_updates_the_target()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fitsnet-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string target = Path.Combine(dir, "target.fits"), link = Path.Combine(dir, "link.fits");
            new FitsFile().Add(ImageHdu.Create<byte>([1, 2], 2)).Save(target);
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return; // symbolic links need a privilege on Windows: nothing to check here
            }
            new FitsFile().Add(ImageHdu.Create<byte>([7, 8, 9], 3)).Save(link);
            Assert.NotNull(new FileInfo(link).LinkTarget); // still a link
            using var f = FitsFile.Open(target);
            Assert.Equal(new byte[] { 7, 8, 9 }, ((ImageHdu)f.Primary).ReadPixels<byte>());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Overwriting_a_file_replaces_it_completely_and_leaves_no_temporary_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fitsnet-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "a.fits");
            new FitsFile().Add(ImageHdu.Create<byte>(new byte[10000], 10000)).Save(path);
            new FitsFile().Add(ImageHdu.Create<byte>([5], 1)).Save(path);
            using (var f = FitsFile.Open(path)) Assert.Equal(new byte[] { 5 }, ((ImageHdu)f.Primary).ReadPixels<byte>());
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
