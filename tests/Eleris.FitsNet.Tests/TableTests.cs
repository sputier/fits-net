using System.Numerics;
using System.Text;

namespace Eleris.FitsNet.Tests;

public class TableTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static FitsFile RoundTrip(FitsFile file)
    {
        using var ms = new MemoryStream();
        file.Save(ms);
        ms.Position = 0;
        return FitsFile.Read(ms, new FitsReadOptions(Strict: true));
    }

    private static BinaryTableHdu RoundTrip(BinaryTableHdu table) =>
        (BinaryTableHdu)RoundTrip(new FitsFile().Add(table))[1];

    // ---- Binary tables written by astropy ------------------------------------------------------------------------

    [Fact]
    public void Reads_astropy_binary_table_structure()
    {
        using var f = FitsFile.Open(Fixture("bintable.fits"), new FitsReadOptions(true));
        var t = Assert.IsType<BinaryTableHdu>(f[1]);
        Assert.Equal(3, t.RowCount);
        Assert.Equal(123, t.RowWidth);
        Assert.Equal(16, t.ColumnCount);
        Assert.Equal("EVENTS", t.Header.Get<string>("EXTNAME"));
        Assert.Equal("count", t.Columns[0].Unit);
        Assert.Equal("ID", t.Columns[0].Name);
        Assert.Equal(1, t.IndexOf("flag"));
        Assert.Equal(-1, t.IndexOf("nope"));
        Assert.Equal(new long[] { 3, 2 }, t.Columns[11].Dimensions!);
        Assert.Equal(-99L, t.Columns[10].Null);
        Assert.Equal("TFIELDS", t.Header[7].Keyword);
    }

    [Fact]
    public void Reads_astropy_binary_table_values()
    {
        using var f = FitsFile.Open(Fixture("bintable.fits"));
        var t = (BinaryTableHdu)f[1];

        Assert.Equal(new[] { 1, 2, 3 }, t.ReadColumn<int>("ID"));
        Assert.Equal(new bool?[] { true, false, true }, t.ReadLogicalColumn(1));
        bool[] bits = t.ReadBitColumn(2);
        Assert.Equal(24, bits.Length);
        Assert.Equal([0, 7, 11, 16, 17, 18, 19, 20, 21, 22, 23], Enumerable.Range(0, 24).Where(i => bits[i]));
        Assert.Equal(new[] { "abc", "de", "fghij" }, t.ReadStringColumn("NAME"));
        Assert.Equal(Enumerable.Range(0, 9).Select(i => (float)i), t.ReadColumn<float>("FLUX"));
        Assert.Equal(new ushort[] { 0, 1, 65535 }, t.ReadColumn<ushort>("U16"));
        Assert.Equal(new ulong[] { 0, 1UL << 63, ulong.MaxValue }, t.ReadColumn<ulong>("U64"));
        Assert.Equal(new[] { 1.5, 2.5, 3.5 }, t.ReadColumn<double>("DBL"));
        Assert.Equal(new[] { new Complex(1, 2), new Complex(3, -4), Complex.Zero }, t.ReadColumn<Complex>("CPLX"));
        Assert.Equal(new[] { new Complex(1, 2), new Complex(3, -4), Complex.Zero }, t.ReadColumn<Complex>("CPLXS"));
        Assert.Equal(Enumerable.Range(0, 18), t.ReadColumn<int>("DIM"));
        Assert.Equal(new short[] { 1, -2, 3, 4, -5, 6 }, t.ReadColumn<short>("SHORTS"));
        Assert.Equal(new byte[] { 1, 2, 3, 255, 0, 7 }, t.ReadColumn<byte>("BYTES"));
        Assert.Equal(new[] { new[] { 1, 2, 3 }, new[] { 4 }, Array.Empty<int>() }, t.ReadVariableColumn<int>("VARJ"));
        Assert.Equal(new[] { new[] { 0.5 }, Array.Empty<double>(), new[] { 1.5, 2.5 } }, t.ReadVariableColumn<double>("VARQ"));
    }

    [Fact]
    public void Tnull_becomes_nan_when_reading_as_double_but_stays_raw_otherwise()
    {
        using var f = FitsFile.Open(Fixture("bintable.fits"));
        var t = (BinaryTableHdu)f[1];
        Assert.Equal([1.0, double.NaN, 3.0], t.ReadColumn<double>("NUL"));
        Assert.Equal(new[] { 1, -99, 3 }, t.ReadColumn<int>("NUL"));
    }

    [Fact]
    public void Wrong_type_requests_are_rejected_with_clear_exceptions()
    {
        using var f = FitsFile.Open(Fixture("bintable.fits"));
        var t = (BinaryTableHdu)f[1];
        Assert.Throws<InvalidOperationException>(() => t.ReadColumn<long>("ID"));
        Assert.Throws<InvalidOperationException>(() => t.ReadColumn<int>("FLAG"));
        Assert.Throws<InvalidOperationException>(() => t.ReadColumn<int>("VARJ"));
        Assert.Throws<InvalidOperationException>(() => t.ReadVariableColumn<int>("ID"));
        Assert.Throws<InvalidOperationException>(() => t.ReadStringColumn(0));
        Assert.Throws<InvalidOperationException>(() => t.ReadColumn<double>("CPLX"));
        Assert.Throws<KeyNotFoundException>(() => t.ReadColumn<int>("MISSING"));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.ReadRaw(99));
    }

    [Fact]
    public void Astropy_binary_table_survives_a_full_read_edit_free_roundtrip()
    {
        using var original = FitsFile.Open(Fixture("bintable.fits"));
        using var copy = RoundTrip(original);
        var a = ((BinaryTableHdu)original[1]).ReadColumns();
        var b = ((BinaryTableHdu)copy[1]).ReadColumns();
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Name, b[i].Name);
            Assert.Equal(a[i].Format, b[i].Format);
            Assert.Equal(a[i].Zero, b[i].Zero);
            Assert.Equal(a[i].Dimensions, b[i].Dimensions);
            Assert.Equal(a[i].Data, b[i].Data);
        }
        Assert.Equal(original[1].Source.ReadAll(), copy[1].Source.ReadAll());
    }

    [Fact]
    public void Rebuilding_the_table_from_its_columns_keeps_other_header_cards()
    {
        using var f = FitsFile.Open(Fixture("bintable.fits"));
        var t = (BinaryTableHdu)f[1];
        t.SetColumns(t.ReadColumns());
        Assert.Equal("Fits Net", t.Header.Get<string>("OBSERVER"));
        Assert.Equal("EVENTS", t.Header.Get<string>("EXTNAME"));
        Assert.Equal(new ushort[] { 0, 1, 65535 }, t.ReadColumn<ushort>("U16"));
        Assert.Equal(40, t.Header.Get<long>("PCOUNT")); // heap size is regenerated from the data
        Assert.Single(t.Header, c => c.Keyword == "TFORM1");
    }

    // ---- Creating binary tables ----------------------------------------------------------------------------------

    [Fact]
    public void Creates_and_roundtrips_every_numeric_type()
    {
        var t = BinaryTableHdu.Create(
        [
            TableColumn.Create<byte>("B", [0, 255]),
            TableColumn.Create<sbyte>("SB", [-128, 127]),
            TableColumn.Create<short>("I", [short.MinValue, short.MaxValue]),
            TableColumn.Create<ushort>("UI", [0, 65535]),
            TableColumn.Create<int>("J", [int.MinValue, int.MaxValue]),
            TableColumn.Create<uint>("UJ", [0, uint.MaxValue]),
            TableColumn.Create<long>("K", [long.MinValue, long.MaxValue]),
            TableColumn.Create<ulong>("UK", [0, ulong.MaxValue]),
            TableColumn.Create<float>("E", [float.MinValue, 1.5f]),
            TableColumn.Create<double>("D", [double.Epsilon, double.MaxValue]),
            TableColumn.CreateComplex("M", [new Complex(1, 2), new Complex(-3, 4)]),
            TableColumn.CreateComplex("C", [new Complex(0.5, 2), new Complex(-3, 4)], singlePrecision: true),
        ]);
        var r = RoundTrip(t);
        Assert.Equal(2, r.RowCount);
        Assert.Equal(new byte[] { 0, 255 }, r.ReadColumn<byte>("B"));
        Assert.Equal(new sbyte[] { -128, 127 }, r.ReadColumn<sbyte>("SB"));
        Assert.Equal(new short[] { short.MinValue, short.MaxValue }, r.ReadColumn<short>("I"));
        Assert.Equal(new ushort[] { 0, 65535 }, r.ReadColumn<ushort>("UI"));
        Assert.Equal(new[] { int.MinValue, int.MaxValue }, r.ReadColumn<int>("J"));
        Assert.Equal(new[] { 0u, uint.MaxValue }, r.ReadColumn<uint>("UJ"));
        Assert.Equal(new[] { long.MinValue, long.MaxValue }, r.ReadColumn<long>("K"));
        Assert.Equal(new[] { 0UL, ulong.MaxValue }, r.ReadColumn<ulong>("UK"));
        Assert.Equal(new[] { float.MinValue, 1.5f }, r.ReadColumn<float>("E"));
        Assert.Equal(new[] { double.Epsilon, double.MaxValue }, r.ReadColumn<double>("D"));
        Assert.Equal(new[] { new Complex(1, 2), new Complex(-3, 4) }, r.ReadColumn<Complex>("M"));
        Assert.Equal(new[] { new Complex(0.5, 2), new Complex(-3, 4) }, r.ReadColumn<Complex>("C"));
        Assert.Equal(12, r.ColumnCount);
    }

    [Fact]
    public void Creates_logical_bit_and_string_columns()
    {
        var t = BinaryTableHdu.Create(
        [
            TableColumn.CreateLogical("L", [true, null, false]),
            TableColumn.CreateBits("X", [.. Enumerable.Range(0, 36).Select(i => i % 3 == 0)], bitsPerRow: 12),
            TableColumn.CreateString("S", ["a", "", "abcd"], width: 4),
            TableColumn.CreateString("S2", ["ab", "cd", "e", "", "fgh", "ij"], width: 3, stringsPerRow: 2),
        ]);
        var r = RoundTrip(t);
        Assert.Equal(new bool?[] { true, null, false }, r.ReadLogicalColumn(0));
        Assert.Equal(Enumerable.Range(0, 36).Select(i => i % 3 == 0), r.ReadBitColumn(1));
        Assert.Equal(new[] { "a", "", "abcd" }, r.ReadStringColumn("S"));
        Assert.Equal(new[] { "ab", "cd", "e", "", "fgh", "ij" }, r.ReadStringColumn("S2"));
        Assert.Equal("6A3", r.Columns[3].Format);
        Assert.Equal(2, r.Columns[1].BinaryFormat.FieldBytes);
    }

    [Fact]
    public void Creates_variable_length_columns_with_both_descriptor_sizes()
    {
        var t = BinaryTableHdu.Create(
        [
            TableColumn.CreateVariable<short>("P", [[1, 2], [], [3]]),
            TableColumn.CreateVariable<double>("Q", [[1.5], [2.5, 3.5], []], use64BitDescriptors: true),
            TableColumn.CreateVariable<ushort>("U", [[65535], [0, 1], []]),
            TableColumn.CreateVariableString("STR", ["hello", "", "x y"]),
        ]);
        Assert.Equal("1PI(2)", t.Columns[0].Format);
        Assert.Equal("1QD(2)", t.Columns[1].Format);
        var r = RoundTrip(t);
        Assert.Equal(new[] { new short[] { 1, 2 }, [], [3] }, r.ReadVariableColumn<short>("P"));
        Assert.Equal(new[] { new[] { 1.5 }, [2.5, 3.5], Array.Empty<double>() }, r.ReadVariableColumn<double>("Q"));
        Assert.Equal(new[] { new ushort[] { 65535 }, [0, 1], [] }, r.ReadVariableColumn<ushort>("U"));
        Assert.Equal(new[] { "hello", "", "x y" }, r.ReadStringColumn("STR"));
        Assert.True(r.Header.Get<long>("PCOUNT") > 0);
    }

    [Fact]
    public void Variable_length_logical_and_bit_arrays_roundtrip()
    {
        var logical = new TableColumn("VL", "1PL(2)") { Data = new bool?[][] { [true, null], [false] } };
        var bits = new TableColumn("VX", "1PX(10)") { Data = new[] { Enumerable.Repeat(true, 10).ToArray(), new[] { false } } };
        var r = RoundTrip(BinaryTableHdu.Create([logical, bits]));
        Assert.Equal(new bool?[][] { [true, null], [false] }, r.ReadVariableLogicalColumn(0));
        Assert.Equal(Enumerable.Repeat(true, 10), r.ReadVariableBitColumn(1)[0]);
        Assert.Equal([false], r.ReadVariableBitColumn(1)[1]);
    }

    [Fact]
    public void Heap_gap_declared_by_theap_is_honoured()
    {
        var t = BinaryTableHdu.Create([TableColumn.CreateVariable<int>("V", [[10, 20], [30]])]);
        byte[] data = t.Source.ReadAll();
        long main = t.RowCount * t.RowWidth;
        var gapped = new byte[data.Length + 7];
        data.AsSpan(0, (int)main).CopyTo(gapped);
        data.AsSpan((int)main).CopyTo(gapped.AsSpan((int)main + 7));
        t.Source = new MemoryDataSource(gapped);
        t.Header.Set("PCOUNT", t.Header.Get<long>("PCOUNT") + 7).Set("THEAP", main + 7);
        var r = RoundTrip(t);
        Assert.Equal(new[] { new[] { 10, 20 }, [30] }, r.ReadVariableColumn<int>("V"));
        Assert.Equal(main + 7, r.Header.Get<long>("THEAP"));
    }

    [Fact]
    public void Scaling_null_and_dimensions_are_stored_in_the_header()
    {
        var c = TableColumn.Create<short>("SC", [2, 5, -1, 4, 0, 8], repeat: 2, unit: "Jy", dimensions: [2]);
        c.Scale = 0.5;
        c.Zero = 10;
        c.Null = -1L;
        c.Display = "F8.2";
        var r = RoundTrip(BinaryTableHdu.Create([c]));
        var meta = r.Columns[0];
        Assert.Equal("Jy", meta.Unit);
        Assert.Equal("F8.2", meta.Display);
        Assert.Equal(0.5, meta.Scale);
        Assert.Equal(-1L, meta.Null);
        Assert.Equal(new long[] { 2 }, meta.Dimensions!);
        Assert.Equal([11.0, 12.5, double.NaN, 12.0, 10.0, 14.0], r.ReadColumn<double>(0));
        Assert.Throws<InvalidOperationException>(() => r.ReadColumn<short>(0));
    }

    [Fact]
    public void Integral_scaling_keywords_are_written_as_integers_not_floats()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<ushort>("U", [1]), TableColumn.Create<ulong>("L", [1])]);
        Assert.IsType<long>(t.Header.Find("TZERO1")!.Value);
        Assert.IsType<System.Numerics.BigInteger>(t.Header.Find("TZERO2")!.Value);
        var scaled = TableColumn.Create<int>("S", [1]);
        scaled.Scale = 0.5;
        scaled.Zero = 3;
        var h = BinaryTableHdu.Create([scaled]).Header;
        Assert.IsType<double>(h.Find("TSCAL1")!.Value);
        Assert.IsType<long>(h.Find("TZERO1")!.Value);
    }

    [Fact]
    public void Empty_tables_and_zero_row_tables_are_valid()
    {
        var empty = RoundTrip(BinaryTableHdu.Create([]));
        Assert.Equal(0, empty.RowCount);
        Assert.Equal(0, empty.ColumnCount);

        var noRows = RoundTrip(BinaryTableHdu.Create([TableColumn.Create<int>("A", []), TableColumn.CreateVariableString("S", [])]));
        Assert.Equal(0, noRows.RowCount);
        Assert.Empty(noRows.ReadColumn<int>("A"));
        Assert.Equal(2, noRows.ColumnCount);
    }

    [Fact]
    public void Mandatory_table_keywords_come_first_and_in_order()
    {
        var r = RoundTrip(BinaryTableHdu.Create([TableColumn.Create<int>("A", [1])]));
        Assert.Equal(["XTENSION", "BITPIX", "NAXIS", "NAXIS1", "NAXIS2", "PCOUNT", "GCOUNT", "TFIELDS", "TTYPE1", "TFORM1"],
            r.Header.Take(10).Select(c => c.Keyword));
    }

    // ---- Editing -------------------------------------------------------------------------------------------------

    private static BinaryTableHdu Sample() => BinaryTableHdu.Create(
    [
        TableColumn.Create<int>("ID", [1, 2, 3, 4]),
        TableColumn.CreateString("NAME", ["a", "b", "c", "d"], 2),
        TableColumn.CreateVariable<short>("V", [[1], [2, 2], [], [4]]),
        TableColumn.Create<float>("XY", [1, 2, 3, 4, 5, 6, 7, 8], repeat: 2),
    ]);

    [Fact]
    public void Adds_and_removes_columns()
    {
        var t = Sample();
        t.Header.Set("EXTNAME", "SAMPLE");
        t.AddColumn(TableColumn.Create<double>("NEW", [9, 8, 7, 6]));
        Assert.Equal(5, t.ColumnCount);
        Assert.Equal([9.0, 8.0, 7.0, 6.0], t.ReadColumn<double>("NEW"));
        t.RemoveColumn(1);
        Assert.Equal(["ID", "V", "XY", "NEW"], t.Columns.Select(c => c.Name));
        Assert.Equal(new[] { 1, 2, 3, 4 }, t.ReadColumn<int>("ID"));
        Assert.Equal(new[] { new short[] { 1 }, [2, 2], [], [4] }, t.ReadVariableColumn<short>("V"));
        Assert.Equal("SAMPLE", t.Header.Get<string>("EXTNAME"));
        Assert.False(t.Header.Contains("TTYPE5"));
        Assert.Throws<FitsException>(() => t.AddColumn(TableColumn.Create<int>("BAD", [1])));
    }

    [Fact]
    public void Removes_and_appends_rows()
    {
        var t = Sample();
        t.RemoveRows(1, 2);
        Assert.Equal(2, t.RowCount);
        Assert.Equal(new[] { 1, 4 }, t.ReadColumn<int>("ID"));
        Assert.Equal(new[] { "a", "d" }, t.ReadStringColumn("NAME"));
        Assert.Equal(new[] { new short[] { 1 }, [4] }, t.ReadVariableColumn<short>("V"));
        Assert.Equal(new float[] { 1, 2, 7, 8 }, t.ReadColumn<float>("XY"));

        t.AppendRows(
        [
            TableColumn.Create<int>("ID", [10]),
            TableColumn.CreateString("NAME", ["z"], 2),
            TableColumn.CreateVariable<short>("V", [[9, 9, 9]]),
            TableColumn.Create<float>("XY", [0, 0], repeat: 2),
        ]);
        Assert.Equal(3, t.RowCount);
        Assert.Equal(new[] { 1, 4, 10 }, t.ReadColumn<int>("ID"));
        Assert.Equal(new[] { new short[] { 1 }, [4], [9, 9, 9] }, t.ReadVariableColumn<short>("V"));

        Assert.Throws<ArgumentOutOfRangeException>(() => t.RemoveRows(2, 5));
        Assert.Throws<ArgumentException>(() => t.AppendRows([TableColumn.Create<int>("ID", [1])]));
    }

    [Fact]
    public void Editing_table_metadata_through_the_header_is_picked_up()
    {
        var t = Sample();
        t.Header.Set("TTYPE1", "IDENT");
        t.Header.Set("TUNIT1", "none");
        Assert.Equal(0, t.IndexOf("ident"));
        Assert.Equal("none", t.Columns[0].Unit);
        Assert.Equal(new[] { 1, 2, 3, 4 }, t.ReadColumn<int>("IDENT"));
    }

    // ---- Malformed tables ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("Z")]
    [InlineData("1PZ")]
    [InlineData("1P")]
    [InlineData("2PJ(3)")]
    [InlineData("1PJ(x)")]
    public void Invalid_tforms_are_rejected(string tform) =>
        Assert.Throws<FitsException>(() => BinaryFormat.Parse(tform));

    [Fact]
    public void Tforms_are_parsed_per_the_standard()
    {
        Assert.Equal(new BinaryFormat(1, 'J', 0, 'J', 0), BinaryFormat.Parse("J"));
        Assert.Equal(new BinaryFormat(20, 'A', 0, 'A', 0), BinaryFormat.Parse("20A"));
        Assert.Equal(new BinaryFormat(20, 'A', 5, 'A', 0), BinaryFormat.Parse("20A5"));
        Assert.Equal(new BinaryFormat(0, 'J', 0, 'J', 0), BinaryFormat.Parse("0J"));
        Assert.Equal(new BinaryFormat(1, 'Q', 0, 'D', 99), BinaryFormat.Parse("1QD(99)"));
        Assert.Equal(new BinaryFormat(0, 'P', 0, 'B', 0), BinaryFormat.Parse("0PB"));
        Assert.Equal(3, BinaryFormat.Parse("17X").FieldBytes);
        Assert.Equal(16, BinaryFormat.Parse("1QJ").FieldBytes);
    }

    [Fact]
    public void Column_length_mismatch_and_oversized_strings_are_rejected()
    {
        Assert.Throws<FitsException>(() =>
            BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2]), TableColumn.Create<int>("B", [1, 2, 3])]));
        Assert.Throws<ArgumentException>(() => TableColumn.CreateString("S", ["toolong"], 3));
        Assert.Throws<ArgumentException>(() => TableColumn.CreateString("S", ["é"], 3));
        Assert.Throws<ArgumentException>(() => TableColumn.Create<int>("A", [1, 2, 3], repeat: 2));
        Assert.Throws<NotSupportedException>(() => TableColumn.Create<decimal>("A", [1m]));
    }

    [Fact]
    public void Row_width_that_contradicts_the_tforms_is_caught_on_save()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2])]);
        t.Header.Set("TFORM1", "2J");
        Assert.Throws<FitsException>(() => new FitsFile().Add(t).Save(new MemoryStream()));
    }

    [Fact]
    public void Descriptors_pointing_outside_the_heap_are_rejected_when_read()
    {
        var t = BinaryTableHdu.Create([TableColumn.CreateVariable<int>("V", [[1, 2, 3]])]);
        byte[] data = t.Source.ReadAll();
        data[3] = 100; // count = 100 elements, far beyond the 12 bytes of heap
        t.Source = new MemoryDataSource(data);
        Assert.Throws<FitsException>(() => t.ReadVariableColumn<int>(0));
        data[3] = 3;
        data[4] = 0x7F; // negative-free but huge offset
        t.Source = new MemoryDataSource(data);
        Assert.Throws<FitsException>(() => t.ReadVariableColumn<int>(0));
    }

    [Fact]
    public void Table_extension_with_wrong_structure_is_rejected_on_read()
    {
        var ms = new MemoryStream();
        new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 0).Set("EXTEND", true).Write(ms);
        new FitsHeader().Set("XTENSION", "BINTABLE").Set("BITPIX", 16).Set("NAXIS", 2).Set("NAXIS1", 2).Set("NAXIS2", 1)
            .Set("PCOUNT", 0).Set("GCOUNT", 1).Set("TFIELDS", 1).Write(ms);
        ms.Write(new byte[2880]);
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(ms.ToArray())));
    }

    // ---- ASCII tables ----------------------------------------------------------------------------------------------

    [Fact]
    public void Reads_astropy_ascii_table()
    {
        using var f = FitsFile.Open(Fixture("asciitable.fits"), new FitsReadOptions(true));
        var t = Assert.IsType<AsciiTableHdu>(f[1]);
        Assert.Equal(3, t.RowCount);
        Assert.Equal(63, t.RowWidth);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, t.ReadStringColumn("NAME"));
        Assert.Equal(new long?[] { 1, -22, 333 }, t.ReadIntegerColumn(1));
        Assert.Equal(new long[] { 1, -22, 333 }, t.ReadColumn<long>("N"));
        Assert.Equal([1.5, -2.25, 1000.125], t.ReadColumn<double>("X"));
        Assert.Equal([1.5e10, -2.5e-5, 0.0], t.ReadColumn<double>("Y"));
        Assert.Equal([3.141592653589793, 1e-300, -2.0], t.ReadColumn<double>("Z"));
        Assert.Equal([1, 9, 14, 24, 39], t.Columns.Select(c => c.AsciiStart));
    }

    [Fact]
    public void Ascii_tables_roundtrip_and_pad_with_blanks()
    {
        var t = AsciiTableHdu.Create(
        [
            TableColumn.CreateAsciiString("S", ["ab", "cdefg", ""]),
            TableColumn.CreateAsciiInteger("I", [1, null, -300]),
            TableColumn.CreateAsciiFloat("F", [1.5, double.NaN, -0.125], "F8.3"),
            TableColumn.CreateAsciiFloat("E", [1e-7, 12345.678, 0], "E12.4"),
            TableColumn.CreateAsciiFloat("D", [1.5, -2, 3e100], "D14.6"),
        ]);
        var file = new FitsFile().Add(t);
        var output = new MemoryStream();
        file.Save(output);
        Assert.Equal((byte)' ', output.ToArray()[^1]);

        var r = (AsciiTableHdu)RoundTrip(file)[1];
        Assert.Equal(new[] { "ab", "cdefg", "" }, r.ReadStringColumn(0));
        Assert.Equal(new long?[] { 1, null, -300 }, r.ReadIntegerColumn(1));
        Assert.Equal([1.5, double.NaN, -0.125], r.ReadColumn<double>(2));
        Assert.Equal(1e-7, r.ReadColumn<double>(3)[0], 12);
        Assert.InRange(r.ReadColumn<double>(3)[1] / 12345.678, 1 - 1e-4, 1 + 1e-4); // E12.4 keeps 5 significant digits
        Assert.InRange(r.ReadColumn<double>(4)[2] / 3e100, 1 - 1e-6, 1 + 1e-6);
        Assert.Equal(1, r.Columns[0].AsciiStart);
    }

    [Fact]
    public void Ascii_tnull_is_used_for_missing_values()
    {
        var c = TableColumn.CreateAsciiInteger("I", [5, null, 7], width: 4);
        c.Null = "-999";
        var t = AsciiTableHdu.Create([c]);
        Assert.Contains("-999", Encoding.ASCII.GetString(t.Source.ReadAll()));
        var r = (AsciiTableHdu)RoundTrip(new FitsFile().Add(t))[1];
        Assert.Equal(new long?[] { 5, null, 7 }, r.ReadIntegerColumn(0));
    }

    [Fact]
    public void Ascii_fields_without_a_decimal_point_use_the_implied_decimals()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [0, 0, 0], "F8.2")]);
        Assert.Equal(8, t.RowWidth);
        t.Source = new MemoryDataSource(Encoding.ASCII.GetBytes("   12345" + "    -5.5" + "  -00123"));
        Assert.Equal([123.45, -5.5, -1.23], t.ReadColumn<double>(0));
    }

    [Fact]
    public void Ascii_exponent_forms_are_parsed()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("E", [0, 0, 0, 0], "E10.3")]);
        Assert.Equal(10, t.RowWidth);
        t.Source = new MemoryDataSource(Encoding.ASCII.GetBytes("1.500E+03 " + "1.5D+03   " + "1.5e3     " + "1.5+3     "));
        Assert.All(t.ReadColumn<double>(0), v => Assert.Equal(1500.0, v));
    }

    [Fact]
    public void Ascii_values_that_do_not_fit_are_rejected()
    {
        Assert.Throws<FitsException>(() => AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("I", [123456], width: 3)]));
        Assert.Throws<FitsException>(() => AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [123456.789], "F5.1")]));
        Assert.Throws<FitsException>(() => AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [double.PositiveInfinity], "E12.4")]));
        Assert.Throws<FitsException>(() => AsciiTableHdu.Create([new TableColumn("X", "Q5") { Data = new double[1] }]));
        Assert.Throws<FitsException>(() => AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("A", [1]), TableColumn.CreateAsciiInteger("B", [1, 2])]));
    }

    [Fact]
    public void Ascii_tables_support_editing()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("I", [1, 2, 3]), TableColumn.CreateAsciiString("S", ["x", "y", "z"])]);
        t.AddColumn(TableColumn.CreateAsciiFloat("F", [0.5, 1.5, 2.5], "F6.2"));
        t.RemoveRows(0, 1);
        Assert.Equal(new long?[] { 2, 3 }, t.ReadIntegerColumn(0));
        Assert.Equal([1.5, 2.5], t.ReadColumn<double>("F"));
        t.RemoveColumn(0);
        Assert.Equal(["S", "F"], t.Columns.Select(c => c.Name));
        Assert.Equal(1, t.Columns[0].AsciiStart);
    }

    [Fact]
    public void Tables_can_follow_images_in_one_file_and_survive_a_roundtrip()
    {
        var file = new FitsFile()
            .Add(ImageHdu.Create<float>([1f, 2f], 2))
            .Add(BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2, 3])]))
            .Add(AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("B", [4, 5])]))
            .Add(ImageHdu.Create<byte>([9], 1));
        using var back = RoundTrip(file);
        Assert.IsType<ImageHdu>(back[0]);
        Assert.Equal(new[] { 1, 2, 3 }, ((BinaryTableHdu)back[1]).ReadColumn<int>("A"));
        Assert.Equal(new long?[] { 4, 5 }, ((AsciiTableHdu)back[2]).ReadIntegerColumn(0));
        Assert.Equal(new byte[] { 9 }, ((ImageHdu)back[3]).ReadPixels<byte>());
    }

    [Fact]
    public void A_table_as_first_hdu_gets_an_empty_primary()
    {
        var file = new FitsFile().Add(BinaryTableHdu.Create([TableColumn.Create<int>("A", [1])]));
        Assert.Equal(2, file.Count);
        Assert.IsType<ImageHdu>(file[0]);
    }
}
