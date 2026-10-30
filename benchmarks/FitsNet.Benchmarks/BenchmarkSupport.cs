using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;

/// <summary>Shared configuration: memory diagnoser, ratio against the baseline, tidy ordering.</summary>
public sealed class FitsConfig : ManualConfig
{
    public FitsConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.Median, RankColumn.Arabic);
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest, MethodOrderPolicy.Declared));
    }
}

/// <summary>Locates and loads the real images of the repository (<c>data/vespera</c>, raw frames of a Vaonis Vespera Pro 2).</summary>
public static class BenchmarkData
{
    public const string Broadband = "img-0022-M42-broadband.fits";
    public const string Pleiades = "img-0078-M45.fits";
    public const string Dualband = "img-1200-M42-dualband.fits";

    /// <summary>Width and height of the frames (3536 x 3536, 12 bit data stored as unsigned 16 bit).</summary>
    public const int Size = 3536;

    public static string Path(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, "data", "vespera", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"data/vespera/{name} was not found above {AppContext.BaseDirectory}");
    }

    /// <summary>A scratch file name that is removed by the caller.</summary>
    public static string TempFile(string extension = ".fits") =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fitsnet-bench-" + Guid.NewGuid().ToString("N") + extension);
}
