# TableHdu Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → [`Hdu`](Eleris.FitsNet.Hdu.md) → `TableHdu`

Common behaviour of ASCII (`TABLE`) and binary (`BINTABLE`) tables.

```csharp
public abstract class TableHdu : Hdu
```

## Properties

| Member | Description |
| --- | --- |
| [`ColumnCount`](#eleris-fitsnet-tablehdu-columncount) | TFIELDS. |
| [`Columns`](#eleris-fitsnet-tablehdu-columns) | Column metadata read from the header ([`TableColumn.Data`](Eleris.FitsNet.TableColumn.md#eleris-fitsnet-tablecolumn-data) is null). |
| [`RowCount`](#eleris-fitsnet-tablehdu-rowcount) | NAXIS2. |
| [`RowWidth`](#eleris-fitsnet-tablehdu-rowwidth) | NAXIS1: bytes per row. |

## Methods

| Member | Description |
| --- | --- |
| [`AddColumn(TableColumn)`](#eleris-fitsnet-tablehdu-addcolumn-eleris-fitsnet-tablecolumn) | Appends a column (its data must have [`TableHdu.RowCount`](#eleris-fitsnet-tablehdu-rowcount) rows). |
| [`AppendRows(IReadOnlyList<TableColumn>)`](#eleris-fitsnet-tablehdu-appendrows-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn) | Appends the rows of `rows`, which must have the same columns as this table: same TFORM (ignoring the variable-length maximum), TSCAL, TZERO, TNULL and TDIM, and data of the same native type. |
| [`IndexOf(string)`](#eleris-fitsnet-tablehdu-indexof-system-string) | Index of the column whose TTYPE equals `name` (case-insensitive), or -1. |
| [`ReadColumns()`](#eleris-fitsnet-tablehdu-readcolumns) | Reads every column, data included. |
| [`ReadRaw(int)`](#eleris-fitsnet-tablehdu-readraw-system-int32) | The stored (unscaled) values of a column in the native representation described on [`TableColumn`](Eleris.FitsNet.TableColumn.md). |
| [`RemoveColumn(int)`](#eleris-fitsnet-tablehdu-removecolumn-system-int32) | Removes a column. |
| [`RemoveRows(long, long)`](#eleris-fitsnet-tablehdu-removerows-system-int64-system-int64) | Removes `count` rows starting at `start`. |
| [`SetColumns(IReadOnlyList<TableColumn>)`](#eleris-fitsnet-tablehdu-setcolumns-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn) | Replaces the whole table: columns, data and the structural keywords. |

## Property details

<a id="eleris-fitsnet-tablehdu-columncount"></a>
### `ColumnCount`

TFIELDS.

```csharp
public int ColumnCount { get; }
```

<a id="eleris-fitsnet-tablehdu-columns"></a>
### `Columns`

Column metadata read from the header ([`TableColumn.Data`](Eleris.FitsNet.TableColumn.md#eleris-fitsnet-tablecolumn-data) is null).

```csharp
public IReadOnlyList<TableColumn> Columns { get; }
```

<a id="eleris-fitsnet-tablehdu-rowcount"></a>
### `RowCount`

NAXIS2.

```csharp
public long RowCount { get; }
```

<a id="eleris-fitsnet-tablehdu-rowwidth"></a>
### `RowWidth`

NAXIS1: bytes per row.

```csharp
public long RowWidth { get; }
```

## Method details

<a id="eleris-fitsnet-tablehdu-addcolumn-eleris-fitsnet-tablecolumn"></a>
### `AddColumn(TableColumn)`

Appends a column (its data must have [`TableHdu.RowCount`](#eleris-fitsnet-tablehdu-rowcount) rows).

```csharp
public virtual void AddColumn(TableColumn column)
```

**Parameters**

- `column` — The column to add; its data must have one entry for each row of the table.

<a id="eleris-fitsnet-tablehdu-appendrows-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn"></a>
### `AppendRows(IReadOnlyList<TableColumn>)`

Appends the rows of `rows`, which must have the same columns as this table: same TFORM (ignoring the variable-length maximum), TSCAL, TZERO, TNULL and TDIM, and data of the same native type.

```csharp
public virtual void AppendRows(IReadOnlyList<TableColumn> rows)
```

**Parameters**

- `rows` — One column of data for each column of the table, in the same order, holding the rows to add.

<a id="eleris-fitsnet-tablehdu-indexof-system-string"></a>
### `IndexOf(string)`

Index of the column whose TTYPE equals `name` (case-insensitive), or -1.

```csharp
public int IndexOf(string name)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`); the match ignores case.

<a id="eleris-fitsnet-tablehdu-readcolumns"></a>
### `ReadColumns()`

Reads every column, data included.

```csharp
public IReadOnlyList<TableColumn> ReadColumns()
```

<a id="eleris-fitsnet-tablehdu-readraw-system-int32"></a>
### `ReadRaw(int)`

The stored (unscaled) values of a column in the native representation described on [`TableColumn`](Eleris.FitsNet.TableColumn.md).

```csharp
public Array ReadRaw(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="eleris-fitsnet-tablehdu-removecolumn-system-int32"></a>
### `RemoveColumn(int)`

Removes a column.

```csharp
public virtual void RemoveColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="eleris-fitsnet-tablehdu-removerows-system-int64-system-int64"></a>
### `RemoveRows(long, long)`

Removes `count` rows starting at `start`.

```csharp
public virtual void RemoveRows(long start, long count)
```

**Parameters**

- `start` — The zero-based index of the first row to remove.
- `count` — The number of rows to remove.

<a id="eleris-fitsnet-tablehdu-setcolumns-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn"></a>
### `SetColumns(IReadOnlyList<TableColumn>)`

Replaces the whole table: columns, data and the structural keywords. Other header cards are kept, except the per-column keywords (TTYPEn, TFORMn, TUNITn, TSCALn, TZEROn, TNULLn, TDISPn, TDIMn, TBCOLn, THEAP and the registered TCTYPn/TDMINn-style keywords carried by [`TableColumn.Extras`](Eleris.FitsNet.TableColumn.md#eleris-fitsnet-tablecolumn-extras)), which are regenerated from `columns`; comments on them are not kept. The caller's column objects are not modified. Heap data not referenced by any row (and any gap before the heap) is dropped, and unused bytes at the end of rows are removed.

```csharp
public virtual void SetColumns(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The new columns, each with its data; they must all have the same number of rows.

## Inherited members

- [`Hdu.VerifyChecksum()`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
