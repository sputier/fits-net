# Getting started

## What a FITS file contains

A FITS file is a sequence of **HDUs** (header and data units). Each HDU has:

- a **header**: a list of cards, each a `KEYWORD = value / comment` line of 80 characters, that describes the data and everything around it (the target, the instrument, the date, the coordinates...);
- some **data**: an image, a table, or nothing.

The first HDU is the **primary HDU**. The following ones are **extensions**. In Eleris.FitsNet, the file is a [`FitsFile`](api/Eleris.FitsNet.FitsFile.md), an HDU is a [`Hdu`](api/Eleris.FitsNet.Hdu.md), and the header is a [`FitsHeader`](api/Eleris.FitsNet.FitsHeader.md).

| Kind of HDU | Class | Holds |
| --- | --- | --- |
| Image (primary or `IMAGE` extension) | [`ImageHdu`](api/Eleris.FitsNet.ImageHdu.md) | An array of 0 to 999 dimensions |
| Random groups (primary only) | [`RandomGroupsHdu`](api/Eleris.FitsNet.RandomGroupsHdu.md) | Many small arrays, each with parameters |
| Binary table (`BINTABLE`) | [`BinaryTableHdu`](api/Eleris.FitsNet.BinaryTableHdu.md) | Rows and typed columns |
| ASCII table (`TABLE`) | [`AsciiTableHdu`](api/Eleris.FitsNet.AsciiTableHdu.md) | Rows and columns written as text |
| Compressed image | [`CompressedImageHdu`](api/Eleris.FitsNet.CompressedImageHdu.md) | A tile-compressed image, stored in a binary table |
| Compressed table | [`CompressedTableHdu`](api/Eleris.FitsNet.CompressedTableHdu.md) | A tile-compressed binary table |
| Anything else | [`GenericHdu`](api/Eleris.FitsNet.GenericHdu.md) | The bytes, kept as they are |

All the HDUs are classes of the same hierarchy, so `file[1]` gives a [`Hdu`](api/Eleris.FitsNet.Hdu.md) that you cast to the kind you expect, or test with `is`.

## Read a file

[`FitsFile.Open`](api/Eleris.FitsNet.FitsFile.md) opens a file on disk. It reads the headers at once and leaves the data on disk until you ask for it, so opening a very large file costs almost nothing. Dispose the file when you are done.

<!-- example: Start.Inspect -->
```csharp
// Open reads the headers now and the data when asked: opening a large file is instantaneous.
using var file = FitsFile.Open("m42.fits");

foreach (Hdu hdu in file)
    Console.WriteLine($"{hdu.GetType().Name}: {hdu.Header.Count} cards, {hdu.DataLength} bytes of data");

var image = (ImageHdu)file.Primary;
Console.WriteLine($"{image.Axes[0]} x {image.Axes[1]} pixels, BITPIX {(int)image.BitPix}");
Console.WriteLine($"Target: {image.Header.Get<string>("OBJECT")}, exposure {image.Header.Get<double>("EXPTIME")} s");

ushort[] pixels = image.ReadPixels<ushort>();
Console.WriteLine($"First pixel: {pixels[0]}");
```
<!-- /example -->

Pixels are read with `ReadPixels<T>()`. `float` and `double` always work: `BSCALE` and `BZERO` are applied and undefined pixels become `NaN`. An integer type works when it is the stored type (or the unsigned type of the standard convention) and no scaling is needed; otherwise an `InvalidOperationException` tells you to use `float` or `double`. [Images](images.md) explains the details.

## Read a table

<!-- example: Start.Table -->
```csharp
using var file = FitsFile.Open("m42.fits");

var catalog = (BinaryTableHdu)file[1];
Console.WriteLine($"{catalog.RowCount} rows, columns: {string.Join(", ", catalog.Columns.Select(c => c.Name))}");

double[] ra = catalog.ReadColumn<double>("RA");
float[] magnitude = catalog.ReadColumn<float>("MAG");
string[] names = catalog.ReadStringColumn("NAME");
for (int row = 0; row < catalog.RowCount; row++)
    Console.WriteLine($"{names[row]}: RA {ra[row]} deg, magnitude {magnitude[row]}");
```
<!-- /example -->

Columns are read by name or by zero-based index. See [Tables](tables.md).

## Create a file

To create a file, create the HDUs, then add them to a new `FitsFile` and save it.

<!-- example: Start.Create -->
```csharp
// 256 x 256 pixels, the first axis (x) varying fastest.
var pixels = new short[256 * 256];
for (int y = 0; y < 256; y++)
    for (int x = 0; x < 256; x++)
        pixels[y * 256 + x] = (short)(x + y);

var image = ImageHdu.Create<short>(pixels, 256, 256);
image.Header.Set("OBJECT", "test pattern", "name of the target");

new FitsFile().Add(image).Save("pattern.fits");
```
<!-- /example -->

The pixel type decides the format of the data: `short` gives 16-bit integers, `float` gives 32-bit floating point numbers, and so on. The first axis varies fastest, as in FITS.

## Edit a file

Open the file, change what you want, and save it, to the same path or to another one.

<!-- example: Start.Edit -->
```csharp
using var file = FitsFile.Open("m42.fits");

file.Primary.Header
    .Set("OBSERVER", "A. Astronomer", "who took the image")
    .AddHistory("Header completed with Eleris.FitsNet");

// The new file is written completely before it replaces the target, so a failure never leaves half a file.
file.Save("m42-edited.fits");
```
<!-- /example -->

The pixels are not loaded to edit a header: with `Open`, the data stays on disk and is copied to the new file in blocks.

## Use the coordinates

If the header has the keywords of a world coordinate system, [`WorldCoordinateSystem`](api/Eleris.FitsNet.Wcs.WorldCoordinateSystem.md) converts pixel positions to positions on the sky and back.

<!-- example: Start.Coordinates -->
```csharp
using var file = FitsFile.Open("m42.fits");

// The WCS keywords of the header link pixels to the sky. Pixel numbers start at 1, as in the FITS standard.
var wcs = file.Primary.GetWorldCoordinateSystem();
double[] sky = wcs.PixelToWorld([100.0, 200.0]);
Console.WriteLine($"Pixel (100, 200) is at RA {sky[0]:F4} deg, Dec {sky[1]:F4} deg");

double[] pixel = wcs.WorldToPixel([83.82, -5.39]);
Console.WriteLine($"M42 is at pixel ({pixel[0]:F1}, {pixel[1]:F1})");
```
<!-- /example -->

## Conventions to remember

- **Pixel numbers.** In the header and in the world coordinate functions, the center of the first pixel is `1.0`, as in the standard. In the arrays you read or write, indexes are zero-based as usual in .NET, and so are `origin` arguments.
- **Axis order.** The first axis varies fastest in memory: for an image of width `w`, the pixel `(x, y)` is at index `y * w + x`.
- **Errors.** A file that is not valid FITS throws a [`FitsException`](api/Eleris.FitsNet.FitsException.md). Wrong arguments throw the usual `ArgumentException` family. See [Reliability and limits](reliability.md).
- **Threads.** Reading from a [`FitsFile`](api/Eleris.FitsNet.FitsFile.md) from several threads at once is not supported; compression uses several threads on its own.
