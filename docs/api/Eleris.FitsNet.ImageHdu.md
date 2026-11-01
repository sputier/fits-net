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
| [`ReadPixels<T>()`](#eleris-fitsnet-imagehdu-readpixels-1) | Reads physical pixel values. |
| [`ReadRawPixels<T>()`](#eleris-fitsnet-imagehdu-readrawpixels-1) | Reads the stored values without applying BSCALE/BZERO or BLANK. `T` must match BITPIX (otherwise [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception)). |
| [`ReadRawRegion<T>(long[], long[])`](#eleris-fitsnet-imagehdu-readrawregion-1-system-int64-system-int64) | Reads stored pixel values of a region (see [`ImageHdu.Subimage`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) and [`ImageHdu.ReadRawPixels`](#eleris-fitsnet-imagehdu-readrawpixels-1)). |
| [`ReadRegion<T>(long[], long[])`](#eleris-fitsnet-imagehdu-readregion-1-system-int64-system-int64) | Reads physical pixel values of a region (see [`ImageHdu.Subimage`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) and [`ImageHdu.ReadPixels`](#eleris-fitsnet-imagehdu-readpixels-1)). |
| [`SetPixels<T>(ReadOnlySpan<T>, long[])`](#eleris-fitsnet-imagehdu-setpixels-1-system-readonlyspan-0-system-int64) | Replaces the pixel data, BITPIX and axes; BSCALE, BZERO and BLANK are reset (BZERO is set for unsigned types). |
| [`Subimage(long[], long[])`](#eleris-fitsnet-imagehdu-subimage-system-int64-system-int64) | Extracts a rectangular part of the image as a new image. |

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

<a id="eleris-fitsnet-imagehdu-readpixels-1"></a>
### `ReadPixels<T>()`

Reads physical pixel values. `double` and `float` always work (BSCALE/BZERO applied, BLANK becomes NaN). Integer types work when no scaling is needed, or for the standard unsigned conventions (`sbyte`, `ushort`, `uint`, `ulong` with the matching BZERO). BLANK is only mapped to NaN for `double`/`float`; integer reads return the sentinel as an ordinary value. Reading 64-bit integers as `double` loses precision above 2^53.

```csharp
public T[] ReadPixels<T>() where T : unmanaged
```

**Type parameters**

- `T` — The type the physical values are returned as (`BSCALE` and `BZERO` applied), for example `float` or `double`.

<a id="eleris-fitsnet-imagehdu-readrawpixels-1"></a>
### `ReadRawPixels<T>()`

Reads the stored values without applying BSCALE/BZERO or BLANK. `T` must match BITPIX (otherwise [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception)).

```csharp
public T[] ReadRawPixels<T>() where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

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

## Inherited members

- [`Hdu.VerifyChecksum()`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
