using BenchmarkDotNet.Attributes;
using FitsNet;

/// <summary>Reading, writing and cutting out a real 3536 x 3536 16-bit frame (25 MB).</summary>
[Config(typeof(FitsConfig))]
public class ImageIoBenchmarks
{
    private string _path = "";
    private string _checksummedPath = "";
    private byte[] _fileBytes = [];
    private FitsFile _inMemory = null!;
    private FitsFile _lazy = null!;
    private ushort[] _pixels = [];
    private ImageHdu _lazyImage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _path = BenchmarkData.Path(BenchmarkData.Broadband);
        _fileBytes = File.ReadAllBytes(_path);
        _inMemory = FitsFile.Read(new MemoryStream(_fileBytes));
        _lazy = FitsFile.Open(_path);
        _lazyImage = (ImageHdu)_lazy.Primary;
        _pixels = _lazyImage.ReadPixels<ushort>();

        _checksummedPath = BenchmarkData.TempFile();
        new FitsFile().Add(ImageHdu.Create<ushort>(_pixels, BenchmarkData.Size, BenchmarkData.Size)).Save(_checksummedPath, ChecksumPolicy.Always);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _lazy.Dispose();
        File.Delete(_checksummedPath);
    }

    // ---- reading ----------------------------------------------------------------------------------------------------

    [Benchmark(Description = "Open (header only)")]
    public int OpenOnly()
    {
        using var f = FitsFile.Open(_path);
        return f.Count;
    }

    [Benchmark(Description = "Open + ReadRawPixels<short>")]
    public int ReadRaw()
    {
        using var f = FitsFile.Open(_path);
        return ((ImageHdu)f.Primary).ReadRawPixels<short>().Length;
    }

    [Benchmark(Description = "Open + ReadPixels<ushort> (BZERO convention)")]
    public int ReadUnsigned()
    {
        using var f = FitsFile.Open(_path);
        return ((ImageHdu)f.Primary).ReadPixels<ushort>().Length;
    }

    [Benchmark(Description = "Open + ReadPixels<float>")]
    public int ReadFloat()
    {
        using var f = FitsFile.Open(_path);
        return ((ImageHdu)f.Primary).ReadPixels<float>().Length;
    }

    [Benchmark(Description = "Open + ReadPixels<double>")]
    public int ReadDouble()
    {
        using var f = FitsFile.Open(_path);
        return ((ImageHdu)f.Primary).ReadPixels<double>().Length;
    }

    [Benchmark(Description = "FitsFile.Read(Stream) whole file")]
    public int ReadWholeFile()
    {
        using var stream = new MemoryStream(_fileBytes, writable: false);
        return FitsFile.Read(stream).Count;
    }

    [Benchmark(Description = "ReadPixels<ushort> from memory")]
    public int ReadFromMemory() => ((ImageHdu)_inMemory.Primary).ReadPixels<ushort>().Length;

    // ---- regions ----------------------------------------------------------------------------------------------------

    [Benchmark(Description = "Cutout 256x256 (lazy file)")]
    public int Cutout() => _lazyImage.ReadRegion<ushort>([1500, 1500], [256, 256]).Length;

    [Benchmark(Description = "Cutout 16 x 3536 (a narrow column)")]
    public int NarrowColumn() => _lazyImage.ReadRegion<ushort>([1000, 0], [16, BenchmarkData.Size]).Length;

    [Benchmark(Description = "Cutout 3536 x 64 (64 whole rows)")]
    public int WholeRows() => _lazyImage.ReadRegion<ushort>([0, 2000], [BenchmarkData.Size, 64]).Length;

    // ---- writing ----------------------------------------------------------------------------------------------------

    [Benchmark(Description = "ImageHdu.Create<ushort>")]
    public ImageHdu CreateImage() => ImageHdu.Create<ushort>(_pixels, BenchmarkData.Size, BenchmarkData.Size);

    [Benchmark(Description = "Save to memory, no checksum")]
    public long SaveNoChecksum()
    {
        using var ms = new MemoryStream(_fileBytes.Length + 4096);
        _inMemory.Save(ms, ChecksumPolicy.Never);
        return ms.Length;
    }

    [Benchmark(Description = "Save to memory, CHECKSUM + DATASUM")]
    public long SaveWithChecksum()
    {
        using var ms = new MemoryStream(_fileBytes.Length + 4096);
        _inMemory.Save(ms, ChecksumPolicy.Always);
        return ms.Length;
    }

    [Benchmark(Description = "Save a lazily opened file to disk")]
    public long SaveLazyToDisk()
    {
        string target = BenchmarkData.TempFile();
        try
        {
            _lazy.Save(target, ChecksumPolicy.Never);
            return new FileInfo(target).Length;
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Benchmark(Description = "VerifyChecksums (lazy file)")]
    public int Verify()
    {
        using var f = FitsFile.Open(_checksummedPath);
        return f.VerifyChecksums().Count;
    }
}
