using System.Numerics;

namespace FitsNet.Examples;

public static class HeaderExamples
{
    public static void Values()
    {
        #region Example:Headers.Values
        var header = new FitsHeader();

        // Set replaces the value of a keyword or appends a new card; the calls can be chained.
        header
            .Set("OBJECT", "M42", "name of the target")   // string
            .Set("NAXIS3", 3)                            // integer
            .Set("EXPTIME", 30.5, "[s] exposure")        // floating point
            .Set("FLATCORR", true)                       // logical
            .Set("GAIN", new Complex(1.5, -0.5))         // complex
            .Set("NODATA", null);                     // a card with no value

        // Get converts the value and returns the default of the type when the keyword is missing or cannot be converted.
        string? target = header.Get<string>("OBJECT");
        double exposure = header.Get<double>("EXPTIME");    // 30.5
        int axes = header.Get<int>("NAXIS3");               // 3
        int missing = header.Get<int>("NOPE");              // 0

        // TryGetValue tells a missing keyword from a zero.
        if (header.TryGetValue("EXPTIME", out double seconds))
            Console.WriteLine($"{target}: {seconds} s, {axes} axes, {missing}");

        // Find returns the card itself, with its comment.
        FitsCard? card = header.Find("EXPTIME");
        Console.WriteLine($"{card!.Keyword} = {card.Value} / {card.Comment}");

        Console.WriteLine(header.Contains("OBJECT"));       // True
        header.Remove("NODATA");
        #endregion
    }

    public static void Comments()
    {
        #region Example:Headers.Comments
        var header = new FitsHeader();
        header.Set("EXPTIME", 30.0, "[s] first comment");

        // A comment given to Set replaces the old one; no comment keeps the old one.
        header.Set("EXPTIME", 60.0);                         // the comment is still "[s] first comment"
        header.Set("EXPTIME", 60.0, "[s] exposure time");    // replaced
        header.SetComment("EXPTIME", null);                  // removed
        #endregion
    }

    public static void Commentary()
    {
        #region Example:Headers.Commentary
        var header = new FitsHeader();

        // COMMENT and HISTORY cards have no value. A long text is written over several consecutive cards.
        header.AddComment("Calibrated with the master flat of 2026-01-10.");
        header.AddHistory("Dark subtracted");
        header.AddHistory("Flat fielded");

        foreach (FitsCard card in header.Where(c => c.Kind == FitsCardKind.Commentary))
            Console.WriteLine($"{card.Keyword}: {card.Value}");
        #endregion
    }

    public static void Cards()
    {
        #region Example:Headers.Cards
        var header = new FitsHeader()
            .Set("SIMPLE", true)
            .Set("BITPIX", 16)
            .Set("NAXIS", 0);

        // Cards are kept in order and can be inserted anywhere.
        header.Insert(1, new FitsCard("TELESCOP", "Vaonis Vespera Pro 2", "instrument"));

        for (int i = 0; i < header.Count; i++)
            Console.WriteLine($"{i}: {header[i]}");

        // Header is enumerable, so LINQ works on the cards.
        string[] keywords = header.Select(c => c.Keyword).ToArray();
        Console.WriteLine(string.Join(" ", keywords));
        #endregion
    }

    public static void Hierarch()
    {
        #region Example:Headers.Hierarch
        var header = new FitsHeader();

        // Standard keywords have at most 8 characters. The HIERARCH convention (widely used, but not part of the standard)
        // allows longer ones; it is never chosen silently.
        header.SetHierarch("ESO DET CHIP1 ID", "CCD-42", "detector identifier");

        Console.WriteLine(header.Find("ESO DET CHIP1 ID")!.IsHierarch);   // True
        #endregion
    }

    public static void LongStrings()
    {
        #region Example:Headers.LongStrings
        var header = new FitsHeader();

        // A string longer than a card is written with CONTINUE cards (the long string convention) and read back whole.
        string notes = string.Join(" ", Enumerable.Repeat("a long observing note", 12));
        header.Set("NOTES", notes);

        byte[] bytes = header.ToBytes();
        FitsHeader back = FitsHeader.Read(new MemoryStream(bytes));
        Console.WriteLine(back.Get<string>("NOTES") == notes);            // True
        #endregion
    }

    public static void Serialization()
    {
        #region Example:Headers.Serialization
        var header = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 0);

        // A header is written as a whole number of 2880-byte blocks that ends with the END card.
        byte[] bytes = header.ToBytes();
        Console.WriteLine($"{bytes.Length} bytes = {bytes.Length / FitsHeader.BlockSize} block");

        // Write puts the same bytes on a stream; Read parses a header from a stream.
        using var stream = new MemoryStream();
        header.Write(stream);
        stream.Position = 0;
        FitsHeader parsed = FitsHeader.Read(stream);
        Console.WriteLine(parsed.Count);
        #endregion
    }

    public static void Cleanup()
    {
        #region Example:Headers.Remove
        var header = new FitsHeader().Set("A", 1).Set("B", 2).AddComment("note");

        int removed = header.Remove("A");   // 1: the number of cards removed
        header.RemoveAt(0);                 // removes the card that is now first ("B")
        header.Clear();                     // removes everything
        Console.WriteLine($"{removed} {header.Count}");
        #endregion
    }
}
