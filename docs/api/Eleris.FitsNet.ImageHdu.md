# ImageHdu Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → [`Hdu`](Eleris.FitsNet.Hdu.md) → `ImageHdu`

An image: the primary array or an `XTENSION = 'IMAGE'` extension.

```csharp
public sealed class ImageHdu : Hdu
```

## Constructors

| Member | Description |
| --- | --- |
| [`ImageHdu()`](#eleris-fitsnet-imagehdu-ctor) | Creates an empty image (NAXIS = 0). |

## Properties

| Member | Description |
| --- | --- |
| [`Axes`](#eleris-fitsnet-imagehdu-axes) | The length of each axis (`NAXIS1`, `NAXIS2`, ...), the fastest varying axis first. |
| [`BitPix`](#eleris-fitsnet-imagehdu-bitpix) | The data representation (`BITPIX`). |
| [`PixelCount`](#eleris-fitsnet-imagehdu-pixelcount) | Total number of pixels (0 when there are no axes). |

## Methods

| Member | Description |
| --- | --- |
| [`Compress(CompressionOptions)`](#eleris-fitsnet-imagehdu-compress-eleris-fitsnet-compression-compressionoptions) | Tile-compresses this image (see [`CompressedImageHdu.Compress`](Eleris.FitsNet.CompressedImageHdu.md#eleris-fitsnet-compressedimagehdu-compress-eleris-fitsnet-imagehdu-eleris-fitsnet-compression-compressionoptions)). |
| [`Create<T>(ReadOnlySpan<T>, long[])`](#eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64) | Creates an image from pixels. |
| [`CreateFromPlanes<T>(IReadOnlyList<ReadOnlyMemory<T>>, long[])`](#eleris-fitsnet-imagehdu-createfromplanes-1-system-collections-generic-ireadonlylist-system-readonlymemory-0-system-int64) | Creates an image from planes, without copying them: a plane is all the pixels that share one index on the last axis (the planes of a cube, the rows of a 2D image). |
| [`ReadPixels<T>()`](#eleris-fitsnet-imagehdu-readpixels-1) | Reads physical pixel values. |
| [`ReadPixels<T>(CancellationToken)`](#eleris-fitsnet-imagehdu-readpixels-1-system-threading-cancellationtoken) | Reads physical pixel values, as [`ImageHdu.ReadPixels`](#eleris-fitsnet-imagehdu-readpixels-1) does, checking `cancellationToken` between blocks of data. |
| [`ReadPixelsAsync<T>(CancellationToken)`](#eleris-fitsnet-imagehdu-readpixelsasync-1-system-threading-cancellationtoken) | Asynchronous version of [`ImageHdu.ReadPixels`](#eleris-fitsnet-imagehdu-readpixels-1): the data of a lazily opened file is read with asynchronous I/O, a block at a time, and each block is converted between two reads. |
| [`ReadRawPixels<T>()`](#eleris-fitsnet-imagehdu-readrawpixels-1) | Reads the stored values without applying BSCALE/BZERO or BLANK. `T` must match BITPIX (otherwise [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception)). |
| [`ReadRawPixels<T>(CancellationToken)`](#eleris-fitsnet-imagehdu-readrawpixels-1-system-threading-cancellationtoken) | Reads the stored values, as [`ImageHdu.ReadRawPixels`](#eleris-fitsnet-imagehdu-readrawpixels-1) does, checking `cancellationToken` between blocks of data. |
| [`ReadRawPixelsAsync<T>(CancellationToken)`](#eleris-fitsnet-imagehdu-readrawpixelsasync-1-system-threading-cancellationtoken) | Asynchronous version of [`ImageHdu.ReadRawPixels`](#eleris-fitsnet-imagehdu-readrawpixels-1): the data of a lazily opened file is read with asynchronous I/O, a block at a time, and each block is converted between two reads. |
| [`ReadRawRegion<T>(long[], long[])`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64) | Reads stored pixel values of a region (see [`ImageHdu.Subimage`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) and [`ImageHdu.ReadRawPixels`](#eleris-fitsnet-imagehdu-readrawpixels-1)). |
| [`ReadRawRegion<T>(long[], long[], CancellationToken)`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64-system-threading-cancellationtoken) | Reads stored pixel values of a region, as [`ImageHdu.ReadRawRegion`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64) does, checking `cancellationToken` between the runs of pixels read and the blocks of values converted. |
| [`ReadRawRegionAsync<T>(long[], long[], CancellationToken)`](#eleris-fitsnet-imagehdu-readrawregionasync-1-system-int64-system-int64-system-threading-cancellationtoken) | Asynchronous version of [`ImageHdu.ReadRawRegion`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64): the bytes of the region are read from a lazily opened file with asynchronous I/O. `cancellationToken` is checked as [`ImageHdu.ReadRawRegion`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64-system-threading-cancellationtoken) does. |
| [`ReadRegion<T>(long[], long[])`](#eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64) | Reads physical pixel values of a region (see [`ImageHdu.Subimage`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) and [`ImageHdu.ReadPixels`](#eleris-fitsnet-imagehdu-readpixels-1)). |
| [`ReadRegion<T>(long[], long[], CancellationToken)`](#eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64-system-threading-cancellationtoken) | Reads physical pixel values of a region, as [`ImageHdu.ReadRegion`](#eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64) does, checking `cancellationToken` between the runs of pixels read and the blocks of values converted. |
| [`ReadRegionAsync<T>(long[], long[], CancellationToken)`](#eleris-fitsnet-imagehdu-readregionasync-1-system-int64-system-int64-system-threading-cancellationtoken) | Asynchronous version of [`ImageHdu.ReadRegion`](#eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64): the bytes of the region are read from a lazily opened file with asynchronous I/O, then converted. |
| [`SetPixels<T>(ReadOnlySpan<T>, long[])`](#eleris-fitsnet-imagehdu-setpixels-1-system-readonlyspan-0-system-int64) | Replaces the pixel data, BITPIX and axes; BSCALE, BZERO and BLANK are reset (BZERO is set for unsigned types). |
| [`Subimage(long[], long[])`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) | Extracts a rectangular part of the image as a new image. |
| [`Subimage(long[], long[], CancellationToken)`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64-system-threading-cancellationtoken) | Extracts a rectangular part of the image as a new image, as [`ImageHdu.Subimage`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) does, checking `cancellationToken` between the runs of pixels read, and every megabyte within a long run. |

## Constructor details

<a id="eleris-fitsnet-imagehdu-ctor"></a>
### `ImageHdu()`

Creates an empty image (NAXIS = 0).

```csharp
public ImageHdu()
```

## Property details

<a id="eleris-fitsnet-imagehdu-axes"></a>
### `Axes`

The length of each axis (`NAXIS1`, `NAXIS2`, ...), the fastest varying axis first.

```csharp
public IReadOnlyList<long> Axes { get; }
```

<a id="eleris-fitsnet-imagehdu-bitpix"></a>
### `BitPix`

The data representation (`BITPIX`).

```csharp
public BitPix BitPix { get; }
```

<a id="eleris-fitsnet-imagehdu-pixelcount"></a>
### `PixelCount`

Total number of pixels (0 when there are no axes).

```csharp
public long PixelCount { get; }
```

## Method details

<a id="eleris-fitsnet-imagehdu-compress-eleris-fitsnet-compression-compressionoptions"></a>
### `Compress(CompressionOptions)`

Tile-compresses this image (see [`CompressedImageHdu.Compress`](Eleris.FitsNet.CompressedImageHdu.md#eleris-fitsnet-compressedimagehdu-compress-eleris-fitsnet-imagehdu-eleris-fitsnet-compression-compressionoptions)).

```csharp
public CompressedImageHdu Compress(CompressionOptions? options = null)
```

**Parameters**

- `options` — The compression options, or null for Rice compression with default settings.

<a id="eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64"></a>
### `Create<T>(ReadOnlySpan<T>, long[])`

Creates an image from pixels. Supported types: byte, sbyte, short, ushort, int, uint, long, ulong, float, double.

```csharp
public static ImageHdu Create<T>(ReadOnlySpan<T> pixels, params long[] axes) where T : unmanaged
```

**Type parameters**

- `T` — The pixel type: `byte`, `short`, `int`, `long`, `float` or `double`; `sbyte`, `ushort`, `uint` and `ulong` use the `BZERO` convention.

**Parameters**

- `pixels` — Pixels with the first axis varying fastest (FITS order).
- `axes` — NAXIS1..NAXISn.

<a id="eleris-fitsnet-imagehdu-createfromplanes-1-system-collections-generic-ireadonlylist-system-readonlymemory-0-system-int64"></a>
### `CreateFromPlanes<T>(IReadOnlyList<ReadOnlyMemory<T>>, long[])`

Creates an image from planes, without copying them: a plane is all the pixels that share one index on the last axis (the planes of a cube, the rows of a 2D image). The planes are read again each time the data is needed: when the file is saved, the checksums are computed or the pixels are read, and the image keeps them after a save. So they must stay valid and unchanged as long as the image uses them (until it is discarded, or [`ImageHdu.SetPixels`](#eleris-fitsnet-imagehdu-setpixels-1-system-readonlyspan-0-system-int64) has replaced its data): a pooled or native buffer must not be returned before, and a plane changed between the checksum and the write of a save would give a wrong `DATASUM`. The bytes written are those of [`ImageHdu.Create`](#eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64) for the same pixels.

```csharp
public static ImageHdu CreateFromPlanes<T>(IReadOnlyList<ReadOnlyMemory<T>> planes, params long[] axes) where T : unmanaged
```

**Type parameters**

- `T` — The pixel type (see [`ImageHdu.Create`](#eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64)).

**Parameters**

- `planes` — The planes in file order, each with the first axis varying fastest. The list is copied, not the pixels.
- `axes` — NAXIS1..NAXISn; NAXISn is the number of planes.

**Exceptions**

- [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) — `planes` or `axes` is `null`.
- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — There is no axis, too many axes, a negative axis, a number of planes that is not the last axis, or a plane whose length is not the product of the other axes.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — `T` is not a supported pixel type.

**Example**

```csharp
float[] red = ..., green = ..., blue = ...;          // width * height pixels each
var image = ImageHdu.CreateFromPlanes<float>([red, green, blue], width, height, 3);
```

<a id="eleris-fitsnet-imagehdu-readpixels-1"></a>
### `ReadPixels<T>()`

Reads physical pixel values. `double` and `float` always work (BSCALE/BZERO applied, BLANK becomes NaN). Integer types work when no scaling is needed, or for the standard unsigned conventions (`sbyte`, `ushort`, `uint`, `ulong` with the matching BZERO). BLANK is only mapped to NaN for `double`/`float`; integer reads return the sentinel as an ordinary value. Reading 64-bit integers as `double` loses precision above 2^53.

```csharp
public T[] ReadPixels<T>() where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as (`BSCALE` and `BZERO` applied), for example `float` or `double`.

<a id="eleris-fitsnet-imagehdu-readpixels-1-system-threading-cancellationtoken"></a>
### `ReadPixels<T>(CancellationToken)`

Reads physical pixel values, as [`ImageHdu.ReadPixels`](#eleris-fitsnet-imagehdu-readpixels-1) does, checking `cancellationToken` between blocks of data.

```csharp
public T[] ReadPixels<T>(CancellationToken cancellationToken) where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as (`BSCALE` and `BZERO` applied), for example `float` or `double`.

**Parameters**

- `cancellationToken` — The token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-imagehdu-readpixelsasync-1-system-threading-cancellationtoken"></a>
### `ReadPixelsAsync<T>(CancellationToken)`

Asynchronous version of [`ImageHdu.ReadPixels`](#eleris-fitsnet-imagehdu-readpixels-1): the data of a lazily opened file is read with asynchronous I/O, a block at a time, and each block is converted between two reads. `cancellationToken` is checked between blocks. Data already in memory has no I/O to wait for: it is converted on the calling thread.

```csharp
public Task<T[]> ReadPixelsAsync<T>(CancellationToken cancellationToken = default) where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as (`BSCALE` and `BZERO` applied), for example `float` or `double`.

**Parameters**

- `cancellationToken` — A token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-imagehdu-readrawpixels-1"></a>
### `ReadRawPixels<T>()`

Reads the stored values without applying BSCALE/BZERO or BLANK. `T` must match BITPIX (otherwise [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception)).

```csharp
public T[] ReadRawPixels<T>() where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

<a id="eleris-fitsnet-imagehdu-readrawpixels-1-system-threading-cancellationtoken"></a>
### `ReadRawPixels<T>(CancellationToken)`

Reads the stored values, as [`ImageHdu.ReadRawPixels`](#eleris-fitsnet-imagehdu-readrawpixels-1) does, checking `cancellationToken` between blocks of data.

```csharp
public T[] ReadRawPixels<T>(CancellationToken cancellationToken) where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

**Parameters**

- `cancellationToken` — The token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-imagehdu-readrawpixelsasync-1-system-threading-cancellationtoken"></a>
### `ReadRawPixelsAsync<T>(CancellationToken)`

Asynchronous version of [`ImageHdu.ReadRawPixels`](#eleris-fitsnet-imagehdu-readrawpixels-1): the data of a lazily opened file is read with asynchronous I/O, a block at a time, and each block is converted between two reads. `cancellationToken` is checked between blocks. Data already in memory has no I/O to wait for: it is converted on the calling thread.

```csharp
public Task<T[]> ReadRawPixelsAsync<T>(CancellationToken cancellationToken = default) where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

**Parameters**

- `cancellationToken` — A token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64"></a>
### `ReadRawRegion<T>(long[], long[])`

Reads stored pixel values of a region (see [`ImageHdu.Subimage`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) and [`ImageHdu.ReadRawPixels`](#eleris-fitsnet-imagehdu-readrawpixels-1)).

```csharp
public T[] ReadRawRegion<T>(long[] origin, long[] shape) where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

**Parameters**

- `origin` — The zero-based position of the first pixel of the region on each axis.
- `shape` — The length of the region on each axis.

<a id="eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64-system-threading-cancellationtoken"></a>
### `ReadRawRegion<T>(long[], long[], CancellationToken)`

Reads stored pixel values of a region, as [`ImageHdu.ReadRawRegion`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64) does, checking `cancellationToken` between the runs of pixels read and the blocks of values converted.

```csharp
public T[] ReadRawRegion<T>(long[] origin, long[] shape, CancellationToken cancellationToken) where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

**Parameters**

- `origin` — The zero-based position of the first pixel of the region on each axis.
- `shape` — The length of the region on each axis.
- `cancellationToken` — The token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-imagehdu-readrawregionasync-1-system-int64-system-int64-system-threading-cancellationtoken"></a>
### `ReadRawRegionAsync<T>(long[], long[], CancellationToken)`

Asynchronous version of [`ImageHdu.ReadRawRegion`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64): the bytes of the region are read from a lazily opened file with asynchronous I/O. `cancellationToken` is checked as [`ImageHdu.ReadRawRegion`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64-system-threading-cancellationtoken) does.

```csharp
public Task<T[]> ReadRawRegionAsync<T>(long[] origin, long[] shape, CancellationToken cancellationToken = default) where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

**Parameters**

- `origin` — The zero-based position of the first pixel of the region on each axis.
- `shape` — The length of the region on each axis.
- `cancellationToken` — A token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64"></a>
### `ReadRegion<T>(long[], long[])`

Reads physical pixel values of a region (see [`ImageHdu.Subimage`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) and [`ImageHdu.ReadPixels`](#eleris-fitsnet-imagehdu-readpixels-1)).

```csharp
public T[] ReadRegion<T>(long[] origin, long[] shape) where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as.

**Parameters**

- `origin` — The zero-based position of the first pixel of the region on each axis.
- `shape` — The length of the region on each axis.

<a id="eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64-system-threading-cancellationtoken"></a>
### `ReadRegion<T>(long[], long[], CancellationToken)`

Reads physical pixel values of a region, as [`ImageHdu.ReadRegion`](#eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64) does, checking `cancellationToken` between the runs of pixels read and the blocks of values converted.

```csharp
public T[] ReadRegion<T>(long[] origin, long[] shape, CancellationToken cancellationToken) where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as.

**Parameters**

- `origin` — The zero-based position of the first pixel of the region on each axis.
- `shape` — The length of the region on each axis.
- `cancellationToken` — The token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-imagehdu-readregionasync-1-system-int64-system-int64-system-threading-cancellationtoken"></a>
### `ReadRegionAsync<T>(long[], long[], CancellationToken)`

Asynchronous version of [`ImageHdu.ReadRegion`](#eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64): the bytes of the region are read from a lazily opened file with asynchronous I/O, then converted. `cancellationToken` is checked as [`ImageHdu.ReadRegion`](#eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64-system-threading-cancellationtoken) does.

```csharp
public Task<T[]> ReadRegionAsync<T>(long[] origin, long[] shape, CancellationToken cancellationToken = default) where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as.

**Parameters**

- `origin` — The zero-based position of the first pixel of the region on each axis.
- `shape` — The length of the region on each axis.
- `cancellationToken` — A token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-fitsnet-imagehdu-setpixels-1-system-readonlyspan-0-system-int64"></a>
### `SetPixels<T>(ReadOnlySpan<T>, long[])`

Replaces the pixel data, BITPIX and axes; BSCALE, BZERO and BLANK are reset (BZERO is set for unsigned types).

```csharp
public void SetPixels<T>(ReadOnlySpan<T> pixels, params long[] axes) where T : unmanaged
```

**Type parameters**

- `T` — The pixel type (see [`ImageHdu.Create`](#eleris-fitsnet-imagehdu-create-1-system-readonlyspan-0-system-int64)).

**Parameters**

- `pixels` — The pixels, the first axis varying fastest.
- `axes` — The length of each axis.

<a id="eleris-fitsnet-imagehdu-subimage-system-int64-system-int64"></a>
### `Subimage(long[], long[])`

Extracts a rectangular part of the image as a new image. Only the bytes of the region are read from a lazily opened file, so any part of an image of any size can be read. `CRPIXn` (all alternate WCS too) is shifted so that the world coordinates of the region stay correct; CHECKSUM and DATASUM are dropped.

```csharp
public ImageHdu Subimage(long[] origin, long[] shape)
```

**Parameters**

- `origin` — Zero-based first pixel along each axis.
- `shape` — Number of pixels along each axis.

<a id="eleris-fitsnet-imagehdu-subimage-system-int64-system-int64-system-threading-cancellationtoken"></a>
### `Subimage(long[], long[], CancellationToken)`

Extracts a rectangular part of the image as a new image, as [`ImageHdu.Subimage`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) does, checking `cancellationToken` between the runs of pixels read, and every megabyte within a long run.

```csharp
public ImageHdu Subimage(long[] origin, long[] shape, CancellationToken cancellationToken)
```

**Parameters**

- `origin` — Zero-based first pixel along each axis.
- `shape` — Number of pixels along each axis.
- `cancellationToken` — The token that cancels the read.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

## Inherited members

- [`Hdu.VerifyChecksum()`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
