using System.Globalization;
using System.Text;

namespace Eleris.FitsNet.Tests;

/// <summary>The fields of an ASCII table are parsed from the bytes: the usual forms take a fast path, the odd ones the careful one.</summary>
public class AsciiFieldParsingTests
{
    private static AsciiTableHdu Table(string format, int width, params string[] fields)
    {
        var table = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", new double[fields.Length], format)]);
        table.Source = new MemoryDataSource(Encoding.Latin1.GetBytes(string.Concat(fields.Select(f => f.PadRight(width)))));
        return table;
    }

    [Fact]
    public void Real_numbers_are_read_in_every_usual_notation()
    {
        var t = Table("F12.3", 12, "12.5", "   -0.125", "1.5E+3", "1.5e3", "2.5D-1", "7.d2", "+8.", ".5", "1.25E+003");
        Assert.Equal([12.5, -0.125, 1500.0, 1500.0, 0.25, 700.0, 8.0, 0.5, 1250.0], t.ReadColumn<double>(0));
    }

    [Fact]
    public void A_missing_decimal_point_is_implied_and_a_bare_signed_exponent_is_understood()
    {
        var t = Table("F10.2", 10, "12345", "-5", "1.5+3", "1.5-2", "12E3");
        Assert.Equal([123.45, -0.05, 1500.0, 0.015, 120.0], t.ReadColumn<double>(0));
    }

    [Fact]
    public void Blank_fields_are_NaN()
    {
        var t = Table("F8.2", 8, "", "     ", "1.5");
        Assert.Equal([double.NaN, double.NaN, 1.5], t.ReadColumn<double>(0));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("E5")]
    [InlineData("-")]
    [InlineData("1E")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1E999")]
    [InlineData("1 5")]
    public void Invalid_numbers_are_refused_with_the_field_in_the_message(string field)
    {
        var t = Table("F10.2", 10, "1.5", field);
        var ex = Assert.Throws<FitsException>(() => t.ReadColumn<double>(0));
        Assert.Contains("row 1", ex.Message);
    }

    [Fact]
    public void Fields_wider_than_the_fast_path_are_still_read()
    {
        string wide = "0." + new string('1', 70) + "E+2";
        var t = Table("E90.70", 90, wide, "12.5");
        double[] values = t.ReadColumn<double>(0);
        Assert.Equal(double.Parse(wide, CultureInfo.InvariantCulture), values[0]);
        Assert.Equal(12.5, values[1]);
    }

    [Fact]
    public void Many_values_agree_with_the_framework_parser()
    {
        var random = new Random(5);
        string[] fields = Enumerable.Range(0, 2000).Select(_ =>
        {
            double v = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-8, 9));
            return (random.Next(3) switch { 0 => v.ToString("F6", CultureInfo.InvariantCulture), 1 => v.ToString("E8", CultureInfo.InvariantCulture), _ => v.ToString("R", CultureInfo.InvariantCulture) });
        }).ToArray();
        var t = Table("E24.8", 24, fields);
        double[] read = t.ReadColumn<double>(0);
        for (int i = 0; i < fields.Length; i++) Assert.Equal(double.Parse(fields[i], CultureInfo.InvariantCulture), read[i]);
    }

    [Fact]
    public void Integers_are_read_trimmed_and_blank_ones_are_null()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [0, 0, 0, 0, 0], 8)]);
        t.Source = new MemoryDataSource(Encoding.Latin1.GetBytes("      42" + "      -7" + "      +5" + "        " + "9999999 "));
        Assert.Equal<long?>([42, -7, 5, null, 9999999], t.ReadIntegerColumn(0));
    }

    [Fact]
    public void An_invalid_integer_is_named_in_the_error()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [0, 0], 6)]);
        t.Source = new MemoryDataSource(Encoding.Latin1.GetBytes("    12" + "   4x2"));
        var ex = Assert.Throws<FitsException>(() => t.ReadIntegerColumn(0));
        Assert.Contains("'4x2'", ex.Message);
        Assert.Contains("row 1", ex.Message);
    }

    [Fact]
    public void Character_fields_lose_their_trailing_blanks_only()
    {
        var t = AsciiTableHdu.Create([TableColumn.CreateAsciiString("S", ["", ""], 6)]);
        t.Source = new MemoryDataSource(Encoding.Latin1.GetBytes("  ab  " + "café  "));
        Assert.Equal(["  ab", "café"], t.ReadStringColumn(0));
    }

    [Fact]
    public void Written_numbers_have_the_fortran_forms_and_the_right_alignment()
    {
        var e = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("E", [1.5e10, 1e100, 1e-5, 0.0, -2.5, double.NaN], "E14.7")]);
        string[] expectedE = ["1.5000000E+10", "1.0000000E+100", "1.0000000E-05", "0.0000000E+00", "-2.5000000E+00", ""];
        string textE = Encoding.ASCII.GetString(e.Source.ReadAll());
        for (int i = 0; i < expectedE.Length; i++) Assert.Equal(expectedE[i].PadLeft(14), textE.Substring(i * 14, 14));

        var f = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [12.5, -0.25, 0.5, 100000.125], "F10.3")]);
        string textF = Encoding.ASCII.GetString(f.Source.ReadAll());
        Assert.Equal("    12.500" + "    -0.250" + "     0.500" + "100000.125", textF);

        var d = AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("D", [2.0], "D12.4")]);
        Assert.Equal("  2.0000D+00", Encoding.ASCII.GetString(d.Source.ReadAll()));

        var mixed = AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("I", [7, -12345, 0], 8), TableColumn.CreateAsciiString("S", ["ab", "", "cdef"], 5)]);
        Assert.Equal("       7 ab   " + "  -12345      " + "       0 cdef ", Encoding.ASCII.GetString(mixed.Source.ReadAll()));
    }

    [Fact]
    public void A_value_that_does_not_fit_is_refused_and_a_null_is_written_as_TNULL_or_blank()
    {
        var tooWide = Assert.Throws<FitsException>(() => AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [123456.789], "F8.3")]));
        Assert.Contains("does not fit", tooWide.Message);
        Assert.Throws<FitsException>(() => AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [123456789], 5)]));
        Assert.Throws<ArgumentException>(() => TableColumn.CreateAsciiString("S", ["too long"], 3));   // refused when the column is made
        Assert.Throws<FitsException>(() => AsciiTableHdu.Create([TableColumn.CreateAsciiFloat("F", [double.PositiveInfinity], "F8.3")]));
        Assert.Throws<ArgumentException>(() => TableColumn.CreateAsciiString("S", ["tab	here"], 12));

        var nulls = AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [5, null, 7], 4)]);
        Assert.Equal("   5" + "    " + "   7", Encoding.ASCII.GetString(nulls.Source.ReadAll()));
    }
}
