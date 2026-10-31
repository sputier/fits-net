namespace FitsNet.Examples;

public static class CompressionExamples
{
    public static void CompressImage()
    {
        #region Example:Compression.Image
        using var source = FitsFile.Open("m42.fits");
        var image = (ImageHdu)source.Primary;

        // Rice is the default and the best choice for integer images from a camera: lossless and fast.
        CompressedImageHdu packed = image.Compress();
        Console.WriteLine($"{packed.Algorithm}, tiles of {string.Join(" x ", packed.TileShape)}");

        // Choose the algorithm and the tile shape (one row per tile by default).
        var options = new CompressionOptions
        {
            Algorithm = CompressionAlgorithm.Gzip2,
            TileShape = [160, 120],
        };
        CompressedImageHdu tiled = image.Compress(options);

        // The compressed image is an HDU like any other. The file needs a primary HDU in front of it.
        new FitsFile().Add(tiled).Save("m42-compressed.fits");
        #endregion
    }

    public static void ReadCompressed()
    {
        #region Example:Compression.Read
        using var file = FitsFile.Open("m42-compressed.fits");

        // Extensions are recognized by their keywords: a compressed image is a CompressedImageHdu.
        var packed = (CompressedImageHdu)file[1];
        Console.WriteLine($"{packed.BitPix}, {string.Join(" x ", packed.Axes)}");

        // ReadPixels decompresses the tiles in parallel (the result does not depend on the number of threads).
        ushort[] pixels = packed.ReadPixels<ushort>();

        // Decompress gives the ordinary image HDU back, with its header restored.
        ImageHdu image = packed.Decompress();
        Console.WriteLine($"{pixels.Length} pixels, {image.Header.Get<string>("OBJECT")}");
        #endregion
    }

    public static void FloatingPoint()
    {
        #region Example:Compression.Float
        var rng = new Random(1);
        var image = ImageHdu.Create<float>(Enumerable.Range(0, 128 * 128).Select(_ => 100f + (float)rng.NextDouble()).ToArray(), 128, 128);

        // Floating point pixels are quantized (turned into integers) before the compression, which loses
        // a controlled amount of precision: the step is the noise of the image divided by QuantizeLevel (here a quarter).
        var lossy = new CompressionOptions
        {
            Quantization = QuantizationMethod.SubtractiveDither2,
            QuantizeLevel = 4,
        };
        CompressedImageHdu packed = image.Compress(lossy);

        // To keep every bit, do not quantize: the pixels are stored with gzip.
        var lossless = new CompressionOptions
        {
            Algorithm = CompressionAlgorithm.Gzip2,
            Quantization = QuantizationMethod.None,
        };
        float[] exact = image.Compress(lossless).ReadPixels<float>();
        Console.WriteLine($"{packed.Algorithm} {exact[0] == image.ReadPixels<float>()[0]}");
        #endregion
    }

    public static void Parallelism()
    {
        #region Example:Compression.Parallelism
        using var file = FitsFile.Open("m42.fits");
        var image = (ImageHdu)file.Primary;

        // By default every core is used. 1 means one tile at a time.
        var options = new CompressionOptions { MaxDegreeOfParallelism = 2 };
        CompressedImageHdu packed = image.Compress(options);
        ImageHdu back = packed.Decompress(maxDegreeOfParallelism: 2);
        Console.WriteLine(back.PixelCount);
        #endregion
    }

    public static void CompressTable()
    {
        #region Example:Compression.Table
        using var file = FitsFile.Open("m42.fits");
        var catalog = (BinaryTableHdu)file[1];

        // Each column of a table is compressed on its own; the library picks Rice for integers, gzip for the others.
        CompressedTableHdu packed = CompressedTableHdu.Compress(catalog);
        Console.WriteLine(string.Join(", ", packed.ColumnAlgorithms));

        // Or choose: here every column with gzip, and 1000 rows in each tile.
        var options = new CompressedTableOptions { Algorithm = CompressionAlgorithm.Gzip2, TileRows = 1000 };
        new FitsFile().Add(CompressedTableHdu.Compress(catalog, options)).Save("catalog-compressed.fits");

        // Decompress gives the original table back, with its header.
        using var again = FitsFile.Open("catalog-compressed.fits");
        BinaryTableHdu table = ((CompressedTableHdu)again[1]).Decompress();
        Console.WriteLine($"{table.RowCount} rows, {table.Header.Get<string>("EXTNAME")}");
        #endregion
    }
}
