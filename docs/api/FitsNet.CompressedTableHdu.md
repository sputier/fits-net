# CompressedTableHdu Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → [`Hdu`](FitsNet.Hdu.md) → [`TableHdu`](FitsNet.TableHdu.md) → [`BinaryTableHdu`](FitsNet.BinaryTableHdu.md) → `CompressedTableHdu`

A tile-compressed binary table (standard section 10.3): a binary table with `ZTABLE = T` whose rows hold, for each tile of rows of the original table, one compressed byte stream per column. It is a [`BinaryTableHdu`](FitsNet.BinaryTableHdu.md), so the table API still describes the stored form; [`CompressedTableHdu.Decompress`](#fitsnet-compressedtablehdu-decompress-system-int32) rebuilds the original table and [`CompressedTableHdu.Compress`](#fitsnet-compressedtablehdu-compress-fitsnet-binarytablehdu-fitsnet-compression-compressedtableoptions) builds this kind of table from an ordinary one. Fixed and variable-length columns are supported with `RICE_1`, `GZIP_1`, `GZIP_2` and `NOCOMPRESS`.

```csharp
public sealed class CompressedTableHdu : BinaryTableHdu
```

## Properties

| Member | Description |
| --- | --- |
| [`ColumnAlgorithms`](#fitsnet-compressedtablehdu-columnalgorithms) | The compression algorithm of each column (ZCTYPn), e.g. |
| [`TileRows`](#fitsnet-compressedtablehdu-tilerows) | Number of rows of the original table in each tile (ZTILELEN). |
| [`UncompressedRowCount`](#fitsnet-compressedtablehdu-uncompressedrowcount) | Number of rows of the original table (ZNAXIS2). |
| [`UncompressedRowWidth`](#fitsnet-compressedtablehdu-uncompressedrowwidth) | Width in bytes of a row of the original table (ZNAXIS1). |

## Methods

| Member | Description |
| --- | --- |
| [`AddColumn(TableColumn)`](#fitsnet-compressedtablehdu-addcolumn-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`AppendRows(IReadOnlyList<TableColumn>)`](#fitsnet-compressedtablehdu-appendrows-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`Compress(BinaryTableHdu, CompressedTableOptions)`](#fitsnet-compressedtablehdu-compress-fitsnet-binarytablehdu-fitsnet-compression-compressedtableoptions) | Compresses a table. |
| [`Create(IReadOnlyList<TableColumn>)`](#fitsnet-compressedtablehdu-create-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Not available: use [`CompressedTableHdu.Compress`](#fitsnet-compressedtablehdu-compress-fitsnet-binarytablehdu-fitsnet-compression-compressedtableoptions) to build a compressed table. |
| [`Decompress(int)`](#fitsnet-compressedtablehdu-decompress-system-int32) | Rebuilds the original table: tiles are decompressed in parallel, every header card that describes the table rather than its compressed form is restored (NAXIS1, NAXIS2, PCOUNT, TFORMn, THEAP) and the Z\* keywords are removed. |
| [`IsCompressedTable(FitsHeader)`](#fitsnet-compressedtablehdu-iscompressedtable-fitsnet-fitsheader) | Whether a binary table header describes a tile-compressed table. |
| [`RemoveColumn(int)`](#fitsnet-compressedtablehdu-removecolumn-system-int32) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`RemoveRows(long, long)`](#fitsnet-compressedtablehdu-removerows-system-int64-system-int64) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`SetColumns(IReadOnlyList<TableColumn>)`](#fitsnet-compressedtablehdu-setcolumns-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |

## Property details

<a id="fitsnet-compressedtablehdu-columnalgorithms"></a>
### `ColumnAlgorithms`

The compression algorithm of each column (ZCTYPn), e.g. `RICE_1`.

```csharp
public IReadOnlyList<string> ColumnAlgorithms { get; }
```

<a id="fitsnet-compressedtablehdu-tilerows"></a>
### `TileRows`

Number of rows of the original table in each tile (ZTILELEN).

```csharp
public long TileRows { get; }
```

<a id="fitsnet-compressedtablehdu-uncompressedrowcount"></a>
### `UncompressedRowCount`

Number of rows of the original table (ZNAXIS2).

```csharp
public long UncompressedRowCount { get; }
```

<a id="fitsnet-compressedtablehdu-uncompressedrowwidth"></a>
### `UncompressedRowWidth`

Width in bytes of a row of the original table (ZNAXIS1).

```csharp
public long UncompressedRowWidth { get; }
```

## Method details

<a id="fitsnet-compressedtablehdu-addcolumn-fitsnet-tablecolumn"></a>
### `AddColumn(TableColumn)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void AddColumn(TableColumn column)
```

**Parameters**

- `column` — The column to add; its data must have one entry for each row of the table.

<a id="fitsnet-compressedtablehdu-appendrows-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `AppendRows(IReadOnlyList<TableColumn>)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void AppendRows(IReadOnlyList<TableColumn> rows)
```

**Parameters**

- `rows` — One column of data for each column of the table, in the same order, holding the rows to add.

<a id="fitsnet-compressedtablehdu-compress-fitsnet-binarytablehdu-fitsnet-compression-compressedtableoptions"></a>
### `Compress(BinaryTableHdu, CompressedTableOptions)`

Compresses a table. The table is left untouched; its header cards are copied, except the structural ones that now describe the compressed table (NAXIS1, NAXIS2, PCOUNT, TFORMn, THEAP, CHECKSUM, DATASUM), whose original values go to the Z\* keywords. The compression directives FZTILELN, FZALGOR and FZALGn of the header are obeyed when `options` says nothing.

```csharp
public static CompressedTableHdu Compress(BinaryTableHdu table, CompressedTableOptions? options = null)
```

**Parameters**

- `table` — The table to compress; it is not modified.
- `options` — The compression options, or null for the automatic choice of algorithm and tile size.

**Exceptions**

- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The rows have bytes that belong to no column (they would be lost).

<a id="fitsnet-compressedtablehdu-create-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `Create(IReadOnlyList<TableColumn>)`

Not available: use [`CompressedTableHdu.Compress`](#fitsnet-compressedtablehdu-compress-fitsnet-binarytablehdu-fitsnet-compression-compressedtableoptions) to build a compressed table.

```csharp
public static BinaryTableHdu Create(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The columns, each with its data; they must all have the same number of rows.

<a id="fitsnet-compressedtablehdu-decompress-system-int32"></a>
### `Decompress(int)`

Rebuilds the original table: tiles are decompressed in parallel, every header card that describes the table rather than its compressed form is restored (NAXIS1, NAXIS2, PCOUNT, TFORMn, THEAP) and the Z\* keywords are removed.

```csharp
public BinaryTableHdu Decompress(int maxDegreeOfParallelism = -1)
```

**Parameters**

- `maxDegreeOfParallelism` — Tiles decoded at the same time (default: one per processor core).

<a id="fitsnet-compressedtablehdu-iscompressedtable-fitsnet-fitsheader"></a>
### `IsCompressedTable(FitsHeader)`

Whether a binary table header describes a tile-compressed table.

```csharp
public static bool IsCompressedTable(FitsHeader header)
```

**Parameters**

- `header` — The header of a binary table extension.

<a id="fitsnet-compressedtablehdu-removecolumn-system-int32"></a>
### `RemoveColumn(int)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void RemoveColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-compressedtablehdu-removerows-system-int64-system-int64"></a>
### `RemoveRows(long, long)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void RemoveRows(long start, long count)
```

**Parameters**

- `start` — The zero-based index of the first row to remove.
- `count` — The number of rows to remove.

<a id="fitsnet-compressedtablehdu-setcolumns-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `SetColumns(IReadOnlyList<TableColumn>)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void SetColumns(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The new columns, each with its data; they must all have the same number of rows.

## Inherited members

- [`BinaryTableHdu.ReadColumn<T>(int)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readcolumn-1-system-int32) — Reads a numeric column (flat, `repeat` values per row).
- [`BinaryTableHdu.ReadColumn<T>(string)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readcolumn-1-system-string) — Reads a numeric column by name (see [`BinaryTableHdu.ReadColumn`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readcolumn-1-system-int32)).
- [`BinaryTableHdu.ReadLogicalColumn(int)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readlogicalcolumn-system-int32) — Reads an `L` column: true, false, or null for undefined.
- [`BinaryTableHdu.ReadBitColumn(int)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readbitcolumn-system-int32) — Reads an `X` column, one bool per bit.
- [`BinaryTableHdu.ReadStringColumn(int)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readstringcolumn-system-int32) — Reads an `A` column (or a variable-length character column): one string per cell/row.
- [`BinaryTableHdu.ReadStringColumn(string)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readstringcolumn-system-string) — Reads an `A` column by name (see [`BinaryTableHdu.ReadStringColumn`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readstringcolumn-system-int32)).
- [`BinaryTableHdu.ReadVariableColumn<T>(int)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readvariablecolumn-1-system-int32) — Reads a variable-length column (`P`/`Q`): one array per row, of the element type's native representation.
- [`BinaryTableHdu.ReadVariableColumn<T>(string)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readvariablecolumn-1-system-string) — Reads a variable-length column by name (see [`BinaryTableHdu.ReadVariableColumn`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readvariablecolumn-1-system-int32)).
- [`BinaryTableHdu.ReadVariableLogicalColumn(int)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readvariablelogicalcolumn-system-int32) — Reads a variable-length column of logical values (`PL` / `QL`): one array per row, with null for an undefined value.
- [`BinaryTableHdu.ReadVariableBitColumn(int)`](FitsNet.BinaryTableHdu.md#fitsnet-binarytablehdu-readvariablebitcolumn-system-int32) — Reads a variable-length column of bits (`PX` / `QX`): one array of bits per row.
- [`TableHdu.IndexOf(string)`](FitsNet.TableHdu.md#fitsnet-tablehdu-indexof-system-string) — Index of the column whose TTYPE equals `name` (case-insensitive), or -1.
- [`TableHdu.ReadRaw(int)`](FitsNet.TableHdu.md#fitsnet-tablehdu-readraw-system-int32) — The stored (unscaled) values of a column in the native representation described on [`TableColumn`](FitsNet.TableColumn.md).
- [`TableHdu.ReadColumns()`](FitsNet.TableHdu.md#fitsnet-tablehdu-readcolumns) — Reads every column, data included.
- [`TableHdu.RowCount`](FitsNet.TableHdu.md#fitsnet-tablehdu-rowcount) — NAXIS2.
- [`TableHdu.RowWidth`](FitsNet.TableHdu.md#fitsnet-tablehdu-rowwidth) — NAXIS1: bytes per row.
- [`TableHdu.ColumnCount`](FitsNet.TableHdu.md#fitsnet-tablehdu-columncount) — TFIELDS.
- [`TableHdu.Columns`](FitsNet.TableHdu.md#fitsnet-tablehdu-columns) — Column metadata read from the header ([`TableColumn.Data`](FitsNet.TableColumn.md#fitsnet-tablecolumn-data) is null).
- [`Hdu.VerifyChecksum()`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](FitsNet.Hdu.md#fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](FitsNet.Hdu.md#fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
