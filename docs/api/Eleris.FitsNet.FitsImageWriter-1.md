# FitsImageWriter&lt;T&gt; Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `FitsImageWriter<T>`

Writes a FITS file that holds one image, band by band, so that the image never has to be in memory: for images larger than the memory, or computed a few rows at a time (a mosaic, a stack). The header is given first; the rows then follow in file order, a band of consecutive rows of one plane at a time, and [`FitsImageWriter.Complete`](#eleris-fitsnet-fitsimagewriter-1-complete-system-threading-cancellationtoken) publishes the file.

```csharp
public sealed class FitsImageWriter<T> : IDisposable where T : unmanaged
```

**Remarks**

A row is `NAXIS1` values, a plane is `NAXIS2` rows (one row for a one-dimensional image), and the planes are the combinations of the other axes, in file order (`NAXIS3` planes for a cube). The rows are given plane 0 rows 0 to `NAXIS2 - 1`, then plane 1, and so on; a band can have any number of rows, from one to what is left of its plane.

The file is byte for byte the one [`FitsFile.Save`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy-eleris-fitsnet-fitssavemode-system-threading-cancellationtoken) writes for a [`FitsFile`](Eleris.FitsNet.FitsFile.md) holding only an image of the same pixels and header (made with [`ImageHdu.CreateFromPlanes`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-createfromplanes-1-system-collections-generic-ireadonlylist-system-readonlymemory-0-system-int64) or [`ImageHdu.Create`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64)), with the same guarantees: the file is written to a temporary file in the directory of the target and moved in place by [`FitsImageWriter.Complete`](#eleris-fitsnet-fitsimagewriter-1-complete-system-threading-cancellationtoken); [`FitsSaveMode.CreateNew`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-createnew) never replaces a file, even one that appears while the image is written (on Unix with the limits described for that mode in [`FitsFile.Save`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy-eleris-fitsnet-fitssavemode-system-threading-cancellationtoken)); a write that fails, is cancelled, or is disposed of before [`FitsImageWriter.Complete`](#eleris-fitsnet-fitsimagewriter-1-complete-system-threading-cancellationtoken) deletes the temporary file and leaves an existing target untouched. `CHECKSUM` and `DATASUM` are summed while the rows are written, and their cards, which have a fixed width, are filled in before the file is published.

The writer holds no band: each band is converted to the bytes of the file through a buffer of 256 KB, and the memory it uses (that buffer, the buffer of the file and the header) does not depend on the number of rows. The writes are synchronous. Any exception thrown by [`FitsImageWriter.WriteRows`](#eleris-fitsnet-fitsimagewriter-1-writerows-system-int64-system-int64-system-readonlyspan-0-system-threading-cancellationtoken) or [`FitsImageWriter.Complete`](#eleris-fitsnet-fitsimagewriter-1-complete-system-threading-cancellationtoken), an invalid argument included, abandons the write: the temporary file is deleted, nothing is published, and every later call throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception). An instance is not safe for concurrent use.

**Example**

```csharp
var header = new FitsHeader()
    .Set("BITPIX", -32).Set("NAXIS", 2).Set("NAXIS1", width).Set("NAXIS2", height)
    .Set("OBJECT", "M42");
using var writer = FitsImageWriter<float>.Create("mosaic.fits", header, ChecksumPolicy.Always, FitsSaveMode.CreateNew);
var band = new float[width * 64];
for (long row = 0; row < height; row += 64)
{
    int rows = (int)Math.Min(64, height - row);
    ComputeRows(row, band.AsSpan(0, rows * width));
    writer.WriteRows(0, row, band.AsSpan(0, rows * width));
}
writer.Complete();
```

## Properties

| Member | Description |
| --- | --- |
| [`PlaneCount`](#eleris-fitsnet-fitsimagewriter-1-planecount) | The number of planes: the product of `NAXIS3` and the axes after it, 1 for an image of one or two axes. |
| [`RowLength`](#eleris-fitsnet-fitsimagewriter-1-rowlength) | The number of values in a row: `NAXIS1`. |
| [`RowsPerPlane`](#eleris-fitsnet-fitsimagewriter-1-rowsperplane) | The number of rows in a plane: `NAXIS2`, or 1 for a one-dimensional image. |

## Methods

| Member | Description |
| --- | --- |
| [`Complete(CancellationToken)`](#eleris-fitsnet-fitsimagewriter-1-complete-system-threading-cancellationtoken) | Finishes the file and publishes it: pads the data to a whole block, fills in `CHECKSUM` and `DATASUM`, flushes the temporary file to the disk and moves it to the path, replacing an existing file unless the mode is [`FitsSaveMode.CreateNew`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-createnew). |
| [`Create(string, FitsHeader, ChecksumPolicy, FitsSaveMode, CancellationToken)`](#eleris-fitsnet-fitsimagewriter-1-create-system-string-eleris-fitsnet-fitsheader-eleris-fitsnet-checksumpolicy-eleris-fitsnet-fitssavemode-system-threading-cancellationtoken) | Starts writing an image to `path`: checks the header, creates the temporary file next to the target and writes the header to it. |
| [`Dispose()`](#eleris-fitsnet-fitsimagewriter-1-dispose) | Abandons the write if it has not completed: the temporary file is deleted and nothing is published. |
| [`WriteRows(long, long, ReadOnlySpan<T>, CancellationToken)`](#eleris-fitsnet-fitsimagewriter-1-writerows-system-int64-system-int64-system-readonlyspan-0-system-threading-cancellationtoken) | Writes a band: whole rows of one plane, which must be the next ones in file order. |

## Property details

<a id="eleris-fitsnet-fitsimagewriter-1-planecount"></a>
### `PlaneCount`

The number of planes: the product of `NAXIS3` and the axes after it, 1 for an image of one or two axes.

```csharp
public long PlaneCount { get; }
```

<a id="eleris-fitsnet-fitsimagewriter-1-rowlength"></a>
### `RowLength`

The number of values in a row: `NAXIS1`.

```csharp
public int RowLength { get; }
```

<a id="eleris-fitsnet-fitsimagewriter-1-rowsperplane"></a>
### `RowsPerPlane`

The number of rows in a plane: `NAXIS2`, or 1 for a one-dimensional image.

```csharp
public long RowsPerPlane { get; }
```

## Method details

<a id="eleris-fitsnet-fitsimagewriter-1-complete-system-threading-cancellationtoken"></a>
### `Complete(CancellationToken)`

Finishes the file and publishes it: pads the data to a whole block, fills in `CHECKSUM` and `DATASUM`, flushes the temporary file to the disk and moves it to the path, replacing an existing file unless the mode is [`FitsSaveMode.CreateNew`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-createnew). `cancellationToken` is checked until the file is moved in place; the move is not interrupted.

```csharp
public void Complete(CancellationToken cancellationToken = default)
```

**Parameters**

- `cancellationToken` — The token that cancels the completion.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — Rows are missing (nothing is published), the writer has already completed, or an earlier call failed.
- [`FitsFileExistsException`](Eleris.FitsNet.FitsFileExistsException.md) — The mode is [`FitsSaveMode.CreateNew`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-createnew) and something has appeared at the path meanwhile: it is kept.
- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled before the file was put in place.
- [`ObjectDisposedException`](https://learn.microsoft.com/dotnet/api/system.objectdisposedexception) — The writer has been disposed of.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The file could not be written or put in place.

<a id="eleris-fitsnet-fitsimagewriter-1-create-system-string-eleris-fitsnet-fitsheader-eleris-fitsnet-checksumpolicy-eleris-fitsnet-fitssavemode-system-threading-cancellationtoken"></a>
### `Create(string, FitsHeader, ChecksumPolicy, FitsSaveMode, CancellationToken)`

Starts writing an image to `path`: checks the header, creates the temporary file next to the target and writes the header to it. The target is not touched until [`FitsImageWriter.Complete`](#eleris-fitsnet-fitsimagewriter-1-complete-system-threading-cancellationtoken).

```csharp
public static FitsImageWriter<T> Create(string path, FitsHeader header, ChecksumPolicy checksums = ChecksumPolicy.Auto, FitsSaveMode mode = FitsSaveMode.Overwrite, CancellationToken cancellationToken = default)
```

**Parameters**

- `path` — The path of the file to write.
- `header` — The header of the image.
- `checksums` — What to do with the `CHECKSUM` and `DATASUM` keywords.
- `mode` — Whether an existing file may be replaced.
- `cancellationToken` — The token that cancels the creation.

**Returns**

The writer, to dispose of once the file is complete or abandoned.

**Exceptions**

- [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) — `header` is `null`.
- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — `path` is empty, or the header does not describe an image with pixels, has a `BITPIX` that is not the one of `T`, lacks the `BZERO` of an unsigned type, or has rows longer than an array can hold.
- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — `checksums` or `mode` is not a value of its enumeration.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — `T` is not a supported pixel type.
- [`FitsFileExistsException`](Eleris.FitsNet.FitsFileExistsException.md) — `mode` is [`FitsSaveMode.CreateNew`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-createnew) and something exists at `path`.
- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The temporary file could not be created or written.

**Remarks**

The header holds `BITPIX`, `NAXIS` and `NAXIS1`..`NAXISn`, and any other card, which is written as given (`BSCALE`, `BZERO`, `BLANK`, a WCS, ...). It is copied: changing it afterwards changes nothing. Its structural cards are made those of a primary image, as [`FitsFile.Save`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy) does for the first HDU of a file: the header may start with `SIMPLE`, or with `XTENSION = 'IMAGE'` (the header of an extension), or with neither. So the header of an [`ImageHdu`](Eleris.FitsNet.ImageHdu.md) gives the file that saving that image alone would give.

`BITPIX` must be the one of `T`, and for `sbyte`, `ushort`, `uint` and `ulong` the header must have the `BZERO` of the convention (-128, 32768, 2147483648 or 9223372036854775808, which [`ImageHdu.Create`](Eleris.FitsNet.ImageHdu.md#eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64) writes) and no `BSCALE` other than 1. The image must have at least one pixel.

`checksums` is applied as by [`FitsFile.Save`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy): [`ChecksumPolicy.Auto`](Eleris.FitsNet.ChecksumPolicy.md#eleris-fitsnet-checksumpolicy-auto) writes `CHECKSUM` and `DATASUM` when the header has one of them, [`ChecksumPolicy.Always`](Eleris.FitsNet.ChecksumPolicy.md#eleris-fitsnet-checksumpolicy-always) always, [`ChecksumPolicy.Never`](Eleris.FitsNet.ChecksumPolicy.md#eleris-fitsnet-checksumpolicy-never) removes them. Unlike a save of an HDU read from a file and left unchanged, which keeps its stored sums, the writer computes them for the data it is given: sums already in the header are never kept.

Unlike [`FitsFile.Save`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy), the writer cannot attach a [`FitsFile`](Eleris.FitsNet.FitsFile.md) to the file it replaces: do not write over a file that an open [`FitsFile`](Eleris.FitsNet.FitsFile.md) reads (on Windows the replacement fails while it is open).

<a id="eleris-fitsnet-fitsimagewriter-1-dispose"></a>
### `Dispose()`

Abandons the write if it has not completed: the temporary file is deleted and nothing is published.

```csharp
public void Dispose()
```

<a id="eleris-fitsnet-fitsimagewriter-1-writerows-system-int64-system-int64-system-readonlyspan-0-system-threading-cancellationtoken"></a>
### `WriteRows(long, long, ReadOnlySpan<T>, CancellationToken)`

Writes a band: whole rows of one plane, which must be the next ones in file order. The values are converted to the bytes of the file a block at a time, checking `cancellationToken` between blocks; `rows` is not kept.

```csharp
public void WriteRows(long plane, long firstRow, ReadOnlySpan<T> rows, CancellationToken cancellationToken = default)
```

**Parameters**

- `plane` — The zero-based index of the plane, 0 for an image of one or two axes.
- `firstRow` — The zero-based index, in the plane, of the first row of the band.
- `rows` — The values of the band, the rows one after the other, each of [`FitsImageWriter.RowLength`](#eleris-fitsnet-fitsimagewriter-1-rowlength) values.
- `cancellationToken` — The token that cancels the write.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — `plane` and `firstRow` are not the next row to write (rows out of order, written twice, or after the last one), or `rows` is empty, is not a whole number of rows, or has more rows than are left in the plane.
- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.
- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The writer has completed, or an earlier call failed.
- [`ObjectDisposedException`](https://learn.microsoft.com/dotnet/api/system.objectdisposedexception) — The writer has been disposed of.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The temporary file could not be written.
