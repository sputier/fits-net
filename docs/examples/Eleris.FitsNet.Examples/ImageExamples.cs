namespace Eleris.FitsNet.Examples;

public static class ImageExamples
{
    public static void CreateTypes()
    {
        #region Example:Images.Types
        // The pixel type decides BITPIX: byte -> 8, short -> 16, int -> 32, long -> 64, float -> -32, double -> -64.
        var floats = ImageHdu.Create<float>(new float[100 * 50], 100, 50);
        Console.WriteLine(floats.BitPix);                  // Single

        // A 3-dimensional cube: 64 x 64 pixels, 10 planes. The first axis varies fastest.
        var cube = ImageHdu.Create<ushort>(new ushort[64 * 64 * 10], 64, 64, 10);
        Console.WriteLine(string.Join(" x ", cube.Axes));  // 64 x 64 x 10
        Console.WriteLine(cube.PixelCount);                // 40960

        // An image can be empty (no axes): a primary HDU that carries only a header.
        var empty = new ImageHdu();
        Console.WriteLine(empty.Axes.Count);              // 0
        #endregion
    }

    public static void Unsigned()
    {
        #region Example:Images.Unsigned
        // FITS has no unsigned integers: ushort pixels are stored as signed 16-bit values with BZERO = 32768.
        // The library applies the convention on both sides, so you only ever see ushort.
        var image = ImageHdu.Create<ushort>([0, 1, 40000, 65535], 4);
        Console.WriteLine(image.BitPix);                                  // Int16
        Console.WriteLine(image.Header.Get<double>("BZERO"));             // 32768

        ushort[] physical = image.ReadPixels<ushort>();                   // 0 1 40000 65535
        short[] stored = image.ReadRawPixels<short>();                    // -32768 -32767 7232 32767
        Console.WriteLine($"{physical[3]} is stored as {stored[3]}");
        #endregion
    }

    public static void Scaling()
    {
        #region Example:Images.Scaling
        // Physical value = BZERO + BSCALE * stored value. ReadPixels applies it, ReadRawPixels does not.
        var image = ImageHdu.Create<short>([0, 100, 200, 300], 4);
        image.Header.Set("BSCALE", 0.5).Set("BZERO", 10.0);

        double[] physical = image.ReadPixels<double>();    // 10 60 110 160
        short[] stored = image.ReadRawPixels<short>();     // 0 100 200 300
        Console.WriteLine($"{physical[1]} from {stored[1]}");
        #endregion
    }

    public static void Blank()
    {
        #region Example:Images.Blank
        // BLANK is the stored value that means "undefined" in an integer image. Read as float or double, it becomes NaN.
        var image = ImageHdu.Create<short>([10, -1, 30], 3);
        image.Header.Set("BLANK", -1);

        float[] values = image.ReadPixels<float>();
        Console.WriteLine(float.IsNaN(values[1]));          // True
        #endregion
    }

    public static void Regions()
    {
        #region Example:Images.Regions
        using var file = FitsFile.Open("m42.fits");
        var image = (ImageHdu)file.Primary;

        // Origin and shape are zero-based, one entry per axis, the first axis first.
        ushort[] cutout = image.ReadRegion<ushort>(origin: [100, 50], shape: [32, 16]);
        Console.WriteLine(cutout.Length);                   // 512

        // Subimage gives a new image HDU for the region, with the WCS reference pixel moved accordingly.
        ImageHdu crop = image.Subimage(origin: [100, 50], shape: [32, 16]);
        Console.WriteLine($"{crop.Axes[0]} x {crop.Axes[1]}");
        Console.WriteLine(crop.Header.Get<double>("CRPIX1"));   // 320.5 - 100 = 220.5
        new FitsFile().Add(crop).Save("m42-crop.fits");
        #endregion
    }

    public static void Replace()
    {
        #region Example:Images.SetPixels
        var image = ImageHdu.Create<short>(new short[4], 2, 2);
        image.Header.Set("OBJECT", "kept");

        // SetPixels replaces the data (and the type and shape); the other cards of the header are kept.
        image.SetPixels<float>([1f, 2f, 3f, 4f, 5f, 6f], 3, 2);
        Console.WriteLine($"{image.BitPix} {string.Join(" x ", image.Axes)} {image.Header.Get<string>("OBJECT")}");
        #endregion
    }

    public static void RandomGroups()
    {
        #region Example:Images.RandomGroups
        // Random groups (an old structure, still used for radio interferometry) store many small arrays,
        // each with a few parameters. Here: 3 groups of 2 parameters and a 2 x 2 data array.
        float[] values =
        [
            0.1f, 0.2f,   1, 2, 3, 4,     // group 0: parameters, then data
            0.3f, 0.4f,   5, 6, 7, 8,
            0.5f, 0.6f,   9, 10, 11, 12,
        ];
        var groups = RandomGroupsHdu.Create<float>(values, groupCount: 3, parameterCount: 2, dataAxes: [2, 2]);

        double[] parameters = groups.ReadParameters(1);     // about 0.3 and 0.4 (floats read as doubles)
        double[] data = groups.ReadData(1);                 // 5 6 7 8
        Console.WriteLine($"{groups.GroupCount} groups; group 1: {parameters[0]}, {data[3]}");
        #endregion
    }
}
