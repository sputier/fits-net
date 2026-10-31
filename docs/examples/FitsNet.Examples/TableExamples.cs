using System.Numerics;

namespace FitsNet.Examples;

public static class TableExamples
{
    public static void CreateBinary()
    {
        #region Example:Tables.Create
        // One TableColumn per column; Create<T> picks the FITS format from the type: int -> J, double -> D, float -> E, short -> I...
        var table = BinaryTableHdu.Create(
        [
            TableColumn.Create<int>("ID", [1, 2, 3]),
            TableColumn.Create<double>("FLUX", [1.5, 2.5, 3.5], unit: "Jy"),
            TableColumn.CreateString("NAME", ["alpha", "beta", "gamma"], width: 8),
            TableColumn.CreateLogical("GOOD", [true, false, null]),     // null is an undefined logical value
        ]);
        table.Header.Set("EXTNAME", "SOURCES");

        new FitsFile().Add(table).Save("sources.fits");   // the file gets an empty primary HDU in front of the table
        #endregion
    }

    public static void ReadBinary()
    {
        #region Example:Tables.Read
        using var file = FitsFile.Open("sources.fits");
        var table = (BinaryTableHdu)file[1];

        Console.WriteLine($"{table.RowCount} rows of {table.RowWidth} bytes");
        foreach (TableColumn column in table.Columns)           // the description of each column, without its data
            Console.WriteLine($"{column.Name}: format {column.Format}, unit {column.Unit ?? "-"}");

        // Read by name or by zero-based index. T is the stored type, or double/float, which also apply the scaling.
        int[] id = table.ReadColumn<int>("ID");
        double[] flux = table.ReadColumn<double>(1);
        string[] name = table.ReadStringColumn("NAME");
        bool?[] good = table.ReadLogicalColumn(table.IndexOf("GOOD"));
        Console.WriteLine($"{id[0]} {flux[0]} {name[0]} {good[0]}");

        // ReadColumns reads everything in one pass: useful to copy or rewrite a table.
        foreach (TableColumn column in table.ReadColumns())
            Console.WriteLine($"{column.Name}: {column.Data!.Length} values");
        #endregion
    }

    public static void Matrix()
    {
        #region Example:Tables.Arrays
        // A cell can hold several values: repeat values per row, stored row after row. TDIM gives them a shape.
        var table = BinaryTableHdu.Create(
        [
            TableColumn.Create<float>("SPECTRUM", [1, 2, 3, 4, 5, 6], repeat: 3),            // 2 rows of 3 values
            TableColumn.Create<short>("STAMP", [1, 2, 3, 4, 5, 6, 7, 8], repeat: 4, dimensions: [2, 2]),
            TableColumn.CreateComplex("VISIBILITY", [new Complex(1, 2), new Complex(3, 4)]),
            TableColumn.CreateBits("FLAGS", [true, false, true, false, false, false, false, true, true, true, true, true, false, false, false, false], bitsPerRow: 8),
        ]);

        float[] spectra = table.ReadColumn<float>("SPECTRUM");     // 6 values: row 0 is [0..2], row 1 is [3..5]
        Complex[] visibilities = table.ReadColumn<Complex>("VISIBILITY");
        bool[] flags = table.ReadBitColumn(3);                     // one bool per bit: 16 values
        Console.WriteLine($"{spectra.Length} {visibilities[1]} {flags.Length}");
        #endregion
    }

    public static void Variable()
    {
        #region Example:Tables.Variable
        // A variable-length column stores a different number of values in each row (in the heap behind the table).
        var table = BinaryTableHdu.Create(
        [
            TableColumn.Create<int>("ID", [1, 2, 3]),
            TableColumn.CreateVariable<double>("TIMES", [[0.5, 1.5, 2.5], [], [9.0]]),
            TableColumn.CreateVariableString("COMMENT", ["first", "", "third one"]),
        ]);

        double[][] times = table.ReadVariableColumn<double>("TIMES");
        string[] comments = table.ReadStringColumn("COMMENT");
        Console.WriteLine($"{times[0].Length} {times[1].Length} {times[2].Length}   {comments[2]}");
        #endregion
    }

    public static void Ascii()
    {
        #region Example:Tables.Ascii
        // ASCII tables (TABLE extensions) store the values as text, so every column has a Fortran-style format.
        var table = AsciiTableHdu.Create(
        [
            TableColumn.CreateAsciiString("NAME", ["Vega", "Sirius", "Rigel"]),
            TableColumn.CreateAsciiInteger("NUMBER", [1, 2, null]),                    // null is written as an undefined value
            TableColumn.CreateAsciiFloat("MAG", [0.03, -1.46, 0.13], format: "F7.2"),
        ]);
        new FitsFile().Add(table).Save("stars.fits");

        using var file = FitsFile.Open("stars.fits");
        var back = (AsciiTableHdu)file[1];
        string[] names = back.ReadStringColumn("NAME");
        long?[] numbers = back.ReadIntegerColumn(1);
        double[] magnitude = back.ReadColumn<double>("MAG");
        Console.WriteLine($"{names[1]} {numbers[2]?.ToString() ?? "undefined"} {magnitude[1]}");
        #endregion
    }

    public static void Edit()
    {
        #region Example:Tables.Edit
        var table = BinaryTableHdu.Create([TableColumn.Create<int>("ID", [1, 2, 3, 4])]);

        table.AddColumn(TableColumn.Create<double>("VALUE", [0.1, 0.2, 0.3, 0.4]));     // one entry per row
        table.RemoveRows(start: 1, count: 2);                                           // rows 1 and 2 are removed
        table.AppendRows(
        [
            TableColumn.Create<int>("ID", [10, 11]),
            TableColumn.Create<double>("VALUE", [1.0, 1.1]),
        ]);
        table.RemoveColumn(0);

        Console.WriteLine($"{table.RowCount} rows, {table.ColumnCount} column");       // 4 rows, 1 column

        // SetColumns replaces the whole table, which is how a table is rebuilt from modified data.
        var columns = table.ReadColumns();
        columns[0].Data = ((double[])columns[0].Data!).Select(v => v * 2).ToArray();
        table.SetColumns(columns);
        #endregion
    }

    public static void Scaling()
    {
        #region Example:Tables.Scaling
        // TSCAL, TZERO and TNULL are properties of the column. Stored value * scale + zero = physical value.
        var column = TableColumn.Create<short>("COUNTS", [100, 200, -999, 400]);
        column.Scale = 0.5;
        column.Zero = 10.0;
        column.Null = -999L;          // TNULL: the stored value that means "undefined"
        var table = BinaryTableHdu.Create([column]);

        // A scaled column is read as double or float: the undefined value becomes NaN.
        double[] physical = table.ReadColumn<double>("COUNTS");   // 60 110 NaN 210

        // ReadRaw gives the stored values, as the native array of the column.
        short[] stored = (short[])table.ReadRaw(0);               // 100 200 -999 400
        Console.WriteLine($"{physical[0]} {double.IsNaN(physical[2])} {stored[2]}");
        #endregion
    }

    public static void UnsignedColumns()
    {
        #region Example:Tables.Unsigned
        // Like images, columns of ushort, uint, ulong and sbyte use the TZERO convention automatically.
        var table = BinaryTableHdu.Create([TableColumn.Create<uint>("BIG", [0u, 3_000_000_000u, uint.MaxValue])]);
        uint[] values = table.ReadColumn<uint>("BIG");
        Console.WriteLine($"{table.Columns[0].Format} {values[2]}");     // 1J 4294967295
        #endregion
    }
}
