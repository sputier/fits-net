# Eleris.FitsNet

Eleris.FitsNet is a .NET library to read, write and edit FITS files (Flexible Image Transport System), the format of astronomical images and tables. It is written in C# only: no native code and no dependency.

It covers the FITS 4.0 standard: images, ASCII and binary tables, random groups, tile compression of images and of tables, checksums, world coordinate systems, time coordinates and the conventions of the appendices. [Standard coverage](standard-coverage.md) lists what is supported and what is not.

## Why this library

- **Complete.** Every kind of HDU of the standard can be read, written and edited, with the details that real files need: unsigned integers, `BSCALE`/`BZERO`/`BLANK`, long strings, variable-length arrays, `HIERARCH` keywords.
- **Fast.** Files are opened lazily, so a file opens in milliseconds whatever the size of its images. Big-endian conversion uses vectorized instructions, and tiles are compressed and decompressed on all the cores. Cutouts read only the bytes they need.
- **Reliable.** A damaged or hostile file fails with a `FitsException`, never with an index, overflow or out-of-memory error. A file that is read and saved without a change is written back byte for byte. A save that fails leaves the file and the data in memory untouched. See [Reliability and limits](reliability.md).
- **Checked against other software.** The output is compared with astropy and wcslib (images, tables, compression, checksums, 27 celestial projections) and ERFA (time scales).

## A first look

The examples of this guide use a file called `m42.fits`: a 640 x 480 image of the sky with its coordinate keywords, followed by a small table of stars.

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

## Installation

Eleris.FitsNet targets .NET 8 and .NET 10 and has no dependency. The package is not on NuGet yet: add the project to your solution and reference it.

```shell
git clone https://github.com/sputier/fits-net.git
dotnet add reference fits-net/src/Eleris.FitsNet/Eleris.FitsNet.csproj
```

The classes live in three namespaces:

```csharp
using Eleris.FitsNet;              // files, HDUs, headers, tables
using Eleris.FitsNet.Compression;  // options of the tile compression
using Eleris.FitsNet.Wcs;          // world coordinates and time
```

## Where to go next

| Page | What it covers |
| --- | --- |
| [Getting started](getting-started.md) | The ideas to know, then reading, creating, editing a file |
| [Files and HDUs](files.md) | Opening, reading, saving, and the structure of a file |
| [Headers](headers.md) | Keywords, values, comments, long strings, `HIERARCH` |
| [Images](images.md) | Pixels, scaling, regions, unsigned integers, random groups |
| [Tables](tables.md) | Binary and ASCII tables, every column type, editing |
| [Compression](compression.md) | Tile compression of images and tables |
| [Checksums](checksums.md) | `CHECKSUM` and `DATASUM` |
| [World coordinates](wcs.md) | Pixels to sky, spectral axes, alternate descriptions |
| [Time](time.md) | Time scales, ISO 8601, the time keywords |
| [Conventions](conventions.md) | Header inheritance, Green Bank, Stokes, extension names |
| [Reliability and limits](reliability.md) | Errors, limits for untrusted files, threads, memory |
| [Standard coverage](standard-coverage.md) | Section by section status against FITS 4.0 |
| [API reference](api/index.md) | Every public type and member |
