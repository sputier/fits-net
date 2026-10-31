# AsciiTableHdu Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → [`Hdu`](FitsNet.Hdu.md) → [`TableHdu`](FitsNet.TableHdu.md) → `AsciiTableHdu`

An ASCII table extension (`XTENSION = 'TABLE'`). Columns hold string\[\] (A), long?\[\] (I) or double\[\] (F/E/D, NaN = null).

```csharp
public sealed class AsciiTableHdu : TableHdu
```

## Methods

| Member | Description |
| --- | --- |
| [`Create(IReadOnlyList<TableColumn>)`](#fitsnet-asciitablehdu-create-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Creates a table from columns of equal length (see the `CreateAscii*` factories of [`TableColumn`](FitsNet.TableColumn.md)). |
| [`ReadColumn<T>(int)`](#fitsnet-asciitablehdu-readcolumn-1-system-int32) | Reads a numeric column as `double`/`float` (TSCAL/TZERO applied, blank or TNULL fields are NaN). |
| [`ReadColumn<T>(string)`](#fitsnet-asciitablehdu-readcolumn-1-system-string) | Reads a numeric column by name (see [`AsciiTableHdu.ReadColumn`](#fitsnet-asciitablehdu-readcolumn-1-system-int32)). |
| [`ReadIntegerColumn(int)`](#fitsnet-asciitablehdu-readintegercolumn-system-int32) | Reads an `I` column; blank or TNULL fields are null. |
| [`ReadStringColumn(int)`](#fitsnet-asciitablehdu-readstringcolumn-system-int32) | Reads an `A` column. |
| [`ReadStringColumn(string)`](#fitsnet-asciitablehdu-readstringcolumn-system-string) | Reads an `A` column by name: one string per row, with the trailing blanks removed. |

## Method details

<a id="fitsnet-asciitablehdu-create-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `Create(IReadOnlyList<TableColumn>)`

Creates a table from columns of equal length (see the `CreateAscii*` factories of [`TableColumn`](FitsNet.TableColumn.md)).

```csharp
public static AsciiTableHdu Create(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The columns, each with its data; they must all have the same number of rows.

<a id="fitsnet-asciitablehdu-readcolumn-1-system-int32"></a>
### `ReadColumn<T>(int)`

Reads a numeric column as `double`/`float` (TSCAL/TZERO applied, blank or TNULL fields are NaN).

```csharp
public T[] ReadColumn<T>(int index) where T : unmanaged
```

**Type parameters**

- `T` — The type the values are returned as: `double` or `float`, or `long` for an unscaled `I` column; any other type throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception).

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-asciitablehdu-readcolumn-1-system-string"></a>
### `ReadColumn<T>(string)`

Reads a numeric column by name (see [`AsciiTableHdu.ReadColumn`](#fitsnet-asciitablehdu-readcolumn-1-system-int32)).

```csharp
public T[] ReadColumn<T>(string name) where T : unmanaged
```

**Type parameters**

- `T` — The type the values are returned as: `double` or `float`, or `long` for an unscaled `I` column; any other type throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception).

**Parameters**

- `name` — The name of the column (`TTYPEn`); the match ignores case.

<a id="fitsnet-asciitablehdu-readintegercolumn-system-int32"></a>
### `ReadIntegerColumn(int)`

Reads an `I` column; blank or TNULL fields are null.

```csharp
public long?[] ReadIntegerColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-asciitablehdu-readstringcolumn-system-int32"></a>
### `ReadStringColumn(int)`

Reads an `A` column.

```csharp
public string[] ReadStringColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-asciitablehdu-readstringcolumn-system-string"></a>
### `ReadStringColumn(string)`

Reads an `A` column by name: one string per row, with the trailing blanks removed.

```csharp
public string[] ReadStringColumn(string name)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`); the match ignores case.

## Inherited members

- [`TableHdu.IndexOf(string)`](FitsNet.TableHdu.md#fitsnet-tablehdu-indexof-system-string) — Index of the column whose TTYPE equals `name` (case-insensitive), or -1.
- [`TableHdu.ReadRaw(int)`](FitsNet.TableHdu.md#fitsnet-tablehdu-readraw-system-int32) — The stored (unscaled) values of a column in the native representation described on [`TableColumn`](FitsNet.TableColumn.md).
- [`TableHdu.ReadColumns()`](FitsNet.TableHdu.md#fitsnet-tablehdu-readcolumns) — Reads every column, data included.
- [`TableHdu.SetColumns(IReadOnlyList<TableColumn>)`](FitsNet.TableHdu.md#fitsnet-tablehdu-setcolumns-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) — Replaces the whole table: columns, data and the structural keywords.
- [`TableHdu.AddColumn(TableColumn)`](FitsNet.TableHdu.md#fitsnet-tablehdu-addcolumn-fitsnet-tablecolumn) — Appends a column (its data must have [`TableHdu.RowCount`](FitsNet.TableHdu.md#fitsnet-tablehdu-rowcount) rows).
- [`TableHdu.RemoveColumn(int)`](FitsNet.TableHdu.md#fitsnet-tablehdu-removecolumn-system-int32) — Removes a column.
- [`TableHdu.RemoveRows(long, long)`](FitsNet.TableHdu.md#fitsnet-tablehdu-removerows-system-int64-system-int64) — Removes `count` rows starting at `start`.
- [`TableHdu.AppendRows(IReadOnlyList<TableColumn>)`](FitsNet.TableHdu.md#fitsnet-tablehdu-appendrows-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) — Appends the rows of `rows`, which must have the same columns as this table: same TFORM (ignoring the variable-length maximum), TSCAL, TZERO, TNULL and TDIM, and data of the same native type.
- [`TableHdu.RowCount`](FitsNet.TableHdu.md#fitsnet-tablehdu-rowcount) — NAXIS2.
- [`TableHdu.RowWidth`](FitsNet.TableHdu.md#fitsnet-tablehdu-rowwidth) — NAXIS1: bytes per row.
- [`TableHdu.ColumnCount`](FitsNet.TableHdu.md#fitsnet-tablehdu-columncount) — TFIELDS.
- [`TableHdu.Columns`](FitsNet.TableHdu.md#fitsnet-tablehdu-columns) — Column metadata read from the header ([`TableColumn.Data`](FitsNet.TableColumn.md#fitsnet-tablecolumn-data) is null).
- [`Hdu.VerifyChecksum()`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](FitsNet.Hdu.md#fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](FitsNet.Hdu.md#fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
