using System.Numerics;
using System.Text;

namespace Eleris.FitsNet.Tests;

public class FitsHeaderTests
{
    private static FitsHeader RoundTrip(FitsHeader h, bool strict = true)
    {
        using var ms = new MemoryStream(h.ToBytes());
        var read = FitsHeader.Read(ms, new FitsReadOptions(strict));
        Assert.Equal(ms.Length, ms.Position);
        return read;
    }

    [Fact]
    public void Serialized_header_is_padded_to_2880_and_ends_with_END()
    {
        var h = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 16).Set("NAXIS", 0);
        byte[] bytes = h.ToBytes();
        Assert.Equal(2880, bytes.Length);
        Assert.Equal(h.SerializedLength, bytes.Length);
        Assert.Equal("END", Encoding.ASCII.GetString(bytes, 3 * 80, 3));
        Assert.All(bytes.Skip(4 * 80), b => Assert.Equal((byte)' ', b));
    }

    [Fact]
    public void Exactly_36_cards_spill_into_a_second_block_because_of_END()
    {
        var h = new FitsHeader();
        for (int i = 0; i < 36; i++) h.Add(FitsCard.CommentCard($"c{i}"));
        Assert.Equal(5760, h.ToBytes().Length);
        Assert.Equal(36, RoundTrip(h).Count);
    }

    [Fact]
    public void Roundtrips_all_value_types()
    {
        var h = new FitsHeader()
            .Set("B", true, "bool").Set("I", -42).Set("BIG", new BigInteger(ulong.MaxValue))
            .Set("D", 1.0e-300).Set("F", 0.1).Set("C", new Complex(1.5, -2.5))
            .Set("S", "it's here").Set("E", "").Set("UNDEF", null)
            .AddComment("a comment").AddHistory("a history")
            .Add(FitsCard.Commentary("", "blank key"));
        var r = RoundTrip(h);
        Assert.Equal(h.Count, r.Count);
        for (int i = 0; i < h.Count; i++)
        {
            Assert.Equal(h[i].Keyword, r[i].Keyword);
            Assert.Equal(h[i].Value, r[i].Value);
            Assert.Equal(h[i].Comment, r[i].Comment);
        }
    }

    [Fact]
    public void Roundtrips_long_string_with_continue_cards_and_comment()
    {
        string text = string.Concat(Enumerable.Range(0, 60).Select(i => $"seg{i:D3}-it's ")).TrimEnd();
        var h = new FitsHeader().Set("LONGSTR", text, "the comment");
        var r = RoundTrip(h);
        Assert.Equal(text, r.Get<string>("LONGSTR"));
        Assert.Equal("the comment", r.Find("LONGSTR")!.Comment);
        Assert.Equal(2, r.Count);
        Assert.Equal("OGIP 1.0", r.Get<string>("LONGSTRN")); // the convention is announced when CONTINUE cards are used
        Assert.False(RoundTrip(new FitsHeader().Set("SHORT", "x")).Contains("LONGSTRN"));
        Assert.Equal(1, RoundTrip(r).Cards.Count(c => c.Keyword == "LONGSTRN"));
    }

    [Fact]
    public void Long_string_with_quote_runs_never_splits_an_escaped_pair()
    {
        for (int pad = 0; pad < 8; pad++)
        {
            string text = new string('a', 60 + pad) + new string('\'', 30) + "tail";
            var r = RoundTrip(new FitsHeader().Set("Q", text));
            Assert.Equal(text, r.Get<string>("Q"));
        }
    }

    [Fact]
    public void Roundtrips_hierarch_keyword()
    {
        var r = RoundTrip(new FitsHeader().SetHierarch("ESO DET CHIP1 NAME", "CCD-44", "chip"));
        Assert.Equal("CCD-44", r.Get<string>("ESO DET CHIP1 NAME"));
        Assert.True(r.Find("ESO DET CHIP1 NAME")!.IsHierarch);
        r.Set("ESO DET CHIP1 NAME", "CCD-45"); // an existing HIERARCH card can be updated with Set
        Assert.Equal("CCD-45", r.Get<string>("ESO DET CHIP1 NAME"));
        Assert.Throws<ArgumentException>(() => new FitsHeader().Set("ESO DET CHIP1 NAME", "x")); // creating one needs SetHierarch
    }

    [Fact]
    public void Set_replaces_existing_value_and_keeps_position_and_comment()
    {
        var h = new FitsHeader().Set("A", 1, "keep me").Set("B", 2);
        h.Set("A", 10);
        Assert.Equal(10L, h.Get<long>("A"));
        Assert.Equal("keep me", h.Find("A")!.Comment);
        Assert.Equal("A", h[0].Keyword);
        Assert.Equal(2, h.Count);
    }

    [Fact]
    public void Remove_deletes_all_matching_cards()
    {
        var h = new FitsHeader().Set("A", 1).Add(FitsCard.CommentCard("x")).Set("B", 2);
        Assert.Equal(1, h.Remove("A"));
        Assert.False(h.Contains("A"));
        Assert.Equal(2, h.Count);
    }

    [Fact]
    public void Typed_getters_convert_numeric_values()
    {
        var h = new FitsHeader().Set("I", 7).Set("D", 2.5).Set("S", "x").Set("B", true);
        Assert.Equal(7, h.Get<int>("I"));
        Assert.Equal(7.0, h.Get<double>("I"));
        Assert.Equal(2.5, h.Get<double>("D"));
        Assert.False(h.TryGetValue<int>("D", out _));
        Assert.False(h.TryGetValue<int>("S", out _));
        Assert.False(h.TryGetValue<int>("B", out _));
        Assert.False(h.TryGetValue<int>("MISSING", out _));
        Assert.True(h.Get<bool>("B"));
    }

    [Fact]
    public void Typed_getter_does_not_overflow_silently()
    {
        var h = new FitsHeader().Set("I", 300);
        Assert.False(h.TryGetValue<byte>("I", out _));
    }

    [Fact]
    public async Task Async_read_matches_sync_read()
    {
        var h = new FitsHeader().Set("SIMPLE", true).Set("NAXIS", 2);
        using var ms = new MemoryStream(h.ToBytes());
        var r = await FitsHeader.ReadAsync(ms, new FitsReadOptions(true));
        Assert.Equal(2L, r.Get<long>("NAXIS"));
    }

    [Fact]
    public void Truncated_stream_throws()
    {
        byte[] bytes = new FitsHeader().Set("A", 1).ToBytes();
        Assert.Throws<FitsException>(() => FitsHeader.Read(new MemoryStream(bytes[..1000])));
        Assert.Throws<FitsException>(() => FitsHeader.Read(new MemoryStream([])));
    }

    [Fact]
    public void Missing_END_throws()
    {
        var bytes = new byte[2880];
        Array.Fill(bytes, (byte)' ');
        Assert.Throws<FitsException>(() => FitsHeader.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void Illegal_bytes_throw_in_strict_and_are_replaced_in_lenient()
    {
        byte[] bytes = new FitsHeader().Set("A", "x").ToBytes();
        bytes[20] = 0xE9;
        Assert.Throws<FitsException>(() => FitsHeader.Read(new MemoryStream(bytes), new FitsReadOptions(true)));
        Assert.NotNull(FitsHeader.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void Reading_a_header_leaves_stream_at_data_start()
    {
        var h = new FitsHeader().Set("SIMPLE", true);
        var ms = new MemoryStream();
        h.Write(ms);
        ms.Write([1, 2, 3]);
        ms.Position = 0;
        FitsHeader.Read(ms);
        Assert.Equal(2880, ms.Position);
    }
}
