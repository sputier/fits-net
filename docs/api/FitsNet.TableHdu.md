# TableHdu Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → [`Hdu`](FitsNet.Hdu.md) → `TableHdu`

Common behaviour of ASCII (`TABLE`) and binary (`BINTABLE`) tables.

```csharp
public abstract class TableHdu : Hdu
```

## Properties

| Member | Description |
| --- | --- |
| [`ColumnCount`](#fitsnet-tablehdu-columncount) | TFIELDS. |
| [`Columns`](#fitsnet-tablehdu-columns) | Column metadata read from the header ([`TableColumn.Data`](FitsNet.TableColumn.md#fitsnet-tablecolumn-data) is null). |
| [`RowCount`](#fitsnet-tablehdu-rowcount) | NAXIS2. |
| [`RowWidth`](#fitsnet-tablehdu-rowwidth) | NAXIS1: bytes per row. |

## Methods

| Member | Description |
| --- | --- |
| [`AddColumn(TableColumn)`](#fitsnet-tablehdu-addcolumn-fitsnet-tablecolumn) | Appends a column (its data must have [`TableHdu.RowCount`](#fitsnet-tablehdu-rowcount) rows). |
| [`AppendRows(IReadOnlyList<TableColumn>)`](#fitsnet-tablehdu-appendrows-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Appends the rows of `rows`, which must have the same columns as this table: same TFORM (ignoring the variable-length maximum), TSCAL, TZERO, TNULL and TDIM, and data of the same native type. |
| [`IndexOf(string)`](#fitsnet-tablehdu-indexof-system-string) | Index of the column whose TTYPE equals `name` (case-insensitive), or -1. |
| [`ReadColumns()`](#fitsnet-tablehdu-readcolumns) | Reads every column, data included. |
| [`ReadRaw(int)`](#fitsnet-tablehdu-readraw-system-int32) | The stored (unscaled) values of a column in the native representation described on [`TableColumn`](FitsNet.TableColumn.md). |
| [`RemoveColumn(int)`](#fitsnet-tablehdu-removecolumn-system-int32) | Removes a column. |
| [`RemoveRows(long, long)`](#fitsnet-tablehdu-removerows-system-int64-system-int64) | Removes `count` rows starting at `start`. |
| [`SetColumns(IReadOnlyList<TableColumn>)`](#fitsnet-tablehdu-setcolumns-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Replaces the whole table: columns, data and the structural keywords. |

## Property details

<a id="fitsnet-tablehdu-columncount"></a>
### `ColumnCount`

TFIELDS.

```csharp
public int ColumnCount { get; }
```

<a id="fitsnet-tablehdu-columns"></a>
### `Columns`

Column metadata read from the header ([`TableColumn.Data`](FitsNet.TableColumn.md#fitsnet-tablecolumn-data) is null).

```csharp
public IReadOnlyList<TableColumn> Columns { get; }
```

<a id="fitsnet-tablehdu-rowcount"></a>
### `RowCount`

NAXIS2.

```csharp
public long RowCount { get; }
```

<a id="fitsnet-tablehdu-rowwidth"></a>
### `RowWidth`

NAXIS1: bytes per row.

```csharp
public long RowWidth { get; }
```

## Method details

<a id="fitsnet-tablehdu-addcolumn-fitsnet-tablecolumn"></a>
### `AddColumn(TableColumn)`

Appends a column (its data must have [`TableHdu.RowCount`](#fitsnet-tablehdu-rowcount) rows).

```csharp
public virtual void AddColumn(TableColumn column)
```

**Parameters**

- `column` — The column to add; its data must have one entry for each row of the table.

<a id="fitsnet-tablehdu-appendrows-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `AppendRows(IReadOnlyList<TableColumn>)`

Appends the rows of `rows`, which must have the same columns as this table: same TFORM (ignoring the variable-length maximum), TSCAL, TZERO, TNULL and TDIM, and data of the same native type.

```csharp
public virtual void AppendRows(IReadOnlyList<TableColumn> rows)
```

**Parameters**

- `rows` — One column of data for each column of the table, in the same order, holding the rows to add.

<a id="fitsnet-tablehdu-indexof-system-string"></a>
### `IndexOf(string)`

Index of the column whose TTYPE equals `name` (case-insensitive), or -1.

```csharp
public int IndexOf(string name)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`); the match ignores case.

<a id="fitsnet-tablehdu-readcolumns"></a>
### `ReadColumns()`

Reads every column, data included.

```csharp
public IReadOnlyList<TableColumn> ReadColumns()
```

<a id="fitsnet-tablehdu-readraw-system-int32"></a>
### `ReadRaw(int)`

The stored (unscaled) values of a column in the native representation described on [`TableColumn`](FitsNet.TableColumn.md).

```csharp
public Array ReadRaw(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-tablehdu-removecolumn-system-int32"></a>
### `RemoveColumn(int)`

Removes a column.

```csharp
public virtual void RemoveColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-tablehdu-removerows-system-int64-system-int64"></a>
### `RemoveRows(long, long)`

Removes `count` rows starting at `start`.

```csharp
public virtual void RemoveRows(long start, long count)
```

**Parameters**

- `start` — The zero-based index of the first row to remove.
- `count` — The number of rows to remove.

<a id="fitsnet-tablehdu-setcolumns-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `SetColumns(IReadOnlyList<TableColumn>)`

Replaces the whole table: columns, data and the structural keywords. Other header cards are kept, except the per-column keywords (TTYPEn, TFORMn, TUNITn, TSCALn, TZEROn, TNULLn, TDISPn, TDIMn, TBCOLn, THEAP and the registered TCTYPn/TDMINn-style keywords carried by [`TableColumn.Extras`](FitsNet.TableColumn.md#fitsnet-tablecolumn-extras)), which are regenerated from `columns`; comments on them are not kept. The caller's column objects are not modified. Heap data not referenced by any row (and any gap before the heap) is dropped, and unused bytes at the end of rows are removed.

```csharp
public virtual void SetColumns(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The new columns, each with its data; they must all have the same number of rows.

## Inherited members

- [`Hdu.VerifyChecksum()`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](FitsNet.Hdu.md#fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](FitsNet.Hdu.md#fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
