using System.Numerics;

namespace FitsNet.Tests;

/// <summary>Regression tests for defects found in the milestone 1 code review.</summary>
public class FitsReviewRegressionTests
{
    private static string Line(string s) => s.PadRight(80);

    private static FitsHeader RoundTrip(FitsHeader h, bool strict = true)
    {
        using var ms = new MemoryStream(h.ToBytes());
        return FitsHeader.Read(ms, new FitsReadOptions(strict));
    }

    [Fact]
    public void Long_hierarch_key_with_long_string_throws_instead_of_looping()
    {
        var card = FitsCard.Hierarch(new string('K', 64), "'a");
        Assert.Throws<FitsException>(() => FitsCardCodec.Format(card).ToList());
        var card66 = FitsCard.Hierarch(new string('K', 66), "x");
        Assert.Throws<FitsException>(() => FitsCardCodec.Format(card66).ToList());
    }

    [Fact]
    public void Short_string_with_long_hierarch_key_is_not_truncated_by_padding()
    {
        var card = FitsCard.Hierarch(new string('K', 62), "abc");
        string line = FitsCardCodec.Format(card).Single();
        Assert.Equal(80, line.Length);
        Assert.Equal("abc", FitsCardCodec.Parse(line, strict: true).Value);
    }

    [Theory]
    [InlineData("END")]
    [InlineData("CONTINUE")]
    [InlineData("COMMENT")]
    [InlineData("HISTORY")]
    [InlineData("HIERARCH")]
    public void Reserved_keywords_cannot_carry_values(string keyword) =>
        Assert.Throws<ArgumentException>(() => new FitsCard(keyword, 1));

    [Theory]
    [InlineData("END")]
    [InlineData("CONTINUE")]
    [InlineData("HIERARCH")]
    public void Reserved_keywords_cannot_be_commentary(string keyword) =>
        Assert.Throws<ArgumentException>(() => FitsCard.Commentary(keyword, "x"));

    [Fact]
    public void Lenient_END_with_junk_becomes_commentary_and_never_an_End_card()
    {
        var card = FitsCardCodec.Parse(Line("END     junk"), strict: false);
        Assert.Equal(FitsCardKind.Commentary, card.Kind);
        Assert.Throws<FitsException>(() => FitsCardCodec.Parse(Line("END     junk"), strict: true));
    }

    [Fact]
    public void Lenient_header_with_END_junk_keeps_reading_and_roundtrips_safely()
    {
        byte[] bytes = new FitsHeader().Set("A", 1).Set("B", 2).ToBytes();
        System.Text.Encoding.ASCII.GetBytes("END     junk", 0, 12, bytes, 80);
        System.Text.Encoding.ASCII.GetBytes("END", 0, 3, bytes, 160);
        var h = FitsHeader.Read(new MemoryStream(bytes));
        Assert.Equal(2, h.Count);
        Assert.Equal(2, RoundTrip(h, strict: false).Count);
    }

    [Fact]
    public void Comments_that_do_not_fit_throw_instead_of_being_truncated()
    {
        Assert.Throws<FitsException>(() => FitsCardCodec.Format(new FitsCard("A", 1, new string('c', 80))).ToList());
        Assert.Throws<FitsException>(() =>
            FitsCardCodec.Format(new FitsCard("A", new string('s', 68), "dropped?")).ToList());
    }

    [Fact]
    public void Comment_that_just_fits_is_kept()
    {
        string comment = new('c', 80 - 33);
        string line = FitsCardCodec.Format(new FitsCard("A", 1, comment)).Single();
        Assert.Equal(comment, FitsCardCodec.Parse(line, strict: true).Comment);
    }

    [Fact]
    public void Lenient_equals_without_space_does_not_lose_first_value_character()
    {
        Assert.Equal(123L, FitsCardCodec.Parse(Line("X       =123"), strict: false).Value);
        Assert.Equal("abc", FitsCardCodec.Parse(Line("X       ='abc'"), strict: false).Value);
    }

    [Theory]
    [InlineData("X       = 1.5e3")]
    [InlineData("X       = 1d3")]
    [InlineData("X       = 'a' junk")]
    [InlineData("foo     = 1")]
    [InlineData("A B     = 1")]
    public void Strict_mode_rejects_non_conforming_cards(string line) =>
        Assert.Throws<FitsException>(() => FitsCardCodec.Parse(Line(line), strict: true));

    [Fact]
    public void Lenient_mode_accepts_lowercase_exponent() =>
        Assert.Equal(1500.0, FitsCardCodec.Parse(Line("X       = 1.5e3"), strict: false).Value);

    [Fact]
    public void Overflowing_float_literal_is_not_turned_into_infinity()
    {
        Assert.Throws<FitsException>(() => FitsCardCodec.Parse(Line("X       = 1.0E+400"), strict: true));
        Assert.Equal("1.0E+400", FitsCardCodec.Parse(Line("X       = 1.0E+400"), strict: false).Value);
    }

    [Fact]
    public void Header_without_END_stops_at_MaxHeaderBlocks()
    {
        var blank = new byte[2880 * 5];
        Array.Fill(blank, (byte)' ');
        Assert.Throws<FitsException>(() =>
            FitsHeader.Read(new MemoryStream(blank), new FitsReadOptions(MaxHeaderBlocks: 3)));
    }

    [Fact]
    public void SetComment_sets_and_clears_comments()
    {
        var h = new FitsHeader().Set("A", 1, "first");
        h.SetComment("A", "second");
        Assert.Equal("second", h.Find("A")!.Comment);
        h.SetComment("A", null);
        Assert.Null(h.Find("A")!.Comment);
        Assert.Equal(1L, h.Get<long>("A"));
        Assert.Throws<KeyNotFoundException>(() => h.SetComment("NOPE", "x"));
    }

    [Fact]
    public void Floats_keep_their_shortest_decimal_representation() =>
        Assert.Equal(0.1, new FitsCard("X", 0.1f).Value);

    [Theory]
    [InlineData("abc  ", "abc")]
    [InlineData("  abc", "  abc")]
    [InlineData("", "")]
    [InlineData(" ", "")]
    public void Trailing_blanks_are_insignificant_leading_blanks_are_kept(string written, string expected) =>
        Assert.Equal(expected, RoundTrip(new FitsHeader().Set("S", written)).Get<string>("S"));

    [Theory]
    [InlineData(66)]
    [InlineData(67)]
    [InlineData(68)]
    [InlineData(69)]
    [InlineData(134)]
    [InlineData(135)]
    [InlineData(136)]
    [InlineData(137)]
    public void String_boundary_lengths_roundtrip_with_80_character_cards(int length)
    {
        string text = new string('x', length - 1) + "z";
        foreach (var line in FitsCardCodec.Format(new FitsCard("S", text)))
            Assert.Equal(80, line.Length);
        Assert.Equal(text, RoundTrip(new FitsHeader().Set("S", text)).Get<string>("S"));
    }

    [Fact]
    public void Continue_cards_have_the_expected_shape()
    {
        var lines = FitsCardCodec.Format(new FitsCard("S", new string('x', 200))).ToList();
        Assert.True(lines.Count >= 3);
        Assert.EndsWith("&'", lines[0].TrimEnd());
        for (int i = 1; i < lines.Count; i++)
            Assert.StartsWith("CONTINUE  '", lines[i]);
        Assert.All(lines.Take(lines.Count - 1), l => Assert.EndsWith("&'", l.TrimEnd()));
        Assert.DoesNotContain("&", lines[^1]);
    }

    [Fact]
    public void Cards_crossing_a_2880_byte_boundary_roundtrip()
    {
        var h = new FitsHeader();
        for (int i = 0; i < 34; i++) h.Add(FitsCard.CommentCard($"c{i}"));
        h.Set("LONG", new string('y', 300));
        var r = RoundTrip(h);
        Assert.Equal(h.Count + 1, r.Count); // + the LONGSTRN convention card
        Assert.Equal(new string('y', 300), r.Get<string>("LONG"));
    }

    [Fact]
    public async Task ReadAsync_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FitsHeader.ReadAsync(new MemoryStream(new FitsHeader().Set("A", 1).ToBytes()), null, cts.Token));
    }

    [Fact]
    public void Getters_convert_bigint_and_float()
    {
        var h = new FitsHeader().Set("BIG", new BigInteger(ulong.MaxValue)).Set("F", 0.5);
        Assert.Equal(ulong.MaxValue, (ulong)h.Get<BigInteger>("BIG"));
        Assert.Equal(0.5f, h.Get<float>("F"));
    }

    [Fact]
    public void Extreme_doubles_roundtrip()
    {
        foreach (double d in new[] { double.MaxValue, double.Epsilon, -0.0, 1e16, 1e-5 })
        {
            double back = RoundTrip(new FitsHeader().Set("D", d)).Get<double>("D");
            Assert.Equal(BitConverter.DoubleToInt64Bits(d), BitConverter.DoubleToInt64Bits(back));
        }
    }
}
