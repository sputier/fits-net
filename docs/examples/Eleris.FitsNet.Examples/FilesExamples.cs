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

    public static void CreateNew()
    {
        #region Example:Files.CreateNew
        using var file = FitsFile.Open("m42.fits");
        file.Primary.Header.Set("OBSERVER", "A. Astronomer");

        // CreateNew never replaces a file, even one that another program creates while the file is being written.
        file.Save("m42-reduced.fits", ChecksumPolicy.Auto, FitsSaveMode.CreateNew);
        try
        {
            file.Save("m42-reduced.fits", ChecksumPolicy.Auto, FitsSaveMode.CreateNew);
        }
        catch (FitsFileExistsException e)
        {
            Console.WriteLine($"Not replaced: {Path.GetFileName(e.FileName)}");   // Not replaced: m42-reduced.fits
        }
        #endregion
    }

    public static void Cancel()
    {
        #region Example:Files.Cancel
        // A token stops a long read or save between two blocks of data: give up after 30 seconds, or when the user asks.
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var file = FitsFile.Open("m42.fits");
        var image = (ImageHdu)file.Primary;
        try
        {
            float[] pixels = image.ReadPixels<float>(cancel.Token);
            ushort[] rows = image.ReadRegion<ushort>(origin: [0, 100], shape: [640, 16], cancel.Token);
            file.Save("m42-saved.fits", ChecksumPolicy.Auto, cancel.Token);
            Console.WriteLine($"{pixels.Length} pixels read, {rows.Length} in the rows, file saved");
        }
        catch (OperationCanceledException)
        {
            // A cancelled save leaves no file behind, and an existing target as it was.
            Console.WriteLine("Cancelled");
        }
        #endregion
    }

    public static void Async()
    {
        AsyncCore().GetAwaiter().GetResult();   // the examples are run one after the other by a synchronous runner
    }

    private static async Task AsyncCore()
    {
        #region Example:Files.Async
        // The file is read and written with asynchronous I/O; the conversion of the pixels runs between two reads or writes.
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var file = FitsFile.Open("m42.fits");
        var image = (ImageHdu)file.Primary;

        float[] pixels = await image.ReadPixelsAsync<float>(cancel.Token);
        ushort[] rows = await image.ReadRegionAsync<ushort>(origin: [0, 100], shape: [640, 16], cancel.Token);
        Console.WriteLine($"{pixels.Length} pixels, {rows.Length} in the rows");

        // The same guarantees as Save: a temporary file, no file left after a failure, and CreateNew never replaces a file.
        await file.SaveAsync("m42-async.fits", ChecksumPolicy.Auto, FitsSaveMode.CreateNew, cancel.Token);
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
