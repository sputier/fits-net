using BenchmarkDotNet.Attributes;
using Eleris.FitsNet;

/// <summary>Header parsing and formatting: the real camera header, and a large header with long strings.</summary>
[Config(typeof(FitsConfig))]
public class HeaderBenchmarks
{
    private byte[] _cameraHeader = [];
    private byte[] _bigHeader = [];
    private FitsHeader _camera = null!;
    private FitsHeader _big = null!;

    [GlobalSetup]
    public void Setup()
    {
        using var stream = File.OpenRead(BenchmarkData.Path(BenchmarkData.Dualband));
        _cameraHeader = new byte[2880];
        stream.ReadExactly(_cameraHeader);
        _camera = FitsHeader.Read(new MemoryStream(_cameraHeader));

        _big = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 16).Set("NAXIS", 0);
        for (int i = 0; i < 2000; i++)
        {
            _big.Set("KEY" + i, i * 0.5, "a comment for the card");
            if (i % 50 == 0) _big.Set("TXT" + i, new string('x', 200));
            if (i % 7 == 0) _big.AddHistory("history line number " + i);
        }
        _bigHeader = _big.ToBytes();
        Console.WriteLine($"// large header: {_big.Count} cards, {_bigHeader.Length / 2880} blocks");
    }

    [Benchmark(Description = "Parse the camera header (22 cards)")]
    public FitsHeader ParseCamera() => FitsHeader.Read(new MemoryStream(_cameraHeader));

    [Benchmark(Description = "Format the camera header")]
    public byte[] FormatCamera() => _camera.ToBytes();

    [Benchmark(Description = "Parse a 2000+ card header (tolerant)")]
    public FitsHeader ParseBig() => FitsHeader.Read(new MemoryStream(_bigHeader));

    [Benchmark(Description = "Parse a 2000+ card header (strict)")]
    public FitsHeader ParseBigStrict() => FitsHeader.Read(new MemoryStream(_bigHeader), new FitsReadOptions(Strict: true));

    [Benchmark(Description = "Format a 2000+ card header")]
    public byte[] FormatBig() => _big.ToBytes();

    [Benchmark(Description = "Look up 100 keywords")]
    public int Lookups()
    {
        int found = 0;
        for (int i = 0; i < 100; i++)
            if (_big.TryGetValue("KEY" + (i * 20), out double _)) found++;
        return found;
    }
}
