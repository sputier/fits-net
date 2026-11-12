# Compression

FITS compresses an image or a table by cutting it into **tiles** and compressing each tile on its own. The result is stored in a binary table extension, so a compressed file is still a FITS file, and any tile can be decompressed without the others. Eleris.FitsNet compresses and decompresses tiles on all the cores.

## Compress an image

`ImageHdu.Compress` returns a [`CompressedImageHdu`](api/Eleris.FitsNet.CompressedImageHdu.md). The source image is not changed. Without options the algorithm is Rice and each tile is one row of the image.

<!-- example: Compression.Image -->
```csharp
using var source = FitsFile.Open("m42.fits");
var image = (ImageHdu)source.Primary;

// Rice is the default and the best choice for integer images from a camera: lossless and fast.
CompressedImageHdu packed = image.Compress();
Console.WriteLine($"{packed.Algorithm}, tiles of {string.Join(" x ", packed.TileShape)}");

// Choose the algorithm and the tile shape (one row per tile by default).
var options = new CompressionOptions
{
    Algorithm = CompressionAlgorithm.Gzip2,
    TileShape = [160, 120],
};
CompressedImageHdu tiled = image.Compress(options);

// The compressed image is an HDU like any other. The file needs a primary HDU in front of it.
new FitsFile().Add(tiled).Save("m42-compressed.fits");
```
<!-- /example -->

The compressed HDU is an extension: the file needs a primary HDU before it, and `FitsFile.Add` puts an empty one for you.

### Algorithms

| [`CompressionAlgorithm`](api/Eleris.FitsNet.Compression.CompressionAlgorithm.md) | `ZCMPTYPE` | Best for |
| --- | --- | --- |
| `Rice` | `RICE_1` | Integer images (8, 16 and 32 bits). Lossless and fast. The default. |
| `Gzip1` | `GZIP_1` | Any type; gzip of the big-endian pixels. |
| `Gzip2` | `GZIP_2` | Any type; gzip after grouping the bytes of equal weight. Usually smaller than `Gzip1`. |
| `Plio` | `PLIO_1` | Masks: integer values from 0 to 2^24. |
| `HCompress` | `HCOMPRESS_1` | Smooth astronomical images. Lossless when `HCompressScale` is 0, lossy otherwise. It needs two dimensions. |
| `NoCompress` | `NOCOMPRESS` | Tiles stored as they are. |

### Options

[`CompressionOptions`](api/Eleris.FitsNet.Compression.CompressionOptions.md) groups the settings. All have a default.

| Option | Default | Meaning |
| --- | --- | --- |
| `Algorithm` | `Rice` | See above. |
| `TileShape` | One row per tile (16 rows for `HCompress`) | Tile size along each axis. |
| `RiceBlockSize` | 32 | Pixels per Rice block. |
| `HCompressScale` | 0 | 0: lossless. Positive: divides the coefficients by that number. Negative `-n`: `n` times the noise of the tile (floating point data). |
| `Quantization` | `SubtractiveDither1` | How floating point pixels become integers, see below. |
| `QuantizeLevel` | 4 | The quantization step is the noise divided by this value. Negative: an absolute step of `-QuantizeLevel`. |
| `DitherSeed` | 1 | Seed of the dither sequence, 1 to 10000. |
| `GzipLevel` | -1 | The zlib level of `Gzip1` and `Gzip2`, 0 to 9; -1 is the default of the runtime. See below. |
| `MaxDegreeOfParallelism` | -1 | -1: one tile per core at a time. 1: sequential. |

Invalid values throw an `ArgumentException` when compressing, before any work is done.

`Gzip1` and `Gzip2` take their time at the default level: it gives smaller files than cfitsio does (about 6 percent smaller for `Gzip2` on a 16-bit camera frame), in about twice the time of cfitsio on one core. From .NET 9, `GzipLevel = 2` gives files about as large as cfitsio's in about the same time, and a higher level trades time for size. All levels give valid files, read the same way by every library. On .NET 8 only 0 (stored), 1 (the fastest) and 9 (the smallest) differ from the default. The tiles are compressed on all the cores unless `MaxDegreeOfParallelism` says otherwise, which also divides the time.

## Read a compressed image

A compressed extension is recognized from its keywords, so `FitsFile` gives you a `CompressedImageHdu` where the file has one. It reports the `Axes` and `BitPix` of the image that was compressed, the `Algorithm` (the `ZCMPTYPE` text, such as `RICE_1`) and the `TileShape`. `ReadPixels<T>()` and `ReadRawPixels<T>()` decompress the whole image and behave like those of an ordinary image. `Decompress` returns an ordinary [`ImageHdu`](api/Eleris.FitsNet.ImageHdu.md) with the original header; to read a region, decompress first and call `ReadRegion<T>()` on the result.

<!-- example: Compression.Read -->
```csharp
using var file = FitsFile.Open("m42-compressed.fits");

// Extensions are recognized by their keywords: a compressed image is a CompressedImageHdu.
var packed = (CompressedImageHdu)file[1];
Console.WriteLine($"{packed.BitPix}, {string.Join(" x ", packed.Axes)}");

// ReadPixels decompresses the tiles in parallel (the result does not depend on the number of threads).
ushort[] pixels = packed.ReadPixels<ushort>();

// Decompress gives the ordinary image HDU back, with its header restored.
ImageHdu image = packed.Decompress();
Console.WriteLine($"{pixels.Length} pixels, {image.Header.Get<string>("OBJECT")}");
```
<!-- /example -->

## Floating point images

Floating point pixels cannot be compressed by Rice or the other integer algorithms, so they are first **quantized**: turned into integers with a step related to the noise of the image. This loses a controlled amount of information, and is what makes floating point images compress well.

`Quantization` chooses the method:

| `QuantizationMethod` | Meaning |
| --- | --- |
| `SubtractiveDither1` | Quantization with a pseudo-random offset, which removes the bias. The default, as in cfitsio. |
| `SubtractiveDither2` | The same, and values equal to zero are kept exactly. |
| `None` | No quantization: the pixels are stored as they are, with a gzip algorithm. Lossless. |

<!-- example: Compression.Float -->
```csharp
var rng = new Random(1);
var image = ImageHdu.Create<float>(Enumerable.Range(0, 128 * 128).Select(_ => 100f + (float)rng.NextDouble()).ToArray(), 128, 128);

// Floating point pixels are quantized (turned into integers) before the compression, which loses
// a controlled amount of precision: the step is the noise of the image divided by QuantizeLevel (here a quarter).
var lossy = new CompressionOptions
{
    Quantization = QuantizationMethod.SubtractiveDither2,
    QuantizeLevel = 4,
};
CompressedImageHdu packed = image.Compress(lossy);

// To keep every bit, do not quantize: the pixels are stored with gzip.
var lossless = new CompressionOptions
{
    Algorithm = CompressionAlgorithm.Gzip2,
    Quantization = QuantizationMethod.None,
};
float[] exact = image.Compress(lossless).ReadPixels<float>();
Console.WriteLine($"{packed.Algorithm} {exact[0] == image.ReadPixels<float>()[0]}");
```
<!-- /example -->

## Control the number of threads

<!-- example: Compression.Parallelism -->
```csharp
using var file = FitsFile.Open("m42.fits");
var image = (ImageHdu)file.Primary;

// By default every core is used. 1 means one tile at a time.
var options = new CompressionOptions { MaxDegreeOfParallelism = 2 };
CompressedImageHdu packed = image.Compress(options);
ImageHdu back = packed.Decompress(maxDegreeOfParallelism: 2);
Console.WriteLine(back.PixelCount);
```
<!-- /example -->

The result never depends on the number of threads.

## Compress a table

[`CompressedTableHdu.Compress`](api/Eleris.FitsNet.CompressedTableHdu.md) compresses a binary table. Each column is compressed on its own, in tiles of a number of rows. Without options, the library picks Rice for integer columns, gzip for logical, bit and character columns, and `Gzip2` for the others.

<!-- example: Compression.Table -->
```csharp
using var file = FitsFile.Open("m42.fits");
var catalog = (BinaryTableHdu)file[1];

// Each column of a table is compressed on its own; the library picks Rice for integers, gzip for the others.
CompressedTableHdu packed = CompressedTableHdu.Compress(catalog);
Console.WriteLine(string.Join(", ", packed.ColumnAlgorithms));

// Or choose: here every column with gzip, and 1000 rows in each tile.
var options = new CompressedTableOptions { Algorithm = CompressionAlgorithm.Gzip2, TileRows = 1000 };
new FitsFile().Add(CompressedTableHdu.Compress(catalog, options)).Save("catalog-compressed.fits");

// Decompress gives the original table back, with its header.
using var again = FitsFile.Open("catalog-compressed.fits");
BinaryTableHdu table = ((CompressedTableHdu)again[1]).Decompress();
Console.WriteLine($"{table.RowCount} rows, {table.Header.Get<string>("EXTNAME")}");
```
<!-- /example -->

Tables use `Rice` (integer columns only), `Gzip1`, `Gzip2` or `NoCompress`; `Plio` and `HCompress` are for images and are refused with an `ArgumentException`. A [`CompressedTableHdu`](api/Eleris.FitsNet.CompressedTableHdu.md) cannot be edited: `AddColumn`, `SetColumns` and the other editing methods throw a `NotSupportedException`. Decompress it, edit the table, and compress it again.

[`CompressedTableOptions`](api/Eleris.FitsNet.Compression.CompressedTableOptions.md):

| Option | Default | Meaning |
| --- | --- | --- |
| `Algorithm` | Chosen per column | One algorithm for every column. Rice falls back to `Gzip2` for the columns it cannot handle. |
| `ColumnAlgorithms` | Empty | An algorithm for single columns, by zero-based index. It wins over `Algorithm`. Rice is refused for non-integer columns. |
| `TileRows` | 0 (about 2 MB per tile) | Rows in each tile. |
| `MaxDegreeOfParallelism` | -1 | Threads, as for images. |

## Limits

The limits of the compression (the smoothing of lossy `HCompress` data, the interoperability of compressed tables) are listed in [Standard coverage](standard-coverage.md#compression).
