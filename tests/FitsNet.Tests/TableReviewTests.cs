using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FitsNet.Tests;

/// <summary>Regression tests for defects found in the milestone 3 code review.</summary>
public class TableReviewTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static FitsFile BinTableFile(long naxis1, long naxis2, long pcount, string tform, byte[] data)
    {
        var ms = new MemoryStream();
        new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 0).Set("EXTEND", true).Write(ms);
        new FitsHeader().Set("XTENSION", "BINTABLE").Set("BITPIX", 8).Set("NAXIS", 2).Set("NAXIS1", naxis1).Set("NAXIS2", naxis2)
            .Set("PCOUNT", pcount).Set("GCOUNT", 1).Set("TFIELDS", 1).Set("TTYPE1", "C").Set("TFORM1", tform).Write(ms);
        ms.Write(data);
        ms.Write(new byte[(2880 - data.Length % 2880) % 2880]);
        return FitsFile.Read(new MemoryStream(ms.ToArray()));
    }

    private static BinaryTableHdu RoundTrip(BinaryTableHdu t)
    {
        var ms = new MemoryStream();
        new FitsFile().Add(t).Save(ms);
        ms.Position = 0;
        return (BinaryTableHdu)FitsFile.Read(ms, new FitsReadOptions(true))[1];
    }

    // ---- Hostile headers -----------------------------------------------------------------------------------------

    [Fact]
    public void Row_count_larger_than_the_data_is_rejected_before_allocating()
    {
        using var f = BinTableFile(naxis1: 0, naxis2: 300_000_000, pcount: 4, "1J", new byte[4]);
        var t = (BinaryTableHdu)f[1];
        Assert.Throws<FitsException>(() => t.ReadColumn<int>(0));
    }

    [Theory]
    [InlineData("2000000000J")]
    [InlineData("9223372036854775807J")]
    [InlineData("9223372036854775807X")]
    [InlineData("1000000000A1")]
    public void Hostile_tform_repeat_counts_throw_FitsException(string tform)
    {
        using var f = BinTableFile(naxis1: 4, naxis2: 1, pcount: 0, tform, new byte[4]);
        var t = (BinaryTableHdu)f[1];
        Assert.Throws<FitsException>(() => t.ReadRaw(0));
    }

    [Fact]
    public void Zero_width_tables_with_absurd_row_counts_are_rejected()
    {
        using var f = BinTableFile(naxis1: 0, naxis2: 2_000_000_000, pcount: 0, "0J", []);
        Assert.Throws<FitsException>(() => ((BinaryTableHdu)f[1]).ReadRaw(0));
    }

    [Fact]
    public void Column_that_runs_past_the_row_is_rejected_instead_of_reading_the_next_row()
    {
        using var f = BinTableFile(naxis1: 4, naxis2: 2, pcount: 0, "2J", new byte[8]);
        Assert.Throws<FitsException>(() => ((BinaryTableHdu)f[1]).ReadRaw(0));
    }

    private static BinaryTableHdu VarTable(params int[][] rows) => BinaryTableHdu.Create([TableColumn.CreateVariable("V", rows)]);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10_000)]
    public void Invalid_theap_values_are_rejected(long theap)
    {
        var t = VarTable([1, 2], [3]);
        t.Header.Set("THEAP", theap);
        Assert.Throws<FitsException>(() => t.ReadVariableColumn<int>(0));
    }

    [Fact]
    public void Descriptors_with_absurd_counts_are_rejected_without_overflow()
    {
        var q = BinaryTableHdu.Create([new TableColumn("V", "1QX(1)") { Data = new[] { new[] { true } } }]);
        byte[] data = q.Source.ReadAll();
        BinaryPrimitives.WriteInt64BigEndian(data, long.MaxValue);
        q.Source = new MemoryDataSource(data);
        Assert.Throws<FitsException>(() => q.ReadVariableBitColumn(0));

        var p = VarTable([1]);
        data = p.Source.ReadAll();
        BinaryPrimitives.WriteInt32BigEndian(data, int.MaxValue);
        p.Source = new MemoryDataSource(data);
        Assert.Throws<FitsException>(() => p.ReadVariableColumn<int>(0));
    }

    [Fact]
    public void Descriptors_aliasing_one_big_region_cannot_amplify_memory_use()
    {
        const int rows = 100_000;
        var cells = new byte[rows][];
        Array.Fill(cells, Array.Empty<byte>());
        cells[0] = new byte[1_000_000];
        var t = BinaryTableHdu.Create([TableColumn.CreateVariable("V", cells)]);
        byte[] data = t.Source.ReadAll();
        for (int r = 0; r < rows; r++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(r * 8), 1_000_000);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(r * 8 + 4), 0);
        }
        t.Source = new MemoryDataSource(data);
        Assert.Throws<FitsException>(() => t.ReadVariableColumn<byte>(0));
    }

    [Fact]
    public void Zero_repeat_variable_columns_store_no_descriptor()
    {
        var v = new TableColumn("V", "0PJ") { Data = new[] { Array.Empty<int>(), Array.Empty<int>() } };
        var t = BinaryTableHdu.Create([v, TableColumn.Create<int>("A", [7, 8])]);
        Assert.Equal(4, t.RowWidth);
        var r = RoundTrip(t);
        Assert.Equal(new[] { 7, 8 }, r.ReadColumn<int>("A"));
        Assert.All(r.ReadVariableColumn<int>("V"), row => Assert.Empty(row));
    }

    // ---- Strings, precision --------------------------------------------------------------------------------------

    [Fact]
    public void Latin1_bytes_read_from_a_binary_table_can_be_edited_and_saved()
    {
        var t = BinaryTableHdu.Create([TableColumn.CreateString("S", ["ok", "deg"], 4)]);
        byte[] data = t.Source.ReadAll();
        data[1] = 0xB0;
        t.Source = new MemoryDataSource(data);
        Assert.Equal("o°", t.ReadStringColumn(0)[0]);

        t.AddColumn(TableColumn.Create<int>("N", [1, 2]));
        Assert.Equal("o°", RoundTrip(t).ReadStringColumn("S")[0]);
        Assert.Throws<ArgumentException>(() => TableColumn.CreateString("S", ["°"], 2)); // creation stays strict ASCII
    }

    [Fact]
    public void Latin1_bytes_read_from_an_ascii_table_can_be_edited_and_saved()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiString("S", ["ab", "cd"], 3)]);
        byte[] data = t.Source.ReadAll();
        data[1] = 0xB0;
        t.Source = new MemoryDataSource(data);
        t.RemoveRows(1, 1);
        Assert.Equal("a°", t.ReadStringColumn(0)[0]);
    }

    [Fact]
    public void Ascii_exponent_formats_keep_full_double_precision()
    {
        double[] values = [Math.PI, 0.1 + 0.2, 1e-300, -1.7976931348623157e308, 5e-324, 0, 123456789.12345678];
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("D", values, "D25.17")]);
        var ms = new MemoryStream();
        new FitsFile().Add(t).Save(ms);
        ms.Position = 0;
        var back = (AsciiTableHdu)FitsFile.Read(ms)[1];
        Assert.Equal(values.Select(BitConverter.DoubleToInt64Bits), back.ReadColumn<double>(0).Select(BitConverter.DoubleToInt64Bits));
        Assert.Contains("D+00", Encoding.ASCII.GetString(t.Source.ReadAll()));
    }

    [Fact]
    public void Editing_an_astropy_ascii_table_does_not_truncate_its_values()
    {
        using var f = FitsFile.Open(Fixture("asciitable.fits"));
        var t = (AsciiTableHdu)f[1];
        t.AddColumn(TableColumn.CreateAsciiInteger("EXTRA", [1, 2, 3]));
        Assert.Equal([3.141592653589793, 1e-300, -2.0], t.ReadColumn<double>("Z"));
    }

    // ---- Per-column keywords, schema, row counts -------------------------------------------------------------------

    [Fact]
    public void Registered_column_keywords_follow_their_column()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1]), TableColumn.Create<int>("B", [2])]);
        t.Header.Set("TCTYP2", "RA---TAN").Set("TDMIN2", 0.5).Set("TUCD2", "pos.eq.ra");
        Assert.Equal("RA---TAN", t.Columns[1].Extras["TCTYP"]);

        t.RemoveColumn(0);
        Assert.Equal("RA---TAN", t.Header.Get<string>("TCTYP1"));
        Assert.Equal(0.5, t.Header.Get<double>("TDMIN1"));
        Assert.False(t.Header.Contains("TCTYP2"));

        t.AddColumn(TableColumn.Create<int>("C", [3]));
        t.RemoveColumn(1);
        Assert.Equal("pos.eq.ra", t.Header.Get<string>("TUCD1"));
        Assert.False(t.Header.Contains("TUCD2"));
    }

    [Fact]
    public void Rows_with_spare_bytes_can_be_read_and_saved()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2])]);
        var wide = new byte[14];
        t.Source.ReadAll().AsSpan(0, 4).CopyTo(wide.AsSpan(0));
        t.Source.ReadAll().AsSpan(4, 4).CopyTo(wide.AsSpan(7));
        t.Source = new MemoryDataSource(wide);
        t.Header.Set("NAXIS1", 7);

        Assert.Equal(new[] { 1, 2 }, t.ReadColumn<int>(0));
        var r = RoundTrip(t);
        Assert.Equal(7, r.RowWidth);
        Assert.Equal(new[] { 1, 2 }, r.ReadColumn<int>(0));

        t.SetColumns(t.ReadColumns());
        Assert.Equal(4, t.RowWidth);
    }

    [Fact]
    public void Row_count_survives_removing_the_last_column()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2, 3])]);
        t.RemoveColumn(0);
        Assert.Equal(3, t.RowCount);
        Assert.Equal(0, t.RowWidth);
        t.AddColumn(TableColumn.Create<short>("B", [4, 5, 6]));
        Assert.Equal(3, t.RowCount);
        Assert.Throws<FitsException>(() => t.AddColumn(TableColumn.Create<short>("C", [1])));
    }

    [Fact]
    public void AppendRows_rejects_a_different_scaling_null_or_type()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<short>("A", [1])]);
        Assert.Throws<ArgumentException>(() => t.AppendRows([TableColumn.Create<ushort>("A", [65535])]));
        Assert.Throws<ArgumentException>(() => t.AppendRows([TableColumn.Create<int>("A", [1])]));

        var withNull = TableColumn.Create<short>("A", [2]);
        withNull.Null = -1;
        Assert.Throws<ArgumentException>(() => t.AppendRows([withNull]));

        t.AppendRows([TableColumn.Create<short>("A", [2])]);
        Assert.Equal(new short[] { 1, 2 }, t.ReadColumn<short>(0));
    }

    [Fact]
    public void Callers_columns_are_not_modified_and_can_be_reused()
    {
        var v = TableColumn.CreateVariable<int>("V", [[1, 2], [3, 4, 5]]);
        v.Format = "1PJ(2)";
        var first = BinaryTableHdu.Create([v]);
        var second = BinaryTableHdu.Create([v, TableColumn.Create<int>("X", [1, 2])]);
        Assert.Equal("1PJ(2)", v.Format);
        Assert.Equal("1PJ(3)", first.Columns[0].Format);
        Assert.Equal(2, second.ColumnCount);

        var s = TableColumn.CreateAsciiInteger("I", [1]);
        AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("Z", [5]), s]);
        Assert.Equal(0, s.AsciiStart);
    }

    [Fact]
    public void Null_values_are_normalised_and_validated()
    {
        var c = TableColumn.Create<short>("A", [1]);
        c.Null = (short)-1;
        Assert.IsType<long>(c.Null);
        c.Null = null;
        Assert.Throws<ArgumentException>(() => c.Null = 3.5);
        Assert.Throws<FitsException>(() =>
        {
            var d = TableColumn.Create<int>("D", [1]);
            d.Scale = double.NaN;
            BinaryTableHdu.Create([d]);
        });
    }

    [Fact]
    public void RemoveRows_with_overflowing_arguments_reports_a_range_error()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2, 3])]);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => t.RemoveRows(1, long.MaxValue));
        Assert.Equal("start", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.RemoveRows(4, 0));
        t.RemoveRows(3, 0);
        Assert.Equal(3, t.RowCount);
    }

    [Fact]
    public void Overlapping_ascii_columns_are_rejected()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("A", [1], 5), TableColumn.CreateAsciiInteger("B", [2], 5)]);
        t.Header.Set("TBCOL2", 3);
        Assert.Throws<FitsException>(() => new FitsFile().Add(t).Save(new MemoryStream()));
    }

    [Fact]
    public void Ascii_columns_outside_the_row_or_table_are_rejected_on_read()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("A", [1, 2], 5)]);
        t.Header.Set("NAXIS2", 1000);
        Assert.Throws<FitsException>(() => t.ReadIntegerColumn(0));
        t.Header.Set("NAXIS2", 2).Set("TBCOL1", 4);
        Assert.Throws<FitsException>(() => t.ReadIntegerColumn(0));
    }

    [Fact]
    public void Reading_all_columns_of_a_very_wide_table_stays_fast()
    {
        var columns = Enumerable.Range(0, 999).Select(i => TableColumn.Create<int>("C" + i.ToString(CultureInfo.InvariantCulture), [i])).ToList();
        var t = BinaryTableHdu.Create(columns);
        var sw = Stopwatch.StartNew();
        var read = t.ReadColumns();
        Assert.Equal(999, read.Count);
        Assert.Equal(new[] { 998 }, (int[])read[998].Data!);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }
}
