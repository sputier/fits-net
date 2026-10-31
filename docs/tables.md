# Tables

FITS has two kinds of tables.

| | [`BinaryTableHdu`](api/FitsNet.BinaryTableHdu.md) | [`AsciiTableHdu`](api/FitsNet.AsciiTableHdu.md) |
| --- | --- | --- |
| Extension | `BINTABLE` | `TABLE` |
| Values stored as | Binary | Text |
| Cell content | A value, or an array of values | A single value |
| Variable-length cells | Yes | No |
| Use it for | Nearly everything | Compatibility with old software |

Both derive from [`TableHdu`](api/FitsNet.TableHdu.md), which gives the common members: `RowCount`, `RowWidth`, `ColumnCount`, `Columns`, `IndexOf(name)`, `ReadColumns()`, `ReadRaw(index)`, `SetColumns`, `AddColumn`, `RemoveColumn`, `RemoveRows` and `AppendRows`. A column is described by a [`TableColumn`](api/FitsNet.TableColumn.md): its name, format, unit, scaling, dimensions and, when it carries data, the data.

## Create a binary table

Each column is a `TableColumn`. `TableColumn.Create<T>` chooses the FITS format from the type of the values; the other factories cover strings, logical values, bits, complex numbers and variable-length columns.

| Method | Column |
| --- | --- |
| `Create<T>(name, values, repeat, unit, dimensions)` | Numbers: `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double` |
| `CreateString(name, values, width, stringsPerRow)` | Fixed-width text (`A`) |
| `CreateLogical(name, values, repeat)` | `bool?` values (`L`); `null` is undefined |
| `CreateBits(name, bits, bitsPerRow)` | Bit arrays (`X`) |
| `CreateComplex(name, values, singlePrecision, repeat, unit)` | `Complex` values (`C` or `M`) |
| `CreateVariable<T>(name, rows, use64BitDescriptors, unit)` | A different number of numbers in each row (`P` or `Q`) |
| `CreateVariableString(name, rows, use64BitDescriptors)` | A string of any length in each row |

<!-- example: Tables.Create -->
```csharp
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
```
<!-- /example -->

## Read a binary table

`Columns` describes the columns without reading any data. The data of a column is read when you ask for it, and only that column is read from the file.

<!-- example: Tables.Read -->
```csharp
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
```
<!-- /example -->

Which method reads which column:

| Column format | Method | Result |
| --- | --- | --- |
| Numbers (`B`, `I`, `J`, `K`, `E`, `D`) | `ReadColumn<T>` | `T[]` |
| Complex (`C`, `M`) | `ReadColumn<Complex>` | `Complex[]` |
| Logical (`L`) | `ReadLogicalColumn` | `bool?[]` |
| Bits (`X`) | `ReadBitColumn` | `bool[]`, one per bit |
| Text (`A`) | `ReadStringColumn` | `string[]` |
| Variable-length numbers | `ReadVariableColumn<T>` | `T[][]`, one array per row |
| Variable-length logical, bits | `ReadVariableLogicalColumn`, `ReadVariableBitColumn` | `bool?[][]`, `bool[][]` |

Using the wrong method for a column throws an `InvalidOperationException` that names the right one.

A column with `repeat` values per cell is returned flat: the values of row 0, then the values of row 1, and so on. `Dimensions` (the `TDIM` keyword) gives the shape of one cell, with the first axis varying fastest.

<!-- example: Tables.Arrays -->
```csharp
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
```
<!-- /example -->

### Variable-length columns

A variable-length column keeps its values in a heap behind the table, so each row can have its own count, including zero.

<!-- example: Tables.Variable -->
```csharp
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
```
<!-- /example -->

### Scaling and undefined values

`Scale` (`TSCAL`), `Zero` (`TZERO`) and `Null` (`TNULL`) are properties of the column. `ReadColumn<double>` and `ReadColumn<float>` apply the scaling and turn the `TNULL` value into `NaN`. `ReadColumn<T>` with an integer type works when the column is of that type and is not scaled. A scaled column read as an integer throws an `InvalidOperationException`; use `double`, or `ReadRaw` to get the stored values.

<!-- example: Tables.Scaling -->
```csharp
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
```
<!-- /example -->

### Unsigned columns

As in images, `sbyte`, `ushort`, `uint` and `ulong` are stored as the signed type with a `TZERO` offset, and read back as the unsigned type.

<!-- example: Tables.Unsigned -->
```csharp
// Like images, columns of ushort, uint, ulong and sbyte use the TZERO convention automatically.
var table = BinaryTableHdu.Create([TableColumn.Create<uint>("BIG", [0u, 3_000_000_000u, uint.MaxValue])]);
uint[] values = table.ReadColumn<uint>("BIG");
Console.WriteLine($"{table.Columns[0].Format} {values[2]}");     // 1J 4294967295
```
<!-- /example -->

## ASCII tables

The values of an ASCII table are written as text, so each column has a Fortran-style format (`A8`, `I10`, `F7.2`, `E15.7`...). The factories pick a width for you, or take it from the `width` and `format` arguments. Reading uses `ReadStringColumn`, `ReadIntegerColumn` (`long?[]`, `null` for an undefined value) and `ReadColumn<double>`.

<!-- example: Tables.Ascii -->
```csharp
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
```
<!-- /example -->

## Edit a table

The editing methods keep the header and the other columns consistent. All of them keep the table valid: the column count, the row count and the structural keywords are updated for you.

<!-- example: Tables.Edit -->
```csharp
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
```
<!-- /example -->

To change values, read the columns, modify the arrays, and give them back with `SetColumns`. This rewrites the whole table, which is also how a table is rebuilt from scratch; the cards of the header that do not describe the table are kept.

## Compress a table

A binary table can be compressed by column, see [Compression](compression.md#compress-a-table).
