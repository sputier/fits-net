namespace FitsNet.Examples;

public static class ConventionExamples
{
    public static void Inheritance()
    {
        #region Example:Conventions.Inherit
        // The primary header holds keywords shared by the extensions; an extension with INHERIT = T inherits them.
        var primary = ImageHdu.Create<byte>(new byte[1], 1);
        primary.Header.Set("TELESCOP", "Vespera Pro 2").Set("OBSERVER", "A. Astronomer");

        var table = BinaryTableHdu.Create([TableColumn.Create<int>("ID", [1, 2, 3])]);
        table.Header.Set("INHERIT", true).Set("OBSERVER", "B. Observer");   // its own value wins

        using var stream = new MemoryStream();
        new FitsFile().Add(primary).Add(table).Save(stream);
        stream.Position = 0;
        using var file = FitsFile.Read(stream);

        // The HDU itself is not modified: the merged view is a separate header.
        FitsHeader merged = file.GetEffectiveHeader(1);
        Console.WriteLine(merged.Get<string>("TELESCOP"));     // Vespera Pro 2
        Console.WriteLine(merged.Get<string>("OBSERVER"));     // B. Observer
        Console.WriteLine(file[1].Header.Contains("TELESCOP")); // False
        #endregion
    }

    public static void GreenBank()
    {
        #region Example:Conventions.GreenBank
        // The Green Bank convention moves values between header keywords and table columns.
        var table = BinaryTableHdu.Create(
        [
            TableColumn.Create<double>("FLUX", [1.0, 2.0, 3.0]),
            TableColumn.Create<double>("TSYS", [50.5, 50.5, 50.5]),         // the same in every row
            TableColumn.CreateString("OBJECT", ["M31", "M31", "M31"], 8),   // the same in every row
        ]);

        // A column that holds one value in all rows can become a keyword...
        IReadOnlyList<string> collapsed = table.CollapseConstantColumns();
        Console.WriteLine(string.Join(", ", collapsed));                    // TSYS, OBJECT
        Console.WriteLine(table.Header.Get<double>("TSYS"));                // 50.5

        // ...GetValue finds a value in the column or, failing that, in the keyword, as the convention asks...
        Console.WriteLine(table.GetValue("TSYS", 2));                       // 50.5

        // ...and a keyword can become a column again, so that rows can differ.
        table.ExpandKeyword("TSYS");
        Console.WriteLine(table.ColumnCount);                               // 2
        #endregion
    }

    public static void Stokes()
    {
        #region Example:Conventions.Stokes
        // A STOKES axis holds polarizations; their coordinate values are conventional (section 8.5 of the standard).
        Console.WriteLine(StokesParameters.Symbol(1));                      // I
        Console.WriteLine(StokesParameters.Symbol(-1));                     // RR
        Console.WriteLine(StokesParameters.Code("Q"));                      // 2
        Console.WriteLine(StokesParameters.Description("V"));               // Standard Stokes circular
        #endregion
    }

    public static void Mime()
    {
        #region Example:Conventions.Mime
        // The media types registered for FITS (RFC 4047), for a web server or an upload.
        Console.WriteLine(FitsMimeTypes.Application);                        // application/fits
        Console.WriteLine(FitsMimeTypes.Image);                              // image/fits
        Console.WriteLine(string.Join(" ", FitsMimeTypes.FileExtensions));   // .fits .fit .fts
        #endregion
    }
}
