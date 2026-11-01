namespace Eleris.FitsNet.Examples;

public static class StartExamples
{
    public static void Inspect()
    {
        #region Example:Start.Inspect
        // Open reads the headers now and the data when asked: opening a large file is instantaneous.
        using var file = FitsFile.Open("m42.fits");

        foreach (Hdu hdu in file)
            Console.WriteLine($"{hdu.GetType().Name}: {hdu.Header.Count} cards, {hdu.DataLength} bytes of data");

        var image = (ImageHdu)file.Primary;
        Console.WriteLine($"{image.Axes[0]} x {image.Axes[1]} pixels, BITPIX {(int)image.BitPix}");
        Console.WriteLine($"Target: {image.Header.Get<string>("OBJECT")}, exposure {image.Header.Get<double>("EXPTIME")} s");

        ushort[] pixels = image.ReadPixels<ushort>();
        Console.WriteLine($"First pixel: {pixels[0]}");
        #endregion
    }

    public static void Create()
    {
        #region Example:Start.Create
        // 256 x 256 pixels, the first axis (x) varying fastest.
        var pixels = new short[256 * 256];
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
                pixels[y * 256 + x] = (short)(x + y);

        var image = ImageHdu.Create<short>(pixels, 256, 256);
        image.Header.Set("OBJECT", "test pattern", "name of the target");

        new FitsFile().Add(image).Save("pattern.fits");
        #endregion
    }

    public static void Edit()
    {
        #region Example:Start.Edit
        using var file = FitsFile.Open("m42.fits");

        file.Primary.Header
            .Set("OBSERVER", "A. Astronomer", "who took the image")
            .AddHistory("Header completed with Eleris.FitsNet");

        // The new file is written completely before it replaces the target, so a failure never leaves half a file.
        file.Save("m42-edited.fits");
        #endregion
    }

    public static void Table()
    {
        #region Example:Start.Table
        using var file = FitsFile.Open("m42.fits");

        var catalog = (BinaryTableHdu)file[1];
        Console.WriteLine($"{catalog.RowCount} rows, columns: {string.Join(", ", catalog.Columns.Select(c => c.Name))}");

        double[] ra = catalog.ReadColumn<double>("RA");
        float[] magnitude = catalog.ReadColumn<float>("MAG");
        string[] names = catalog.ReadStringColumn("NAME");
        for (int row = 0; row < catalog.RowCount; row++)
            Console.WriteLine($"{names[row]}: RA {ra[row]} deg, magnitude {magnitude[row]}");
        #endregion
    }

    public static void Coordinates()
    {
        #region Example:Start.Coordinates
        using var file = FitsFile.Open("m42.fits");

        // The WCS keywords of the header link pixels to the sky. Pixel numbers start at 1, as in the FITS standard.
        var wcs = file.Primary.GetWorldCoordinateSystem();
        double[] sky = wcs.PixelToWorld([100.0, 200.0]);
        Console.WriteLine($"Pixel (100, 200) is at RA {sky[0]:F4} deg, Dec {sky[1]:F4} deg");

        double[] pixel = wcs.WorldToPixel([83.82, -5.39]);
        Console.WriteLine($"M42 is at pixel ({pixel[0]:F1}, {pixel[1]:F1})");
        #endregion
    }
}
