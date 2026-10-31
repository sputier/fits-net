# CompressedImageHdu Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → [`Hdu`](FitsNet.Hdu.md) → [`TableHdu`](FitsNet.TableHdu.md) → [`BinaryTableHdu`](FitsNet.BinaryTableHdu.md) → `CompressedImageHdu`

A tile-compressed image (standard section 10): a binary table with `ZIMAGE = T` whose rows hold the compressed tiles. It is a [`BinaryTableHdu`](FitsNet.BinaryTableHdu.md), so the table API still works, and it adds the image API: [`CompressedImageHdu.Decompress`](#fitsnet-compressedimagehdu-decompress-system-int32), [`CompressedImageHdu.ReadPixels`](#fitsnet-compressedimagehdu-readpixels-1) and [`CompressedImageHdu.Compress`](#fitsnet-compressedimagehdu-compress-fitsnet-imagehdu-fitsnet-compression-compressionoptions).

```csharp
public sealed class CompressedImageHdu : BinaryTableHdu
```

## Properties

| Member | Description |
| --- | --- |
| [`Algorithm`](#fitsnet-compressedimagehdu-algorithm) | The ZCMPTYPE value, e.g. |
| [`Axes`](#fitsnet-compressedimagehdu-axes) | The axes of the uncompressed image (ZNAXISn). |
| [`BitPix`](#fitsnet-compressedimagehdu-bitpix) | BITPIX of the uncompressed image (ZBITPIX). |
| [`TileShape`](#fitsnet-compressedimagehdu-tileshape) | The tile size along each axis (ZTILEn). |

## Methods

| Member | Description |
| --- | --- |
| [`AddColumn(TableColumn)`](#fitsnet-compressedimagehdu-addcolumn-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`AppendRows(IReadOnlyList<TableColumn>)`](#fitsnet-compressedimagehdu-appendrows-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`Compress(ImageHdu, CompressionOptions)`](#fitsnet-compressedimagehdu-compress-fitsnet-imagehdu-fitsnet-compression-compressionoptions) | Compresses an image. |
| [`Create(IReadOnlyList<TableColumn>)`](#fitsnet-compressedimagehdu-create-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Not available: use [`CompressedImageHdu.Create`](#fitsnet-compressedimagehdu-create-1-system-readonlyspan-0-system-int64-fitsnet-compression-compressionoptions) or [`CompressedImageHdu.Compress`](#fitsnet-compressedimagehdu-compress-fitsnet-imagehdu-fitsnet-compression-compressionoptions) to build a compressed image. |
| [`Create<T>(ReadOnlySpan<T>, long[], CompressionOptions)`](#fitsnet-compressedimagehdu-create-1-system-readonlyspan-0-system-int64-fitsnet-compression-compressionoptions) | Compresses pixels into a tile-compressed image (supported types: those of [`ImageHdu.Create`](FitsNet.ImageHdu.md#fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64)). |
| [`Decompress(int)`](#fitsnet-compressedimagehdu-decompress-system-int32) | Decompresses every tile into an ordinary image (BSCALE, BZERO, BLANK and other cards are carried over). |
| [`IsCompressedImage(FitsHeader)`](#fitsnet-compressedimagehdu-iscompressedimage-fitsnet-fitsheader) | Whether a binary table header describes a tile-compressed image. |
| [`ReadPixels<T>()`](#fitsnet-compressedimagehdu-readpixels-1) | Decompresses and reads physical pixel values, like [`ImageHdu.ReadPixels`](FitsNet.ImageHdu.md#fitsnet-imagehdu-readpixels-1). |
| [`ReadRawPixels<T>()`](#fitsnet-compressedimagehdu-readrawpixels-1) | Decompresses and reads the stored pixel values, like [`ImageHdu.ReadRawPixels`](FitsNet.ImageHdu.md#fitsnet-imagehdu-readrawpixels-1). |
| [`RemoveColumn(int)`](#fitsnet-compressedimagehdu-removecolumn-system-int32) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`RemoveRows(long, long)`](#fitsnet-compressedimagehdu-removerows-system-int64-system-int64) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`SetColumns(IReadOnlyList<TableColumn>)`](#fitsnet-compressedimagehdu-setcolumns-system-collections-generic-ireadonlylist-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |

## Property details

<a id="fitsnet-compressedimagehdu-algorithm"></a>
### `Algorithm`

The ZCMPTYPE value, e.g. `RICE_1`.

```csharp
public string Algorithm { get; }
```

<a id="fitsnet-compressedimagehdu-axes"></a>
### `Axes`

The axes of the uncompressed image (ZNAXISn).

```csharp
public IReadOnlyList<long> Axes { get; }
```

<a id="fitsnet-compressedimagehdu-bitpix"></a>
### `BitPix`

BITPIX of the uncompressed image (ZBITPIX).

```csharp
public BitPix BitPix { get; }
```

<a id="fitsnet-compressedimagehdu-tileshape"></a>
### `TileShape`

The tile size along each axis (ZTILEn).

```csharp
public IReadOnlyList<long> TileShape { get; }
```

## Method details

<a id="fitsnet-compressedimagehdu-addcolumn-fitsnet-tablecolumn"></a>
### `AddColumn(TableColumn)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void AddColumn(TableColumn column)
```

**Parameters**

- `column` — The column to add; its data must have one entry for each row of the table.

<a id="fitsnet-compressedimagehdu-appendrows-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `AppendRows(IReadOnlyList<TableColumn>)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void AppendRows(IReadOnlyList<TableColumn> rows)
```

**Parameters**

- `rows` — One column of data for each column of the table, in the same order, holding the rows to add.

<a id="fitsnet-compressedimagehdu-compress-fitsnet-imagehdu-fitsnet-compression-compressionoptions"></a>
### `Compress(ImageHdu, CompressionOptions)`

Compresses an image. The image is left untouched; its header cards (except structural ones) are copied.

```csharp
public static CompressedImageHdu Compress(ImageHdu image, CompressionOptions? options = null)
```

**Parameters**

- `image` — The image to compress; it is not modified.
- `options` — The compression options, or null for Rice compression with default settings.

<a id="fitsnet-compressedimagehdu-create-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `Create(IReadOnlyList<TableColumn>)`

Not available: use [`CompressedImageHdu.Create`](#fitsnet-compressedimagehdu-create-1-system-readonlyspan-0-system-int64-fitsnet-compression-compressionoptions) or [`CompressedImageHdu.Compress`](#fitsnet-compressedimagehdu-compress-fitsnet-imagehdu-fitsnet-compression-compressionoptions) to build a compressed image.

```csharp
public static BinaryTableHdu Create(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The columns, each with its data; they must all have the same number of rows.

<a id="fitsnet-compressedimagehdu-create-1-system-readonlyspan-0-system-int64-fitsnet-compression-compressionoptions"></a>
### `Create<T>(ReadOnlySpan<T>, long[], CompressionOptions)`

Compresses pixels into a tile-compressed image (supported types: those of [`ImageHdu.Create`](FitsNet.ImageHdu.md#fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64)).

```csharp
public static CompressedImageHdu Create<T>(ReadOnlySpan<T> pixels, long[] axes, CompressionOptions? options = null) where T : unmanaged
```

**Type parameters**

- `T` — The pixel type: `byte`, `short`, `int`, `long`, `float` or `double`, or an unsigned type.

**Parameters**

- `pixels` — The pixels, the first axis varying fastest.
- `axes` — The length of each axis.
- `options` — The compression options, or null for Rice compression with default settings.

<a id="fitsnet-compressedimagehdu-decompress-system-int32"></a>
### `Decompress(int)`

Decompresses every tile into an ordinary image (BSCALE, BZERO, BLANK and other cards are carried over).

```csharp
public ImageHdu Decompress(int maxDegreeOfParallelism = -1)
```

**Parameters**

- `maxDegreeOfParallelism` — Tiles decoded at the same time (default: one per processor core).

<a id="fitsnet-compressedimagehdu-iscompressedimage-fitsnet-fitsheader"></a>
### `IsCompressedImage(FitsHeader)`

Whether a binary table header describes a tile-compressed image.

```csharp
public static bool IsCompressedImage(FitsHeader header)
```

**Parameters**

- `header` — The header of a binary table extension.

<a id="fitsnet-compressedimagehdu-readpixels-1"></a>
### `ReadPixels<T>()`

Decompresses and reads physical pixel values, like [`ImageHdu.ReadPixels`](FitsNet.ImageHdu.md#fitsnet-imagehdu-readpixels-1).

```csharp
public T[] ReadPixels<T>() where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as, for example `float` or `double`.

<a id="fitsnet-compressedimagehdu-readrawpixels-1"></a>
### `ReadRawPixels<T>()`

Decompresses and reads the stored pixel values, like [`ImageHdu.ReadRawPixels`](FitsNet.ImageHdu.md#fitsnet-imagehdu-readrawpixels-1).

```csharp
public T[] ReadRawPixels<T>() where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

<a id="fitsnet-compressedimagehdu-removecolumn-system-int32"></a>
### `RemoveColumn(int)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void RemoveColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="fitsnet-compressedimagehdu-removerows-system-int64-system-int64"></a>
### `RemoveRows(long, long)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void RemoveRows(long start, long count)
```

**Parameters**

- `start` — The zero-based index of the first row to remove.
- `count` — The number of rows to remove.

<a id="fitsnet-compressedimagehdu-setcolumns-system-collections-generic-ireadonlylist-fitsnet-tablecolumn"></a>
### `SetColumns(IReadOnlyList<TableColumn>)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void SetColumns(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The columns that would replace the table.

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
