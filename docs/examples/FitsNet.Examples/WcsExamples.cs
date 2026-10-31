namespace FitsNet.Examples;

public static class WcsExamples
{
    public static void Celestial()
    {
        #region Example:Wcs.Celestial
        using var file = FitsFile.Open("m42.fits");

        // Read the description from the header of any HDU (or from a FitsHeader with WorldCoordinateSystem.FromHeader).
        WorldCoordinateSystem wcs = file.Primary.GetWorldCoordinateSystem();
        Console.WriteLine($"{wcs.ProjectionCode} projection, {wcs.AxisCount} axes, celestial: {wcs.IsCelestial}");

        // Pixels (the center of the first pixel is 1.0) to the sky. Celestial coordinates are in degrees.
        double[] sky = wcs.PixelToWorld([320.5, 240.5]);
        Console.WriteLine($"RA {sky[wcs.LongitudeAxis]:F5}, Dec {sky[wcs.LatitudeAxis]:F5}");

        // And back.
        double[] pixel = wcs.WorldToPixel([83.83, -5.38]);
        Console.WriteLine($"({pixel[0]:F2}, {pixel[1]:F2})");
        #endregion
    }

    public static void Outside()
    {
        #region Example:Wcs.Outside
        var header = new FitsHeader()
            .Set("CTYPE1", "RA---SIN").Set("CTYPE2", "DEC--SIN")
            .Set("CRPIX1", 100.0).Set("CRPIX2", 100.0)
            .Set("CRVAL1", 150.0).Set("CRVAL2", 30.0)
            .Set("CDELT1", -1.0).Set("CDELT2", 1.0);
        var wcs = WorldCoordinateSystem.FromHeader(header);

        // A point that has no image in the projection (here: beyond the edge of the sphere) gives NaN, not an exception.
        double[] far = wcs.PixelToWorld([1000.0, 1000.0]);
        Console.WriteLine(double.IsNaN(far[0]));                       // True

        // The same for the opposite direction: the far side of the sky is not on the plane.
        double[] hidden = wcs.WorldToPixel([330.0, -30.0]);
        Console.WriteLine(double.IsNaN(hidden[0]));                    // True
        #endregion
    }

    public static void Batch()
    {
        #region Example:Wcs.Batch
        using var file = FitsFile.Open("m42.fits");
        var wcs = file.Primary.GetWorldCoordinateSystem();

        // Many points at once, without allocating: x and y of each point one after the other.
        const int width = 640;
        var pixels = new double[width * 2];
        for (int x = 0; x < width; x++)
        {
            pixels[2 * x] = x + 1;        // FITS pixel numbers start at 1
            pixels[2 * x + 1] = 240.5;
        }
        var world = new double[pixels.Length];
        wcs.PixelToWorld(pixels, world);
        Console.WriteLine($"The middle row goes from RA {world[0]:F3} to RA {world[2 * (width - 1)]:F3}");
        #endregion
    }

    public static void Spectral()
    {
        #region Example:Wcs.Spectral
        // A cube with two sky axes and a frequency axis sampled linearly.
        var header = new FitsHeader()
            .Set("CTYPE1", "RA---TAN").Set("CTYPE2", "DEC--TAN").Set("CTYPE3", "FREQ")
            .Set("CRPIX1", 1.0).Set("CRPIX2", 1.0).Set("CRPIX3", 1.0)
            .Set("CRVAL1", 10.0).Set("CRVAL2", 20.0).Set("CRVAL3", 1.4204e9)
            .Set("CDELT1", -0.01).Set("CDELT2", 0.01).Set("CDELT3", 1.0e5)
            .Set("CUNIT3", "Hz");
        var wcs = WorldCoordinateSystem.FromHeader(header);

        double[] world = wcs.PixelToWorld([1.0, 1.0, 11.0]);
        Console.WriteLine($"{world[2]} Hz");                           // 1421400000 Hz
        Console.WriteLine(string.Join(", ", wcs.AxisKinds));           // Celestial, Celestial, Spectral

        // A nonlinear spectral axis: sampled linearly in frequency, expressed as a wavelength (the -F2W algorithm).
        header.Set("CTYPE3", "WAVE-F2W").Set("CRVAL3", 0.2110611).Set("CDELT3", -1.0e-6).Set("CUNIT3", "m");
        var wavelengths = WorldCoordinateSystem.FromHeader(header);
        Console.WriteLine(wavelengths.PixelToWorld([1.0, 1.0, 11.0])[2]);
        #endregion
    }

    public static void Alternates()
    {
        #region Example:Wcs.Alternates
        // A header can hold several descriptions of the same axes: the primary one, and alternates named A to Z.
        var header = new FitsHeader()
            .Set("CTYPE1", "RA---TAN").Set("CTYPE2", "DEC--TAN")
            .Set("CRPIX1", 50.0).Set("CRPIX2", 50.0).Set("CRVAL1", 10.0).Set("CRVAL2", 10.0)
            .Set("CDELT1", -0.1).Set("CDELT2", 0.1)
            .Set("WCSNAMEA", "galactic")
            .Set("CTYPE1A", "GLON-CAR").Set("CTYPE2A", "GLAT-CAR")
            .Set("CRPIX1A", 50.0).Set("CRPIX2A", 50.0).Set("CRVAL1A", 100.0).Set("CRVAL2A", 5.0)
            .Set("CDELT1A", -0.1).Set("CDELT2A", 0.1);

        foreach (char letter in WorldCoordinateSystem.AlternatesIn(header))
        {
            var wcs = WorldCoordinateSystem.FromHeader(header, letter);
            Console.WriteLine($"{letter}: {wcs.Name}, {wcs.ProjectionCode}");     // A: galactic, CAR
        }
        #endregion
    }

    public static void Errors()
    {
        #region Example:Wcs.Errors
        var header = new FitsHeader().Set("CTYPE1", "RA---ZZZ").Set("CTYPE2", "DEC--ZZZ");
        try
        {
            WorldCoordinateSystem.FromHeader(header);
        }
        catch (NotSupportedException e)
        {
            Console.WriteLine(e.Message);       // not one of the 27 projections of the FITS standard
        }

        try
        {
            WorldCoordinateSystem.FromHeader(new FitsHeader().Set("CTYPE1", "RA---TAN"));
        }
        catch (FitsException e)
        {
            Console.WriteLine(e.Message);       // a celestial description needs a longitude and a latitude axis
        }
        #endregion
    }
}
