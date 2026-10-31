# BinaryTableHdu Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → [`Hdu`](FitsNet.Hdu.md) → [`TableHdu`](FitsNet.TableHdu.md) → `BinaryTableHdu`

A binary table extension (`XTENSION = 'BINTABLE'`), including variable-length arrays stored in the heap.

```csharp
public class BinaryTableHdu : TableHdu
```

## Methods

| Member | Description |
| --- | --- |
| [`Create(IReadOnlyList<TableColumn>)`](#fitsnet-binarytablehdu-create-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Creates a table from columns of equal length. |
| [`ReadBitColumn(int)`](#fitsnet-binarytablehdu-readbitcolumn-system-int32) | Reads an `X` column, one bool per bit. |
| [`ReadColumn<T>(int)`](#fitsnet-binarytablehdu-readcolumn-1-system-int32) | Reads a numeric column (flat, `repeat` values per row). |
| [`ReadColumn<T>(string)`](#fitsnet-binarytablehdu-readcolumn-1-system-string) | Reads a numeric column by name (see [`BinaryTableHdu.ReadColumn`](#fitsnet-binarytablehdu-readcolumn-1-system-int32)). |
| [`ReadLogicalColumn(int)`](#fitsnet-binarytablehdu-readlogicalcolumn-system-int32) | Reads an `L` column: true, false, or null for undefined. |
| [`ReadStringColumn(int)`](#fitsnet-binarytablehdu-readstringcolumn-system-int32) | Reads an `A` column (or a variable-length character column): one string per cell/row. |
| [`ReadStringColumn(string)`](#fitsnet-binarytablehdu-readstringcolumn-system-string) | Reads an `A` column by name (see [`BinaryTableHdu.ReadStringColumn`](#fitsnet-binarytablehdu-readstringcolumn-system-int32)). |
| [`ReadVariableBitColumn(int)`](#fitsnet-binarytablehdu-readvariablebitcolumn-system-int32) | Reads a variable-length column of bits (`PX` / `QX`): one array of bits per row. |
| [`ReadVariableColumn<T>(int)`](#fitsnet-binarytablehdu-readvariablecolumn-1-system-int32) | Reads a variable-length column (`P`/`Q`): one array per row, of the element type's native representation. |
| [`ReadVariableColumn<T>(string)`](#fitsnet-binarytablehdu-readvariablecolumn-1-system-string) | Reads a variable-length column by name (see [`BinaryTableHdu.ReadVariableColumn`](#fitsnet-binarytablehdu-readvariablecolumn-1-system-int32)). |
| [`ReadVariableLogicalColumn(int)`](#fitsnet-binarytablehdu-readvariablelogicalcolumn-system-int32) | Reads a variable-length column of logical values (`PL` / `QL`): one array per row, with null for an undefined value. |

## Method details

<a id="fitsnet-binarytablehdu-create-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `Create(IReadOnlyList<TableColumn>)`

Creates a table from columns of equal length.

```csharp
public static BinaryTableHdu Create(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The columns, each with its data; they must all have the same number of rows.

<a id="fitsnet-binarytablehdu-readbitcolumn-system-int32"></a>
### `ReadBitColumn(int)`

Reads an `X` column, one bool per bit.

```csharp
public bool[] ReadBitColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-binarytablehdu-readcolumn-1-system-int32"></a>
### `ReadColumn<T>(int)`

Reads a numeric column (flat, `repeat` values per row). Integer reads return the stored TNULL sentinel as an ordinary value; only `double`/`float` reads map it to NaN. `double`/`float` apply TSCAL/TZERO and turn TNULL into NaN; integer types work unscaled or with the unsigned conventions (`sbyte`, `ushort`, `uint`, `ulong`); [`Complex`](https://learn.microsoft.com/dotnet/api/system.numerics.complex) reads `C`/`M` columns.

```csharp
public T[] ReadColumn<T>(int index) where T : unmanaged
```

**Type parameters**

- `T` — The type the values are returned as, for example `short`, `int`, `double` or [`Complex`](https://learn.microsoft.com/dotnet/api/system.numerics.complex).

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-binarytablehdu-readcolumn-1-system-string"></a>
### `ReadColumn<T>(string)`

Reads a numeric column by name (see [`BinaryTableHdu.ReadColumn`](#fitsnet-binarytablehdu-readcolumn-1-system-int32)).

```csharp
public T[] ReadColumn<T>(string name) where T : unmanaged
```

**Type parameters**

- `T` — The type the values are returned as, for example `short`, `int`, `double` or [`Complex`](https://learn.microsoft.com/dotnet/api/system.numerics.complex).

**Parameters**

- `name` — The name of the column (`TTYPEn`); the match ignores case.

<a id="fitsnet-binarytablehdu-readlogicalcolumn-system-int32"></a>
### `ReadLogicalColumn(int)`

Reads an `L` column: true, false, or null for undefined.

```csharp
public bool?[] ReadLogicalColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-binarytablehdu-readstringcolumn-system-int32"></a>
### `ReadStringColumn(int)`

Reads an `A` column (or a variable-length character column): one string per cell/row.

```csharp
public string[] ReadStringColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-binarytablehdu-readstringcolumn-system-string"></a>
### `ReadStringColumn(string)`

Reads an `A` column by name (see [`BinaryTableHdu.ReadStringColumn`](#fitsnet-binarytablehdu-readstringcolumn-system-int32)).

```csharp
public string[] ReadStringColumn(string name)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`); the match ignores case.

<a id="fitsnet-binarytablehdu-readvariablebitcolumn-system-int32"></a>
### `ReadVariableBitColumn(int)`

Reads a variable-length column of bits (`PX` / `QX`): one array of bits per row.

```csharp
public bool[][] ReadVariableBitColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-binarytablehdu-readvariablecolumn-1-system-int32"></a>
### `ReadVariableColumn<T>(int)`

Reads a variable-length column (`P`/`Q`): one array per row, of the element type's native representation.

```csharp
public T[][] ReadVariableColumn<T>(int index) where T : unmanaged
```

**Type parameters**

- `T` — The type the values of each row are returned as, for example `short` or `double`.

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-binarytablehdu-readvariablecolumn-1-system-string"></a>
### `ReadVariableColumn<T>(string)`

Reads a variable-length column by name (see [`BinaryTableHdu.ReadVariableColumn`](#fitsnet-binarytablehdu-readvariablecolumn-1-system-int32)).

```csharp
public T[][] ReadVariableColumn<T>(string name) where T : unmanaged
```

**Type parameters**

- `T` — The type the values of each row are returned as, for example `short` or `double`.

**Parameters**

- `name` — The name of the column (`TTYPEn`); the match ignores case.

<a id="fitsnet-binarytablehdu-readvariablelogicalcolumn-system-int32"></a>
### `ReadVariableLogicalColumn(int)`

Reads a variable-length column of logical values (`PL` / `QL`): one array per row, with null for an undefined value.

```csharp
public bool?[][] ReadVariableLogicalColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

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
