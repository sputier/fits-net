using BenchmarkDotNet.Attributes;
using FitsNet;
using FitsNet.Wcs;

/// <summary>
/// Coordinate transformations of a whole image: every pixel of a 3536 x 3536 frame (the size of the camera images) through the
/// pixel to world transformation, for projections of different cost.
/// </summary>
[Config(typeof(FitsConfig))]
public class WcsBenchmarks
{
    private const int Size = BenchmarkData.Size;

    private WorldCoordinateSystem _wcs = null!;
    private double[] _row = [];

    [Params("TAN", "SIN", "ZEA", "MOL", "HPX", "COE", "CSC", "SZP", "AIR")]
    public string Projection { get; set; } = "TAN";

    [GlobalSetup]
    public void Setup()
    {
        var h = new FitsHeader();
        h.Set("CTYPE1", "RA---" + Projection).Set("CTYPE2", "DEC--" + Projection).Set("CRPIX1", Size / 2.0).Set("CRPIX2", Size / 2.0)
            .Set("CRVAL1", 83.8).Set("CRVAL2", -5.4).Set("CDELT1", -0.0005).Set("CDELT2", 0.0005);
        if (Projection == "COE") h.Set("PV2_1", 40.0).Set("PV2_2", 10.0);
        if (Projection == "SZP") h.Set("PV2_1", 0.5);
        _wcs = WorldCoordinateSystem.FromHeader(h);
        _row = new double[2];
    }

    [Benchmark(Description = "One row of 3536 pixels to world")]
    public double Row()
    {
        double sum = 0;
        for (int x = 1; x <= Size; x++)
        {
            _row[0] = x;
            _row[1] = Size / 2.0;
            sum += _wcs.PixelToWorld(_row)[0];
        }
        return sum;
    }

    [Benchmark(Description = "One row of 3536 positions back to pixels")]
    public double Back()
    {
        double sum = 0;
        var world = new double[] { 83.8, -5.4 };
        for (int x = 0; x < Size; x++)
        {
            world[0] = 83.8 + (x - Size / 2) * 0.0005;
            sum += _wcs.WorldToPixel(world)[0];
        }
        return sum;
    }
}

/// <summary>Time values: parsing, formatting and converting between scales.</summary>
[Config(typeof(FitsConfig))]
public class TimeBenchmarks
{
    private const string Text = "2016-12-31T23:59:60.500000000";

    private FitsTime _utc;

    [GlobalSetup]
    public void Setup() => _utc = FitsTime.Parse("2020-06-15T12:34:56.789", TimeScale.UTC);

    [Benchmark(Description = "Parse an ISO-8601 datetime")]
    public FitsTime Parse() => FitsTime.Parse(Text, TimeScale.UTC);

    [Benchmark(Description = "Format an ISO-8601 datetime")]
    public string Format() => _utc.ToIsoString(9);

    [Benchmark(Description = "UTC to TT")]
    public FitsTime UtcToTt() => _utc.To(TimeScale.TT);

    [Benchmark(Description = "UTC to TCG")]
    public FitsTime UtcToTcg() => _utc.To(TimeScale.TCG);
}
