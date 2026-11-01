namespace Eleris.FitsNet.Examples;

public static class FilesExamples
{
    public static void ReadFromStream()
    {
        #region Example:Files.ReadStream
        // Read loads the whole file in memory; the stream stays open and belongs to you.
        byte[] bytes = File.ReadAllBytes("m42.fits");
        using var stream = new MemoryStream(bytes);

        using var file = FitsFile.Read(stream);
        Console.WriteLine($"{file.Count} HDUs");
        #endregion
    }

    public static void Lazy()
    {
        #region Example:Files.Lazy
        // Open keeps the file open and reads the data only when you ask for it.
        using var file = FitsFile.Open("m42.fits");
        var image = (ImageHdu)file.Primary;

        // Reading a small region touches only the bytes it needs, however large the image is.
        ushort[] corner = image.ReadRegion<ushort>(origin: [0, 0], shape: [8, 8]);
        Console.WriteLine($"{corner.Length} pixels read");
        #endregion
    }

    public static void Structure()
    {
        #region Example:Files.Structure
        var file = new FitsFile();

        // The first HDU is the primary HDU. A table cannot be primary, so an empty image is added in front of it when needed.
        file.Add(BinaryTableHdu.Create([TableColumn.Create<int>("ID", [1, 2, 3])]));
        Console.WriteLine(file.Count);                           // 2: the empty primary image and the table
        Console.WriteLine(file.Primary is ImageHdu);             // True

        // Extensions can be inserted, removed and looked up like in a list.
        file.Add(ImageHdu.Create<byte>(new byte[16], 4, 4));
        file.RemoveAt(1);
        Hdu last = file[file.Count - 1];
        Console.WriteLine(last.GetType().Name);                  // ImageHdu

        // Find an extension by its EXTNAME.
        Hdu? catalog = file.FirstOrDefault(h => h.Header.Get<string>("EXTNAME") == "CATALOG");
        Console.WriteLine(catalog is null);                      // True
        #endregion
    }

    public static void SaveToStream()
    {
        #region Example:Files.SaveStream
        using var file = FitsFile.Open("m42.fits");

        using var output = new MemoryStream();
        file.Save(output);

        byte[] bytes = output.ToArray();
        Console.WriteLine($"{bytes.Length} bytes, a multiple of {FitsHeader.BlockSize}: {bytes.Length % FitsHeader.BlockSize == 0}");
        #endregion
    }

    public static void Overwrite()
    {
        #region Example:Files.Overwrite
        File.Copy("m42.fits", "m42-copy.fits", overwrite: true);

        // Saving over the file that is open is safe: the new content is written to a temporary file, then moved in place.
        using var file = FitsFile.Open("m42-copy.fits");
        file.Primary.Header.Set("OBSERVER", "A. Astronomer");
        file.Save("m42-copy.fits");

        // The instance is still usable after the save: it reads from the new file.
        Console.WriteLine(((ImageHdu)file.Primary).ReadPixels<ushort>().Length);
        #endregion
    }

    public static void Options()
    {
        #region Example:Files.Options
        // Strict reading refuses anything that deviates from the standard instead of tolerating it.
        var strict = new FitsReadOptions(Strict: true);
        using (FitsFile.Open("m42.fits", strict)) { }

        // The limits protect a program that reads files it does not control.
        var careful = new FitsReadOptions(
            MaxHeaderBlocks: 100,          // one header: at most 100 blocks of 2880 bytes
            MaxTotalHeaderBlocks: 1_000,   // all the headers of the file together
            MaxInputBytes: 100_000_000);   // Read(Stream): at most 100 MB taken from the stream

        try
        {
            using var stream = File.OpenRead("m42.fits");
            using var file = FitsFile.Read(stream, careful);
            Console.WriteLine($"{file.Count} HDUs");
        }
        catch (FitsException e)
        {
            Console.WriteLine($"Not accepted: {e.Message}");
        }
        #endregion
    }
}
