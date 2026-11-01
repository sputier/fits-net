using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>Regression tests for every defect of the full review of the library (fidelity of read/save, hostile input, wide tables).</summary>
public class FinalReviewTests
{
    private static byte[] HeaderBytes(params string[] cards)
    {
        int blocks = (cards.Length + 1 + 35) / 36;
        var bytes = new byte[blocks * 2880];
        Array.Fill(bytes, (byte)' ');
        int at = 0;
        foreach (string card in cards.Append("END"))
        {
            Encoding.Latin1.GetBytes(card, 0, card.Length, bytes, at);
            at += 80;
        }
        return bytes;
    }

    private static byte[] Save(FitsFile file, ChecksumPolicy policy = ChecksumPolicy.Never)
    {
        var ms = new MemoryStream();
        file.Save(ms, policy);
        return ms.ToArray();
    }

    // ---- 1. a file that is read and saved again keeps its bytes -------------------------------------------------------

    [Fact]
    public void Cards_with_long_comments_in_any_layout_are_written_back_unchanged()
    {
        string comment55 = new string('c', 55), comment60 = new string('d', 60);
        byte[] original = HeaderBytes(
            "SIMPLE  =                    T",
            "BITPIX  =                    8",
            "NAXIS   =                    0",
            "OBJECT  = 'M31     ' / " + comment55,
            "EXPTIME = 30.0 / " + comment60,
            "NOTE    = 'x'   /" + new string('e', 60));
        using var file = FitsFile.Read(new MemoryStream([.. original, .. new byte[0]]));
        byte[] saved = Save(file);
        Assert.Equal(original, saved);
        Assert.Equal(comment55, file.Primary.Header.Find("OBJECT")!.Comment);
    }

    [Fact]
    public void Non_ascii_comments_of_mandatory_cards_do_not_prevent_saving()
    {
        byte[] original = HeaderBytes(
            "SIMPLE  =                    T / conforme à la norme",
            "BITPIX  =                    8 / bits per pixel éè",
            "NAXIS   =                    0 / axes\tTAB",
            "EXTEND  =                    T");
        using var file = FitsFile.Read(new MemoryStream(original));
        Assert.Equal(original, Save(file));
    }

    [Fact]
    public void Non_ascii_text_read_from_a_file_survives_table_edits()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2, 3]), TableColumn.Create<int>("B", [4, 5, 6])]);
        t.Header.Add(FitsCard.Raw("TUNIT1", "µm", null, FitsCardKind.Value)); // as read from a file
        t.Header.Add(FitsCard.Raw("OBSERVER", "Jérôme", null, FitsCardKind.Value));
        t.RemoveRows(0, 1);
        t.AddColumn(TableColumn.Create<short>("C", [7, 8]));
        t.AppendRows([TableColumn.Create<int>("A", [9]), TableColumn.Create<int>("B", [9]), TableColumn.Create<short>("C", [9])]);
        Assert.Equal("µm", t.Columns[0].Unit);
        Assert.Equal("Jérôme", t.Header.Get<string>("OBSERVER"));
        // text written explicitly through the public API must still be ASCII, as the standard requires
        Assert.Throws<ArgumentException>(() => t.Header.Set("OBSERVER", "Jérôme"));
    }

    [Fact]
    public void A_lowercase_keyword_stays_a_plain_keyword_and_hierarch_is_only_for_hierarch()
    {
        byte[] original = HeaderBytes("SIMPLE  =                    T", "BITPIX  =                    8", "NAXIS   =                    0", "object  = 'M31'");
        using var file = FitsFile.Read(new MemoryStream(original));
        var card = file.Primary.Header.Find("object")!;
        Assert.False(card.IsHierarch);
        Assert.Equal(original, Save(file));

        Assert.True(FitsCard.Hierarch("OBJECT", 1).IsHierarch);
        string text = Encoding.ASCII.GetString(new FitsHeader().Add(FitsCard.Hierarch("OBJECT", 1)).ToBytes());
        Assert.StartsWith("HIERARCH OBJECT = ", text);
        Assert.False(new FitsCard("OBJECT", 1).IsHierarch);
    }

    [Fact]
    public void The_extend_card_of_a_single_hdu_file_is_kept()
    {
        using var file = FitsFile.Open(TestData.Vespera("img-0022-M42-broadband.fits"));
        var ms = new MemoryStream();
        file.Save(ms);
        using var back = FitsFile.Read(ms.ToArray().AsSpan().ToArray().Length > 0 ? new MemoryStream(ms.ToArray()) : ms);
        Assert.True(back.Primary.Header.Get<bool>("EXTEND"));
        Assert.Equal(Encoding.Latin1.GetString(File.ReadAllBytes(TestData.Vespera("img-0022-M42-broadband.fits")), 0, 2880),
            Encoding.Latin1.GetString(ms.ToArray(), 0, 2880)); // the whole header block is identical
    }

    // ---- 2. hostile and damaged input fails with FitsException --------------------------------------------------------

    private static FitsFile TableFile(Action<FitsHeader> tweak, int dataBytes)
    {
        var ms = new MemoryStream();
        new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 0).Set("EXTEND", true).Write(ms);
        var h = new FitsHeader().Set("XTENSION", "BINTABLE").Set("BITPIX", 8).Set("NAXIS", 2).Set("NAXIS1", 4).Set("NAXIS2", 1)
            .Set("PCOUNT", 0).Set("GCOUNT", 1);
        tweak(h);
        h.Write(ms);
        ms.Write(new byte[2880]);
        return FitsFile.Read(new MemoryStream(ms.ToArray()));
    }

    [Fact]
    public void Column_offsets_that_overflow_are_reported_cleanly()
    {
        using var f = TableFile(h => h.Set("TFIELDS", 4)
            .Set("TFORM1", "9223372036854775807B").Set("TFORM2", "9223372036854775807B").Set("TFORM3", "9223372036854775807B").Set("TFORM4", "8B"), 4);
        var t = (BinaryTableHdu)f[1];
        Assert.Throws<FitsException>(() => t.ReadColumn<byte>(3));
        Assert.Throws<FitsException>(() => t.ReadRaw(0));
        Assert.Throws<FitsException>(() => t.ReadColumns());
    }

    [Fact]
    public void A_column_wider_than_the_row_is_rejected_whatever_its_offset()
    {
        using var f = TableFile(h => h.Set("TFIELDS", 2).Set("TFORM1", "3B").Set("TFORM2", "1J"), 4);
        Assert.Throws<FitsException>(() => ((BinaryTableHdu)f[1]).ReadColumn<int>(1));
    }

    [Fact]
    public void Random_group_parameters_check_the_group_before_the_sizes()
    {
        var ms = new MemoryStream();
        new FitsHeader().Set("SIMPLE", true).Set("BITPIX", -32).Set("NAXIS", 1).Set("NAXIS1", 0).Set("GROUPS", true)
            .Set("PCOUNT", 3_000_000_000L).Set("GCOUNT", 0).Write(ms);
        using var f = FitsFile.Read(new MemoryStream(ms.ToArray()));
        var g = Assert.IsType<RandomGroupsHdu>(f.Primary);
        Assert.Throws<ArgumentOutOfRangeException>(() => g.ReadParameters(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.ReadNamedParameters(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.ReadData(0));
    }

    [Theory]
    [InlineData("COMPRESSED_DATA", "0B")]
    [InlineData("COMPRESSED_DATA", "8B")]
    [InlineData("COMPRESSED_DATA", "1PA(5)")]
    [InlineData("ZSCALE", "0D")]
    [InlineData("ZSCALE", "2D")]
    [InlineData("ZSCALE", "1A")]
    [InlineData("ZZERO", "1X")]
    [InlineData("ZSCALE", "1PD(3)")]
    public void Compressed_images_with_misshapen_special_columns_fail_with_FitsException(string column, string tform)
    {
        var pixels = Enumerable.Range(0, 64).Select(i => (float)(100 + Math.Sin(i * 1.3) * 5)).ToArray();
        var comp = CompressedImageHdu.Create<float>(pixels, [8, 8], new CompressionOptions { TileShape = [8, 2] });
        int index = comp.IndexOf(column);
        Assert.True(index >= 0);
        comp.Header.Set("TFORM" + (index + 1), tform);
        Assert.IsType<FitsException>(Record.Exception(() => comp.Decompress()));
    }

    [Fact]
    public void A_position_past_the_end_of_the_stream_is_not_a_crash()
    {
        var ms = new MemoryStream(new byte[10]) { Position = 100 };
        Assert.Throws<FitsException>(() => FitsFile.Read(ms));
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream()));
    }

    [Fact]
    public void Aliased_heap_descriptors_cannot_multiply_the_memory_of_bit_arrays()
    {
        const int rows = 60, bits = 8_000_000;
        var data = new bool[rows][];
        Array.Fill(data, Array.Empty<bool>());
        data[0] = new bool[bits];
        var t = BinaryTableHdu.Create([new TableColumn("V", "1PX(" + bits + ")") { Data = data }]);
        byte[] raw = t.Source.ReadAll();
        for (int r = 0; r < rows; r++)
        {
            BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(r * 8), bits);
            BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(r * 8 + 4), 0);
        }
        t.Source = new MemoryDataSource(raw);
        Assert.Throws<FitsException>(() => t.ReadVariableBitColumn(0)); // 60 rows x 8M bools would be 480 MB
    }

    [Fact]
    public void Fixed_bit_columns_larger_than_an_array_are_rejected_before_allocating()
    {
        Assert.Throws<FitsException>(() => BinaryTableCodec.AllocFixed(BinaryFormat.Parse("1000000000X"), null, 5));
        Assert.Throws<FitsException>(() => BinaryTableCodec.AllocFixed(BinaryFormat.Parse("2000000000B"), null, long.MaxValue / 3));
    }

    // ---- 3. wide tables ----------------------------------------------------------------------------------------------

    [Fact]
    public void Reading_the_columns_of_a_wide_table_one_by_one_is_not_quadratic()
    {
        const int columns = 600;
        var t = BinaryTableHdu.Create(Enumerable.Range(0, columns).Select(i => TableColumn.Create<int>("C" + i, [i, i + 1])).ToList());
        for (int i = 0; i < 3000; i++) t.Header.Add(FitsCard.CommentCard("filler " + i)); // a big header makes every lookup costly
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < columns; i++) Assert.Equal(new[] { i, i + 1 }, t.ReadColumn<int>(i));
        for (int i = 0; i < columns; i += 7) Assert.Equal(i, t.IndexOf("c" + i));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}"); // 107 s before the columns were cached
    }

    [Fact]
    public void The_cached_columns_follow_every_header_change()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1]), TableColumn.Create<int>("B", [2])]);
        Assert.Equal(1, t.IndexOf("B"));
        t.Header.Set("TTYPE2", "RENAMED");
        Assert.Equal(-1, t.IndexOf("B"));
        Assert.Equal(1, t.IndexOf("renamed"));
        t.Header.Set("TUNIT1", "m");
        Assert.Equal("m", t.Columns[0].Unit);
        t.Columns[0].Unit = "changed on a copy";
        Assert.Equal("m", t.Columns[0].Unit); // the cache is not exposed
        t.AddColumn(TableColumn.Create<int>("C", [3]));
        Assert.Equal(2, t.IndexOf("c"));
    }

    // ---- 4/5/6 arguments and values ------------------------------------------------------------------------------------

    [Fact]
    public void Column_data_of_the_wrong_type_or_with_nulls_is_rejected_with_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => BinaryTableHdu.Create([new TableColumn("A", "1J") { Data = new double[] { 1 } }]));
        Assert.Throws<ArgumentException>(() => BinaryTableHdu.Create([new TableColumn("A", "4A") { Data = new string?[] { "ok", null } }]));
        Assert.Throws<ArgumentException>(() => BinaryTableHdu.Create([new TableColumn("A", "1PJ(2)") { Data = new int[]?[] { [1], null } }]));
        Assert.Throws<ArgumentException>(() => BinaryTableHdu.Create([new TableColumn("A", "1PJ(2)") { Data = new long[][] { [1] } }]));
        Assert.Throws<ArgumentException>(() => AsciiTableHdu.Create([new TableColumn("A", "I5") { Data = new double[] { 1 } }]));
        Assert.Throws<ArgumentException>(() => AsciiTableHdu.Create([new TableColumn("A", "A5") { Data = new string?[] { null } }]));
    }

    [Fact]
    public void Oversized_images_and_groups_are_ArgumentException_not_OverflowException()
    {
        Assert.Throws<ArgumentException>(() => ImageHdu.Create<byte>([], long.MaxValue / 2, 4));
        Assert.Throws<ArgumentException>(() => RandomGroupsHdu.Create<float>([], long.MaxValue / 2, 4, 2));
        Assert.Throws<ArgumentException>(() => RandomGroupsHdu.Create<float>([], 1, 1000));
        Assert.Throws<ArgumentException>(() => RandomGroupsHdu.Create<ushort>([], 1, 5, long.MaxValue / 2, 4));
    }

    [Theory]
    [InlineData("       -")]
    [InlineData("      E5")]
    [InlineData("    1.5E")]
    [InlineData("     +-   ")]
    public void Ascii_fields_without_digits_are_rejected(string field)
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [0], "F8.2")]);
        t.Source = new MemoryDataSource(Encoding.ASCII.GetBytes(field.PadLeft(8)[..8]));
        Assert.Throws<FitsException>(() => t.ReadColumn<double>(0));
    }

    // ---- 7. compressed tables are not pretended to be tables ------------------------------------------------------------

    [Fact]
    public void A_compressed_table_is_not_exposed_as_rows_and_is_kept_verbatim()
    {
        var t = BinaryTableHdu.Create([TableColumn.CreateVariable<byte>("COMPRESSED_DATA", [[1, 2, 3]])]);
        t.Header.Set("ZTABLE", true).Set("ZTILELEN", 1);
        byte[] bytes = Save(new FitsFile().Add(t));
        using var back = FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(true));
        Assert.IsType<CompressedTableHdu>(back[1]);
        Assert.Equal(bytes, Save(back));
    }
}
