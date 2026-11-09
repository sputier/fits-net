# CompressedImageHdu Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → [`Hdu`](Eleris.FitsNet.Hdu.md) → [`TableHdu`](Eleris.FitsNet.TableHdu.md) → [`BinaryTableHdu`](Eleris.FitsNet.BinaryTableHdu.md) → `CompressedImageHdu`

A tile-compressed image (standard section 10): a binary table with `ZIMAGE = T` whose rows hold the compressed tiles. It is a [`BinaryTableHdu`](Eleris.FitsNet.BinaryTableHdu.md), so the table API still works, and it adds the image API: [`CompressedImageHdu.Decompress`](#eleris-fitsnet-compressedimagehdu-decompress-system-int32), [`CompressedImageHdu.ReadPixels`](#eleris-fitsnet-compressedimagehdu-readpixels-1) and [`CompressedImageHdu.Compress`](#eleris-fitsnet-compressedimagehdu-compress-eleris-fitsnet-imagehdu-eleris-fitsnet-compression-compressionoptions).

```csharp
public sealed class CompressedImageHdu : BinaryTableHdu
```

## Properties

| Member | Description |
| --- | --- |
| [`Algorithm`](#eleris-fitsnet-compressedimagehdu-algorithm) | The ZCMPTYPE value, e.g. |
| [`Axes`](#eleris-fitsnet-compressedimagehdu-axes) | The axes of the uncompressed image (ZNAXISn). |
| [`BitPix`](#eleris-fitsnet-compressedimagehdu-bitpix) | BITPIX of the uncompressed image (ZBITPIX). |
| [`TileShape`](#eleris-fitsnet-compressedimagehdu-tileshape) | The tile size along each axis (ZTILEn). |

## Methods

| Member | Description |
| --- | --- |
| [`AddColumn(TableColumn)`](#eleris-fitsnet-compressedimagehdu-addcolumn-eleris-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`AppendRows(IReadOnlyList<TableColumn>)`](#eleris-fitsnet-compressedimagehdu-appendrows-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`Compress(ImageHdu, CompressionOptions)`](#eleris-fitsnet-compressedimagehdu-compress-eleris-fitsnet-imagehdu-eleris-fitsnet-compression-compressionoptions) | Compresses an image. |
| [`Create(IReadOnlyList<TableColumn>)`](#eleris-fitsnet-compressedimagehdu-create-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn) | Not available: use [`CompressedImageHdu.Create`](#eleris-fitsnet-compressedimagehdu-create-1-system-readonlyspan-0-system-int64-eleris-fitsnet-compression-compressionoptions) or [`CompressedImageHdu.Compress`](#eleris-fitsnet-compressedimagehdu-compress-eleris-fitsnet-imagehdu-eleris-fitsnet-compression-compressionoptions) to build a compressed image. |
| [`Create<T>(ReadOnlySpan<T>, long[], CompressionOptions)`](#eleris-fitsnet-compressedimagehdu-create-1-system-readonlyspan-0-system-int64-eleris-fitsnet-compression-compressionoptions) | Compresses pixels into a tile-compressed image (supported types: those of [`ImageHdu.Create`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64)). |
| [`Decompress(int)`](#eleris-fitsnet-compressedimagehdu-decompress-system-int32) | Decompresses every tile into an ordinary image (BSCALE, BZERO, BLANK and other cards are carried over). |
| [`Decompress(int, CancellationToken)`](#eleris-fitsnet-compressedimagehdu-decompress-system-int32-system-threading-cancellationtoken) | Decompresses every tile into an ordinary image, as [`CompressedImageHdu.Decompress`](#eleris-fitsnet-compressedimagehdu-decompress-system-int32) does, checking `cancellationToken` before each tile. |
| [`IsCompressedImage(FitsHeader)`](#eleris-fitsnet-compressedimagehdu-iscompressedimage-eleris-fitsnet-fitsheader) | Whether a binary table header describes a tile-compressed image. |
| [`ReadPixels<T>()`](#eleris-fitsnet-compressedimagehdu-readpixels-1) | Decompresses and reads physical pixel values, like [`ImageHdu.ReadPixels`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-readpixels-1). |
| [`ReadPixels<T>(CancellationToken)`](#eleris-fitsnet-compressedimagehdu-readpixels-1-system-threading-cancellationtoken) | Decompresses and reads physical pixel values, as [`CompressedImageHdu.ReadPixels`](#eleris-fitsnet-compressedimagehdu-readpixels-1) does, checking `cancellationToken` before each tile. |
| [`ReadPixelsAsync<T>(CancellationToken)`](#eleris-fitsnet-compressedimagehdu-readpixelsasync-1-system-threading-cancellationtoken) | Asynchronous version of [`CompressedImageHdu.ReadPixels`](#eleris-fitsnet-compressedimagehdu-readpixels-1): the compressed data of a lazily opened file is read with asynchronous I/O, then the tiles are decompressed on all the cores as [`CompressedImageHdu.ReadPixels`](#eleris-fitsnet-compressedimagehdu-readpixels-1-system-threading-cancellationtoken) does, checking `cancellationToken` between blocks of the read and before each tile. |
| [`ReadRawPixels<T>()`](#eleris-fitsnet-compressedimagehdu-readrawpixels-1) | Decompresses and reads the stored pixel values, like [`ImageHdu.ReadRawPixels`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-readrawpixels-1). |
| [`ReadRawPixels<T>(CancellationToken)`](#eleris-fitsnet-compressedimagehdu-readrawpixels-1-system-threading-cancellationtoken) | Decompresses and reads the stored pixel values, as [`CompressedImageHdu.ReadRawPixels`](#eleris-fitsnet-compressedimagehdu-readrawpixels-1) does, checking `cancellationToken` before each tile. |
| [`ReadRawPixelsAsync<T>(CancellationToken)`](#eleris-fitsnet-compressedimagehdu-readrawpixelsasync-1-system-threading-cancellationtoken) | Asynchronous version of [`CompressedImageHdu.ReadRawPixels`](#eleris-fitsnet-compressedimagehdu-readrawpixels-1): the compressed data of a lazily opened file is read with asynchronous I/O, then decompressed as [`CompressedImageHdu.ReadRawPixels`](#eleris-fitsnet-compressedimagehdu-readrawpixels-1-system-threading-cancellationtoken) does. |
| [`RemoveColumn(int)`](#eleris-fitsnet-compressedimagehdu-removecolumn-system-int32) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`RemoveRows(long, long)`](#eleris-fitsnet-compressedimagehdu-removerows-system-int64-system-int64) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |
| [`SetColumns(IReadOnlyList<TableColumn>)`](#eleris-fitsnet-compressedimagehdu-setcolumns-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn) | Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. |

## Property details

<a id="eleris-fitsnet-compressedimagehdu-algorithm"></a>
### `Algorithm`

The ZCMPTYPE value, e.g. `RICE_1`.

```csharp
public string Algorithm { get; }
```

<a id="eleris-fitsnet-compressedimagehdu-axes"></a>
### `Axes`

The axes of the uncompressed image (ZNAXISn).

```csharp
public IReadOnlyList<long> Axes { get; }
```

<a id="eleris-fitsnet-compressedimagehdu-bitpix"></a>
### `BitPix`

BITPIX of the uncompressed image (ZBITPIX).

```csharp
public BitPix BitPix { get; }
```

<a id="eleris-fitsnet-compressedimagehdu-tileshape"></a>
### `TileShape`

The tile size along each axis (ZTILEn).

```csharp
public IReadOnlyList<long> TileShape { get; }
```

## Method details

<a id="eleris-fitsnet-compressedimagehdu-addcolumn-eleris-fitsnet-tablecolumn"></a>
### `AddColumn(TableColumn)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void AddColumn(TableColumn column)
```

**Parameters**

- `column` — The column to add; its data must have one entry for each row of the table.

<a id="eleris-fitsnet-compressedimagehdu-appendrows-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn"></a>
### `AppendRows(IReadOnlyList<TableColumn>)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void AppendRows(IReadOnlyList<TableColumn> rows)
```

**Parameters**

- `rows` — One column of data for each column of the table, in the same order, holding the rows to add.

<a id="eleris-fitsnet-compressedimagehdu-compress-eleris-fitsnet-imagehdu-eleris-fitsnet-compression-compressionoptions"></a>
### `Compress(ImageHdu, CompressionOptions)`

Compresses an image. The image is left untouched; its header cards (except structural ones) are copied.

```csharp
public static CompressedImageHdu Compress(ImageHdu image, CompressionOptions? options = null)
```

**Parameters**

- `image` — The image to compress; it is not modified.
- `options` — The compression options, or null for Rice compression with default settings.

<a id="eleris-fitsnet-compressedimagehdu-create-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn"></a>
### `Create(IReadOnlyList<TableColumn>)`

Not available: use [`CompressedImageHdu.Create`](#eleris-fitsnet-compressedimagehdu-create-1-system-readonlyspan-0-system-int64-eleris-fitsnet-compression-compressionoptions) or [`CompressedImageHdu.Compress`](#eleris-fitsnet-compressedimagehdu-compress-eleris-fitsnet-imagehdu-eleris-fitsnet-compression-compressionoptions) to build a compressed image.

```csharp
public static BinaryTableHdu Create(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The columns, each with its data; they must all have the same number of rows.

<a id="eleris-fitsnet-compressedimagehdu-create-1-system-readonlyspan-0-system-int64-eleris-fitsnet-compression-compressionoptions"></a>
### `Create<T>(ReadOnlySpan<T>, long[], CompressionOptions)`

Compresses pixels into a tile-compressed image (supported types: those of [`ImageHdu.Create`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64)).

```csharp
public static CompressedImageHdu Create<T>(ReadOnlySpan<T> pixels, long[] axes, CompressionOptions? options = null) where T : unmanaged
```

**Type parameters**

- `T` — The pixel type: `byte`, `short`, `int`, `long`, `float` or `double`, or an unsigned type.

**Parameters**

- `pixels` — The pixels, the first axis varying fastest.
- `axes` — The length of each axis.
- `options` — The compression options, or null for Rice compression with default settings.

<a id="eleris-fitsnet-compressedimagehdu-decompress-system-int32"></a>
### `Decompress(int)`

Decompresses every tile into an ordinary image (BSCALE, BZERO, BLANK and other cards are carried over).

```csharp
public ImageHdu Decompress(int maxDegreeOfParallelism = -1)
```

**Parameters**

- `maxDegreeOfParallelism` — Tiles decoded at the same time (default: one per processor core).

<a id="eleris-fitsnet-compressedimagehdu-decompress-system-int32-system-threading-cancellationtoken"></a>
### `Decompress(int, CancellationToken)`

Decompresses every tile into an ordinary image, as [`CompressedImageHdu.Decompress`](#eleris-fitsnet-compressedimagehdu-decompress-system-int32) does, checking `cancellationToken` before each tile.

```csharp
public ImageHdu Decompress(int maxDegreeOfParallelism, CancellationToken cancellationToken)
```

**Parameters**

- `maxDegreeOfParallelism` — Tiles decoded at the same time: -1 for one per processor core.
- `cancellationToken` — The token that cancels the decompression.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-compressedimagehdu-iscompressedimage-eleris-fitsnet-fitsheader"></a>
### `IsCompressedImage(FitsHeader)`

Whether a binary table header describes a tile-compressed image.

```csharp
public static bool IsCompressedImage(FitsHeader header)
```

**Parameters**

- `header` — The header of a binary table extension.

<a id="eleris-fitsnet-compressedimagehdu-readpixels-1"></a>
### `ReadPixels<T>()`

Decompresses and reads physical pixel values, like [`ImageHdu.ReadPixels`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-readpixels-1).

```csharp
public T[] ReadPixels<T>() where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as, for example `float` or `double`.

<a id="eleris-fitsnet-compressedimagehdu-readpixels-1-system-threading-cancellationtoken"></a>
### `ReadPixels<T>(CancellationToken)`

Decompresses and reads physical pixel values, as [`CompressedImageHdu.ReadPixels`](#eleris-fitsnet-compressedimagehdu-readpixels-1) does, checking `cancellationToken` before each tile.

```csharp
public T[] ReadPixels<T>(CancellationToken cancellationToken) where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as, for example `float` or `double`.

**Parameters**

- `cancellationToken` — The token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-compressedimagehdu-readpixelsasync-1-system-threading-cancellationtoken"></a>
### `ReadPixelsAsync<T>(CancellationToken)`

Asynchronous version of [`CompressedImageHdu.ReadPixels`](#eleris-fitsnet-compressedimagehdu-readpixels-1): the compressed data of a lazily opened file is read with asynchronous I/O, then the tiles are decompressed on all the cores as [`CompressedImageHdu.ReadPixels`](#eleris-fitsnet-compressedimagehdu-readpixels-1-system-threading-cancellationtoken) does, checking `cancellationToken` between blocks of the read and before each tile.

```csharp
public Task<T[]> ReadPixelsAsync<T>(CancellationToken cancellationToken = default) where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as, for example `float` or `double`.

**Parameters**

- `cancellationToken` — A token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-compressedimagehdu-readrawpixels-1"></a>
### `ReadRawPixels<T>()`

Decompresses and reads the stored pixel values, like [`ImageHdu.ReadRawPixels`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-readrawpixels-1).

```csharp
public T[] ReadRawPixels<T>() where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

<a id="eleris-fitsnet-compressedimagehdu-readrawpixels-1-system-threading-cancellationtoken"></a>
### `ReadRawPixels<T>(CancellationToken)`

Decompresses and reads the stored pixel values, as [`CompressedImageHdu.ReadRawPixels`](#eleris-fitsnet-compressedimagehdu-readrawpixels-1) does, checking `cancellationToken` before each tile.

```csharp
public T[] ReadRawPixels<T>(CancellationToken cancellationToken) where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

**Parameters**

- `cancellationToken` — The token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-compressedimagehdu-readrawpixelsasync-1-system-threading-cancellationtoken"></a>
### `ReadRawPixelsAsync<T>(CancellationToken)`

Asynchronous version of [`CompressedImageHdu.ReadRawPixels`](#eleris-fitsnet-compressedimagehdu-readrawpixels-1): the compressed data of a lazily opened file is read with asynchronous I/O, then decompressed as [`CompressedImageHdu.ReadRawPixels`](#eleris-fitsnet-compressedimagehdu-readrawpixels-1-system-threading-cancellationtoken) does.

```csharp
public Task<T[]> ReadRawPixelsAsync<T>(CancellationToken cancellationToken = default) where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

**Parameters**

- `cancellationToken` — A token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-compressedimagehdu-removecolumn-system-int32"></a>
### `RemoveColumn(int)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void RemoveColumn(int index)
```

**Parameters**

- `index` — The zero-based index of the column.

<a id="eleris-fitsnet-compressedimagehdu-removerows-system-int64-system-int64"></a>
### `RemoveRows(long, long)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void RemoveRows(long start, long count)
```

**Parameters**

- `start` — The zero-based index of the first row to remove.
- `count` — The number of rows to remove.

<a id="eleris-fitsnet-compressedimagehdu-setcolumns-system-collections-generic-ireadonlylist-eleris-fitsnet-tablecolumn"></a>
### `SetColumns(IReadOnlyList<TableColumn>)`

Always throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception): the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.

```csharp
public override void SetColumns(IReadOnlyList<TableColumn> columns)
```

**Parameters**

- `columns` — The columns that would replace the table.

## Inherited members

- [`BinaryTableHdu.ReadColumn<T>(int)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readcolumn-1-system-int32) — Reads a numeric column (flat, `repeat` values per row).
- [`BinaryTableHdu.ReadColumn<T>(string)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readcolumn-1-system-string) — Reads a numeric column by name (see [`BinaryTableHdu.ReadColumn`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readcolumn-1-system-int32)).
- [`BinaryTableHdu.ReadLogicalColumn(int)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readlogicalcolumn-system-int32) — Reads an `L` column: true, false, or null for undefined.
- [`BinaryTableHdu.ReadBitColumn(int)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readbitcolumn-system-int32) — Reads an `X` column, one bool per bit.
- [`BinaryTableHdu.ReadStringColumn(int)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readstringcolumn-system-int32) — Reads an `A` column (or a variable-length character column): one string per cell/row.
- [`BinaryTableHdu.ReadStringColumn(string)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readstringcolumn-system-string) — Reads an `A` column by name (see [`BinaryTableHdu.ReadStringColumn`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readstringcolumn-system-int32)).
- [`BinaryTableHdu.ReadVariableColumn<T>(int)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readvariablecolumn-1-system-int32) — Reads a variable-length column (`P`/`Q`): one array per row, of the element type's native representation.
- [`BinaryTableHdu.ReadVariableColumn<T>(string)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readvariablecolumn-1-system-string) — Reads a variable-length column by name (see [`BinaryTableHdu.ReadVariableColumn`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readvariablecolumn-1-system-int32)).
- [`BinaryTableHdu.ReadVariableLogicalColumn(int)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readvariablelogicalcolumn-system-int32) — Reads a variable-length column of logical values (`PL` / `QL`): one array per row, with null for an undefined value.
- [`BinaryTableHdu.ReadVariableBitColumn(int)`](Eleris.FitsNet.BinaryTableHdu.md#eleris-fitsnet-binarytablehdu-readvariablebitcolumn-system-int32) — Reads a variable-length column of bits (`PX` / `QX`): one array of bits per row.
- [`TableHdu.IndexOf(string)`](Eleris.FitsNet.TableHdu.md#eleris-fitsnet-tablehdu-indexof-system-string) — Index of the column whose TTYPE equals `name` (case-insensitive), or -1.
- [`TableHdu.ReadRaw(int)`](Eleris.FitsNet.TableHdu.md#eleris-fitsnet-tablehdu-readraw-system-int32) — The stored (unscaled) values of a column in the native representation described on [`TableColumn`](Eleris.FitsNet.TableColumn.md).
- [`TableHdu.ReadColumns()`](Eleris.FitsNet.TableHdu.md#eleris-fitsnet-tablehdu-readcolumns) — Reads every column, data included.
- [`TableHdu.RowCount`](Eleris.FitsNet.TableHdu.md#eleris-fitsnet-tablehdu-rowcount) — NAXIS2.
- [`TableHdu.RowWidth`](Eleris.FitsNet.TableHdu.md#eleris-fitsnet-tablehdu-rowwidth) — NAXIS1: bytes per row.
- [`TableHdu.ColumnCount`](Eleris.FitsNet.TableHdu.md#eleris-fitsnet-tablehdu-columncount) — TFIELDS.
- [`TableHdu.Columns`](Eleris.FitsNet.TableHdu.md#eleris-fitsnet-tablehdu-columns) — Column metadata read from the header ([`TableColumn.Data`](Eleris.FitsNet.TableColumn.md#eleris-fitsnet-tablecolumn-data) is null).
- [`Hdu.VerifyChecksum()`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
