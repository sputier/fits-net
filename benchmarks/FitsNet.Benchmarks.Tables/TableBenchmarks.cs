using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using FitsNet;

public sealed class TableConfig : ManualConfig
{
    public TableConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.Median);
    }
}

internal static class Temp
{
    public static string File(string extension = ".fits") =>
        Path.Combine(Path.GetTempPath(), "fitsnet-bench-" + Guid.NewGuid().ToString("N") + extension);
}

/// <summary>Binary tables of 10 000 to 2 million rows: building them, reading one column, reading all of them, from memory and from a file.</summary>
[Config(typeof(TableConfig))]
public class BinaryTableBenchmarks
{
    private TableColumn[] _columns = [];
    private BinaryTableHdu _table = null!;
    private string _path = "";
    private FitsFile _lazy = null!;
    private BinaryTableHdu _lazyTable = null!;

    [Params(10_000, 200_000, 2_000_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(7);
        _columns =
        [
            TableColumn.Create<int>("ID", Enumerable.Range(0, Rows).ToArray()),
            TableColumn.Create<double>("RA", Enumerable.Range(0, Rows).Select(_ => rng.NextDouble() * 360).ToArray()),
            TableColumn.Create<float>("FLUX", Enumerable.Range(0, Rows * 3).Select(_ => (float)rng.NextDouble()).ToArray(), repeat: 3),
            TableColumn.Create<short>("FLAGS", Enumerable.Range(0, Rows).Select(i => (short)(i % 1000)).ToArray()),
            TableColumn.CreateString("NAME", Enumerable.Range(0, Rows).Select(i => "src" + i % 100_000).ToArray(), 12),
            TableColumn.CreateLogical("OK", Enumerable.Range(0, Rows).Select(i => (bool?)(i % 3 != 0)).ToArray()),
        ];
        _table = BinaryTableHdu.Create(_columns);
        _path = Temp.File();
        new FitsFile().Add(_table).Save(_path);
        _lazy = FitsFile.Open(_path);
        _lazyTable = (BinaryTableHdu)_lazy[1];
        Console.WriteLine($"// {Rows:N0} rows x {_table.RowWidth} bytes = {_table.DataLength / 1e6:F1} MB");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _lazy.Dispose();
        File.Delete(_path);
    }

    [Benchmark(Description = "Create the table from column arrays")]
    public long Create() => BinaryTableHdu.Create(_columns).DataLength;

    [Benchmark(Description = "ReadColumn<int> (in memory)")]
    public int ReadInt() => _table.ReadColumn<int>("ID").Length;

    [Benchmark(Description = "ReadColumn<double> (in memory)")]
    public int ReadDouble() => _table.ReadColumn<double>("RA").Length;

    [Benchmark(Description = "ReadColumn<float> x3 (in memory)")]
    public int ReadFloats() => _table.ReadColumn<float>("FLUX").Length;

    [Benchmark(Description = "ReadStringColumn (in memory)")]
    public int ReadStrings() => _table.ReadStringColumn("NAME").Length;

    [Benchmark(Description = "ReadLogicalColumn (in memory)")]
    public int ReadLogical() => _table.ReadLogicalColumn(5).Length;

    [Benchmark(Description = "ReadColumns: every column (in memory)")]
    public int ReadAll() => _table.ReadColumns().Count;

    [Benchmark(Description = "ReadColumn<double> (lazy file)")]
    public int ReadDoubleFromFile() => _lazyTable.ReadColumn<double>("RA").Length;

    [Benchmark(Description = "ReadColumn<short> (lazy file)")]
    public int ReadShortFromFile() => _lazyTable.ReadColumn<short>("FLAGS").Length;

    [Benchmark(Description = "Save to a stream")]
    public long Save()
    {
        using var ms = new MemoryStream();
        new FitsFile().Add(_table).Save(ms, ChecksumPolicy.Never);
        return ms.Length;
    }

    [Benchmark(Description = "Save with CHECKSUM")]
    public long SaveChecksum()
    {
        using var ms = new MemoryStream();
        new FitsFile().Add(_table).Save(ms, ChecksumPolicy.Always);
        return ms.Length;
    }

    [Benchmark(Description = "Remove 1% of the rows")]
    public long RemoveRows()
    {
        var copy = BinaryTableHdu.Create(_columns);
        copy.RemoveRows(Rows / 3, Rows / 100);
        return copy.RowCount;
    }
}

/// <summary>Variable-length arrays (the heap), the costly case of binary tables.</summary>
[Config(typeof(TableConfig))]
public class VariableLengthBenchmarks
{
    private short[][] _rows = [];
    private BinaryTableHdu _table = null!;

    [Params(10_000, 200_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(3);
        _rows = Enumerable.Range(0, Rows).Select(_ => Enumerable.Range(0, rng.Next(0, 40)).Select(i => (short)i).ToArray()).ToArray();
        _table = BinaryTableHdu.Create([TableColumn.CreateVariable<short>("SPECTRUM", _rows)]);
        Console.WriteLine($"// {Rows:N0} rows, heap {_table.Header.Get<long>("PCOUNT") / 1e6:F1} MB");
    }

    [Benchmark(Description = "Create with a variable-length column")]
    public long Create() => BinaryTableHdu.Create([TableColumn.CreateVariable<short>("SPECTRUM", _rows)]).DataLength;

    [Benchmark(Description = "ReadVariableColumn<short>")]
    public int Read() => _table.ReadVariableColumn<short>("SPECTRUM").Length;
}

/// <summary>ASCII tables are text: formatting and parsing numbers dominates.</summary>
[Config(typeof(TableConfig))]
public class AsciiTableBenchmarks
{
    private AsciiTableHdu _table = null!;
    private TableColumn[] _columns = [];

    [Params(10_000, 200_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(5);
        _columns =
        [
            TableColumn.CreateAsciiInteger("ID", Enumerable.Range(0, Rows).Select(i => (long?)i).ToArray()),
            TableColumn.CreateAsciiFloat("X", Enumerable.Range(0, Rows).Select(_ => rng.NextDouble() * 1000).ToArray(), "F12.5"),
            TableColumn.CreateAsciiFloat("Y", Enumerable.Range(0, Rows).Select(_ => rng.NextDouble() - 0.5).ToArray(), "E16.8"),
            TableColumn.CreateAsciiString("NAME", Enumerable.Range(0, Rows).Select(i => "obj" + i % 5000).ToArray()),
        ];
        _table = AsciiTableHdu.Create(_columns);
    }

    [Benchmark(Description = "Create an ASCII table")]
    public long Create() => AsciiTableHdu.Create(_columns).DataLength;

    [Benchmark(Description = "Read a float column (F format)")]
    public int ReadF() => _table.ReadColumn<double>("X").Length;

    [Benchmark(Description = "Read a float column (E format)")]
    public int ReadE() => _table.ReadColumn<double>("Y").Length;

    [Benchmark(Description = "Read an integer column")]
    public int ReadI() => _table.ReadIntegerColumn(0).Length;

    [Benchmark(Description = "Read a string column")]
    public int ReadA() => _table.ReadStringColumn("NAME").Length;
}

/// <summary>Files that hold many extensions: opening, locating and copying them.</summary>
[Config(typeof(TableConfig))]
public class ManyExtensionsBenchmarks
{
    private string _path = "";

    [Params(10, 500)]
    public int Extensions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var file = new FitsFile();
        for (int i = 0; i < Extensions; i++)
        {
            var image = ImageHdu.Create<short>(Enumerable.Range(0, 64 * 64).Select(v => (short)(v + i)).ToArray(), 64, 64);
            image.Header.Set("EXTNAME", "FRAME" + i).Set("INDEX", i);
            file.Add(image);
        }
        _path = Temp.File();
        file.Save(_path);
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_path);

    [Benchmark(Description = "Open the file (all headers)")]
    public int Open()
    {
        using var f = FitsFile.Open(_path);
        return f.Count;
    }

    [Benchmark(Description = "Open and read the last extension")]
    public int ReadLast()
    {
        using var f = FitsFile.Open(_path);
        return ((ImageHdu)f[f.Count - 1]).ReadPixels<short>().Length;
    }

    [Benchmark(Description = "Open, edit one header, save over the file")]
    public int Edit()
    {
        string copy = Temp.File();
        File.Copy(_path, copy);
        try
        {
            using var f = FitsFile.Open(copy);
            f[f.Count / 2].Header.Set("EDITED", true);
            f.Save(copy, ChecksumPolicy.Never);
            return f.Count;
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
