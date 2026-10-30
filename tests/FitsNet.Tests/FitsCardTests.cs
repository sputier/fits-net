using System.Numerics;

namespace FitsNet.Tests;

public class FitsCardTests
{
    private static string Line(string s) => s.PadRight(80);

    private static FitsCard Parse(string s, bool strict = true) => FitsCardCodec.Parse(Line(s), strict);

    [Theory]
    [InlineData("SIMPLE  =                    T / conforms", true, "conforms")]
    [InlineData("FLAG    =                    F", false, null)]
    public void Parses_logical(string line, bool expected, string? comment)
    {
        var c = Parse(line);
        Assert.Equal(expected, c.Value);
        Assert.Equal(comment, c.Comment);
    }

    [Fact]
    public void Parses_integer_as_long_and_bigint_on_overflow()
    {
        Assert.Equal(-32L, Parse("BITPIX  =                  -32").Value);
        Assert.Equal(BigInteger.Parse("99999999999999999999"), Parse("BIG     = 99999999999999999999").Value);
    }

    [Theory]
    [InlineData("X       = 1.5", 1.5)]
    [InlineData("X       = 1.5E3", 1500.0)]
    [InlineData("X       = 1.5D-3", 0.0015)]
    [InlineData("X       = -.5", -0.5)]
    [InlineData("X       = 3.", 3.0)]
    public void Parses_floats_including_D_exponent(string line, double expected) =>
        Assert.Equal(expected, Parse(line).Value);

    [Fact]
    public void Parses_complex() =>
        Assert.Equal(new Complex(1.5, -2), Parse("Z       = (1.5, -2.0) / c").Value);

    [Fact]
    public void Parses_string_with_escaped_quote_and_comment()
    {
        var c = Parse("OBJECT  = 'O''Brien '           / who");
        Assert.Equal("O'Brien", c.Value);
        Assert.Equal("who", c.Comment);
    }

    [Fact]
    public void Slash_inside_string_is_not_a_comment()
    {
        var c = Parse("A       = 'a/b' / real comment");
        Assert.Equal("a/b", c.Value);
        Assert.Equal("real comment", c.Comment);
    }

    [Fact]
    public void Parses_undefined_value()
    {
        var c = Parse("UNDEF   =                      / nothing");
        Assert.Equal(FitsCardKind.Value, c.Kind);
        Assert.Null(c.Value);
        Assert.Equal("nothing", c.Comment);
    }

    [Fact]
    public void Parses_commentary_and_blank()
    {
        var h = Parse("HISTORY made by test");
        Assert.Equal(FitsCardKind.Commentary, h.Kind);
        Assert.Equal("made by test", h.Value);
        Assert.Equal("", Parse("").Keyword);
    }

    [Fact]
    public void Parses_hierarch()
    {
        var c = Parse("HIERARCH ESO DET CHIP = 12 / chip");
        Assert.Equal("ESO DET CHIP", c.Keyword);
        Assert.Equal(12L, c.Value);
        Assert.True(c.IsHierarch);
    }

    [Fact]
    public void Strict_rejects_garbage_value_lenient_keeps_text()
    {
        Assert.Throws<FitsException>(() => Parse("X       = abc"));
        Assert.Equal("abc", Parse("X       = abc", strict: false).Value);
    }

    [Fact]
    public void Strict_rejects_unterminated_string() =>
        Assert.Throws<FitsException>(() => Parse("X       = 'oops"));

    [Fact]
    public void Formats_fixed_format_with_right_justified_values()
    {
        string line = FitsCardCodec.Format(new FitsCard("NAXIS", 2, "dims")).Single();
        Assert.Equal(80, line.Length);
        Assert.Equal("NAXIS   =                    2 / dims", line.TrimEnd());
        Assert.Equal('2', line[29]);
    }

    [Fact]
    public void Formats_string_padded_to_eight_characters()
    {
        string line = FitsCardCodec.Format(new FitsCard("A", "x")).Single();
        Assert.StartsWith("A       = 'x       '", line);
    }

    [Fact]
    public void Formats_doubles_so_they_stay_floating_point()
    {
        string line = FitsCardCodec.Format(new FitsCard("X", 3.0)).Single();
        Assert.Equal(3.0, Parse(line).Value);
        Assert.IsType<double>(Parse(line).Value);
    }

    [Fact]
    public void Rejects_non_finite_numbers_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new FitsCard("X", double.NaN));
        Assert.Throws<ArgumentException>(() => new FitsCard("X", double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new FitsCard("X", new Complex(double.NaN, 0)));
    }

    [Fact]
    public void Value_types_are_normalised()
    {
        Assert.Equal(5L, new FitsCard("X", 5).Value);
        Assert.Equal(1.5, new FitsCard("X", 1.5f).Value);
        Assert.Equal(new BigInteger(ulong.MaxValue), new FitsCard("X", ulong.MaxValue).Value);
    }

    [Theory]
    [InlineData("lower")]
    [InlineData("")]
    public void Invalid_keywords_are_rejected(string keyword) =>
        Assert.Throws<ArgumentException>(() => new FitsCard(keyword, 1));

    [Fact]
    public void Non_ascii_text_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new FitsCard("X", "é"));

    [Fact]
    public void Long_hierarch_numeric_that_cannot_fit_throws_instead_of_truncating()
    {
        string key = string.Join(' ', Enumerable.Repeat("ABCDEFGH", 6));
        Assert.Throws<FitsException>(() => FitsCardCodec.Format(FitsCard.Hierarch(key, 1.2345678901234567e-100)).ToList());
    }
}
