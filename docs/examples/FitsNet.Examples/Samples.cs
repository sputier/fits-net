using FitsNet;

namespace FitsNet.Examples;

/// <summary>The files that the examples of the guide start from: they are created in the current directory.</summary>
public static class Samples
{
    /// <summary>Creates <c>m42.fits</c>: a 640 x 480 image with a celestial WCS and a CATALOG binary table.</summary>
    public static void Prepare()
    {
        var rng = new Random(42);
        var pixels = new ushort[640 * 480];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (ushort)(1000 + (i % 640) + (i / 640) + rng.Next(50));

        var image = ImageHdu.Create<ushort>(pixels, 640, 480);
        image.Header
            .Set("OBJECT", "M42", "name of the target")
            .Set("EXPTIME", 30.0, "[s] exposure time")
            .Set("DATE-OBS", "2026-01-15T21:30:00", "start of the exposure")
            .Set("CTYPE1", "RA---TAN").Set("CTYPE2", "DEC--TAN")
            .Set("CRPIX1", 320.5).Set("CRPIX2", 240.5)
            .Set("CRVAL1", 83.82).Set("CRVAL2", -5.39)
            .Set("CDELT1", -0.001).Set("CDELT2", 0.001)
            .Set("CUNIT1", "deg").Set("CUNIT2", "deg");

        var catalog = BinaryTableHdu.Create(
        [
            TableColumn.Create<int>("ID", [1, 2, 3, 4]),
            TableColumn.Create<double>("RA", [83.81, 83.83, 83.79, 83.85], unit: "deg"),
            TableColumn.Create<double>("DEC", [-5.40, -5.38, -5.41, -5.37], unit: "deg"),
            TableColumn.Create<float>("MAG", [9.5f, 10.2f, 11.8f, 12.1f]),
            TableColumn.CreateString("NAME", ["theta1 Ori A", "theta1 Ori B", "theta1 Ori C", "theta1 Ori D"], 16),
        ]);
        catalog.Header.Set("EXTNAME", "CATALOG");

        new FitsFile().Add(image).Add(catalog).Save("m42.fits");
    }
}
