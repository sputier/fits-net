# CompressionOptions Class

**Namespace:** `Eleris.FitsNet.Compression`

**Inheritance:** `object` → `CompressionOptions`

Parameters for creating a tile-compressed image.

```csharp
public sealed class CompressionOptions
```

## Constructors

| Member | Description |
| --- | --- |
| [`CompressionOptions()`](#eleris-fitsnet-compression-compressionoptions-ctor) | Creates options with the default settings (Rice compression, blocks of 32 pixels, tiles of one row). |

## Properties

| Member | Description |
| --- | --- |
| [`Algorithm`](#eleris-fitsnet-compression-compressionoptions-algorithm) | The algorithm (default [`CompressionAlgorithm.Rice`](Eleris.FitsNet.Compression.CompressionAlgorithm.md#eleris-fitsnet-compression-compressionalgorithm-rice)). |
| [`DitherSeed`](#eleris-fitsnet-compression-compressionoptions-ditherseed) | Seed of the dithering sequence, 1 to 10000 (ZDITHER0). |
| [`HCompressScale`](#eleris-fitsnet-compression-compressionoptions-hcompressscale) | HCOMPRESS scale: 0 for lossless integer compression, a positive value to divide the transformed coefficients by that number, a negative value `-n` for a scale of `n` times the noise of the tile (floating point data). |
| [`MaxDegreeOfParallelism`](#eleris-fitsnet-compression-compressionoptions-maxdegreeofparallelism) | Maximum number of tiles compressed at the same time: -1 (default) for one per processor core, 1 for sequential, or a positive number. |
| [`Quantization`](#eleris-fitsnet-compression-compressionoptions-quantization) | How floating point pixels are quantized (default subtractive dithering, as cfitsio does). |
| [`QuantizeLevel`](#eleris-fitsnet-compression-compressionoptions-quantizelevel) | Quantization step as a fraction of the measured noise: the step is `noise / QuantizeLevel` (default 4). |
| [`RiceBlockSize`](#eleris-fitsnet-compression-compressionoptions-riceblocksize) | Rice block size (default 32). |
| [`TileShape`](#eleris-fitsnet-compression-compressionoptions-tileshape) | The tile size along each axis (ZTILEn). |

## Constructor details

<a id="eleris-fitsnet-compression-compressionoptions-ctor"></a>
### `CompressionOptions()`

Creates options with the default settings (Rice compression, blocks of 32 pixels, tiles of one row).

```csharp
public CompressionOptions()
```

## Property details

<a id="eleris-fitsnet-compression-compressionoptions-algorithm"></a>
### `Algorithm`

The algorithm (default [`CompressionAlgorithm.Rice`](Eleris.FitsNet.Compression.CompressionAlgorithm.md#eleris-fitsnet-compression-compressionalgorithm-rice)).

```csharp
public CompressionAlgorithm Algorithm { get; set; }
```

<a id="eleris-fitsnet-compression-compressionoptions-ditherseed"></a>
### `DitherSeed`

Seed of the dithering sequence, 1 to 10000 (ZDITHER0).

```csharp
public int DitherSeed { get; set; }
```

<a id="eleris-fitsnet-compression-compressionoptions-hcompressscale"></a>
### `HCompressScale`

HCOMPRESS scale: 0 for lossless integer compression, a positive value to divide the transformed coefficients by that number, a negative value `-n` for a scale of `n` times the noise of the tile (floating point data).

```csharp
public double HCompressScale { get; set; }
```

<a id="eleris-fitsnet-compression-compressionoptions-maxdegreeofparallelism"></a>
### `MaxDegreeOfParallelism`

Maximum number of tiles compressed at the same time: -1 (default) for one per processor core, 1 for sequential, or a positive number.

```csharp
public int MaxDegreeOfParallelism { get; set; }
```

<a id="eleris-fitsnet-compression-compressionoptions-quantization"></a>
### `Quantization`

How floating point pixels are quantized (default subtractive dithering, as cfitsio does).

```csharp
public QuantizationMethod Quantization { get; set; }
```

<a id="eleris-fitsnet-compression-compressionoptions-quantizelevel"></a>
### `QuantizeLevel`

Quantization step as a fraction of the measured noise: the step is `noise / QuantizeLevel` (default 4). A negative value means an absolute step of `-QuantizeLevel`.

```csharp
public double QuantizeLevel { get; set; }
```

<a id="eleris-fitsnet-compression-compressionoptions-riceblocksize"></a>
### `RiceBlockSize`

Rice block size (default 32).

```csharp
public int RiceBlockSize { get; set; }
```

<a id="eleris-fitsnet-compression-compressionoptions-tileshape"></a>
### `TileShape`

The tile size along each axis (ZTILEn). Default: one row of the image per tile, except for HCOMPRESS where tiles of 16 rows are used (it needs two dimensions).

```csharp
public long[]? TileShape { get; set; }
```
