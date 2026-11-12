using BenchmarkDotNet.Attributes;
using Eleris.FitsNet;
using Eleris.FitsNet.Compression;

/// <summary>
/// Tile compression and decompression of a real 12-bit frame stored as 16 bits, for every algorithm, one core and all cores.
/// The compression ratio of each algorithm is printed once at the start of the run.
/// </summary>
[Config(typeof(FitsConfig))]
public class CompressionBenchmarks
{
    private ImageHdu _image = null!;
    private CompressedImageHdu _compressed = null!;

    [Params(CompressionAlgorithm.Rice, CompressionAlgorithm.Gzip1, CompressionAlgorithm.Gzip2, CompressionAlgorithm.HCompress, CompressionAlgorithm.NoCompress)]
    public CompressionAlgorithm Algorithm { get; set; }

    /// <summary>1 = sequential, -1 = one tile per core at a time.</summary>
    [Params(1, -1)]
    public int Parallelism { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using var f = FitsFile.Open(BenchmarkData.Path(BenchmarkData.Broadband));
        var original = (ImageHdu)f.Primary;
        _image = ImageHdu.Create<ushort>(original.ReadPixels<ushort>(), BenchmarkData.Size, BenchmarkData.Size);
        _compressed = _image.Compress(Options());
        if (Parallelism == 1)
        {
            double ratio = (double)BenchmarkData.Size * BenchmarkData.Size * 2 / _compressed.DataLength;
            Console.WriteLine($"// {Algorithm}: compression ratio {ratio:F2}:1 ({_compressed.DataLength / 1e6:F1} MB)");
        }
    }

    private CompressionOptions Options() => new() { Algorithm = Algorithm, MaxDegreeOfParallelism = Parallelism };

    [Benchmark(Description = "Compress")]
    public long Compress() => _image.Compress(Options()).DataLength;

    [Benchmark(Description = "Decompress to ushort")]
    public int Decompress() => _compressed.Decompress(Parallelism).ReadPixels<ushort>().Length;
}

/// <summary>Floating point images: quantization with dithering followed by Rice or HCOMPRESS, and the lossless gzip alternative.</summary>
[Config(typeof(FitsConfig))]
public class FloatCompressionBenchmarks
{
    private float[] _pixels = [];
    private CompressedImageHdu _compressed = null!;

    [Params(QuantizationMethod.NoDither, QuantizationMethod.SubtractiveDither1, QuantizationMethod.SubtractiveDither2, QuantizationMethod.None)]
    public QuantizationMethod Quantization { get; set; }

    [Params(1, -1)]
    public int Parallelism { get; set; }

    private CompressionOptions Options() => new()
    {
        Algorithm = Quantization == QuantizationMethod.None ? CompressionAlgorithm.Gzip2 : CompressionAlgorithm.Rice,
        Quantization = Quantization,
        MaxDegreeOfParallelism = Parallelism,
        TileShape = [BenchmarkData.Size, 16],
    };

    [GlobalSetup]
    public void Setup()
    {
        using var f = FitsFile.Open(BenchmarkData.Path(BenchmarkData.Dualband));
        _pixels = ((ImageHdu)f.Primary).ReadPixels<float>();
        _compressed = CompressedImageHdu.Create<float>(_pixels, [BenchmarkData.Size, BenchmarkData.Size], Options());
        if (Parallelism == 1)
            Console.WriteLine($"// float {Quantization}: ratio {(double)_pixels.Length * 4 / _compressed.DataLength:F2}:1");
    }

    [Benchmark(Description = "Compress float32")]
    public long Compress() => CompressedImageHdu.Create<float>(_pixels, [BenchmarkData.Size, BenchmarkData.Size], Options()).DataLength;

    [Benchmark(Description = "Decompress float32")]
    public int Decompress() => _compressed.Decompress(Parallelism).ReadPixels<float>().Length;
}

/// <summary>
/// The zlib level of GZIP_1 and GZIP_2 (<see cref="CompressionOptions.GzipLevel"/>): the default of the runtime, which compresses best,
/// and level 2, which gives files about as large as cfitsio's. The size of the data is printed once at the start of the run.
/// </summary>
[Config(typeof(FitsConfig))]
public class GzipLevelBenchmarks
{
    private ImageHdu _image = null!;

    [Params(CompressionAlgorithm.Gzip1, CompressionAlgorithm.Gzip2)]
    public CompressionAlgorithm Algorithm { get; set; }

    /// <summary>-1 is the default level.</summary>
    [Params(-1, 2)]
    public int Level { get; set; }

    /// <summary>1 = sequential, -1 = one tile per core at a time.</summary>
    [Params(1, -1)]
    public int Parallelism { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using var f = FitsFile.Open(BenchmarkData.Path(BenchmarkData.Broadband));
        var original = (ImageHdu)f.Primary;
        _image = ImageHdu.Create<ushort>(original.ReadPixels<ushort>(), BenchmarkData.Size, BenchmarkData.Size);
        if (Parallelism == 1) Console.WriteLine($"// {Algorithm}, level {Level}: {Compress() / 1e6:F2} MB");
    }

    [Benchmark(Description = "Compress")]
    public long Compress() => _image.Compress(new CompressionOptions { Algorithm = Algorithm, GzipLevel = Level, MaxDegreeOfParallelism = Parallelism }).DataLength;
}

/// <summary>PLIO on a mask (long runs of equal values), the case the algorithm was made for.</summary>
[Config(typeof(FitsConfig))]
public class PlioBenchmarks
{
    private int[] _mask = [];
    private CompressedImageHdu _compressed = null!;

    [GlobalSetup]
    public void Setup()
    {
        const int n = BenchmarkData.Size;
        _mask = new int[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int dx = x - n / 2, dy = y - n / 2;
                _mask[y * n + x] = dx * dx + dy * dy < 1_000_000 ? 1 : dx * dx + dy * dy < 2_000_000 ? 7 : 0;
            }
        _compressed = CompressedImageHdu.Create<int>(_mask, [n, n], new CompressionOptions { Algorithm = CompressionAlgorithm.Plio });
        Console.WriteLine($"// PLIO mask: ratio {(double)_mask.Length * 4 / _compressed.DataLength:F1}:1");
    }

    [Benchmark(Description = "PLIO compress mask")]
    public long Compress() => CompressedImageHdu.Create<int>(_mask, [BenchmarkData.Size, BenchmarkData.Size], new CompressionOptions { Algorithm = CompressionAlgorithm.Plio }).DataLength;

    [Benchmark(Description = "PLIO decompress mask")]
    public int Decompress() => _compressed.ReadPixels<int>().Length;
}
