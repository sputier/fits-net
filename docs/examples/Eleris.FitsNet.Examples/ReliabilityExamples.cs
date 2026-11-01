namespace Eleris.FitsNet.Examples;

public static class ReliabilityExamples
{
    public static void Errors()
    {
        #region Example:Reliability.Errors
        // A file that is not FITS, damaged or hostile fails with a FitsException, never with an index or memory error.
        byte[] garbage = new byte[5760];
        try
        {
            FitsFile.Read(new MemoryStream(garbage));
        }
        catch (FitsException e)
        {
            Console.WriteLine($"Rejected: {e.Message}");
        }

        // Mistakes of the calling code are the usual .NET exceptions.
        try
        {
            var image = ImageHdu.Create<short>(new short[4], 2, 2);
            image.ReadRegion<short>(origin: [0, 0], shape: [3, 3]);     // larger than the image
        }
        catch (ArgumentException e)
        {
            Console.WriteLine($"Refused: {e.GetType().Name}");
        }
        #endregion
    }

    public static void TruncatedFile()
    {
        #region Example:Reliability.Truncated
        byte[] bytes = File.ReadAllBytes("m42.fits");
        byte[] half = bytes[..(bytes.Length / 2)];

        // The header says how much data follows: a file that stops earlier is reported, not silently shortened.
        try
        {
            FitsFile.Read(new MemoryStream(half));
        }
        catch (FitsException e)
        {
            Console.WriteLine(e.Message);
        }
        #endregion
    }

    public static void Dispose()
    {
        #region Example:Reliability.Dispose
        // Open keeps the file open for lazy reading: dispose it. After Dispose the HDUs cannot read their data any more.
        ImageHdu image;
        using (var file = FitsFile.Open("m42.fits"))
        {
            image = (ImageHdu)file.Primary;
            Console.WriteLine(image.ReadPixels<ushort>().Length);       // fine: the file is open
        }

        // Read has no such constraint: it loads everything in memory and the stream is yours.
        using var loaded = FitsFile.Read(new MemoryStream(File.ReadAllBytes("m42.fits")));
        #endregion
    }

    public static void FailedSave()
    {
        #region Example:Reliability.FailedSave
        // A failed save leaves the file and the HDUs as they were: a Save to a path writes a temporary file first.
        var image = ImageHdu.Create<short>(new short[4], 2, 2);
        var file = new FitsFile().Add(image);
        try
        {
            file.Save(Path.Combine("no-such-directory", "out.fits"));
        }
        catch (IOException e)
        {
            Console.WriteLine($"{e.GetType().Name}: nothing was written, header still has {image.Header.Count} cards");
        }
        #endregion
    }
}
