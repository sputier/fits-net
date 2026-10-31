# Reliability and limits

## Errors

| Situation | Exception |
| --- | --- |
| A file that is not valid FITS: bad structure, truncated data, inconsistent keywords, damaged compressed data | [`FitsException`](api/FitsNet.FitsException.md) |
| A feature of a file that the library does not handle (a projection or algorithm outside the standard, a time conversion that needs outside data) | `NotSupportedException` |
| A wrong argument: a region outside the image, a bad keyword, an array of the wrong length | `ArgumentException`, `ArgumentOutOfRangeException` |
| A call that does not fit the state of the object: reading a scaled column as an integer, reading a column with the wrong method | `InvalidOperationException` |
| A bad datetime string given to `FitsTime.Parse` | `FormatException` |
| A file that cannot be read or written | `IOException` and its subclasses |
| Use after `Dispose` | `ObjectDisposedException` |

A damaged or hostile file fails with a `FitsException`, a `NotSupportedException` or an `ArgumentException`. It is not meant to fail with an `IndexOutOfRangeException`, an `OverflowException` or an `OutOfMemoryException`, and the library refuses sizes announced by a header that the file cannot back up before it allocates for them. The test suite includes fuzzing of the readers; report any other exception as a bug.

<!-- example: Reliability.Errors -->
```csharp
// A file that is not FITS, damaged or hostile fails with a FitsException, never with an index or memory error.
byte[] garbage = new byte[5760];
try
{
    FitsFile.Read(new MemoryStream(garbage));
}
catch (FitsException e)
{
    Console.WriteLine($"Rejected: {e.Message}");
}

// Mistakes of the calling code are the usual .NET exceptions.
try
{
    var image = ImageHdu.Create<short>(new short[4], 2, 2);
    image.ReadRegion<short>(origin: [0, 0], shape: [3, 3]);     // larger than the image
}
catch (ArgumentException e)
{
    Console.WriteLine($"Refused: {e.GetType().Name}");
}
```
<!-- /example -->

### Truncated files

The header of an HDU says how many bytes of data follow. A file that stops earlier is refused with a `FitsException`, not silently shortened. The only tolerated defect is the missing padding at the very end of the file, unless `Strict` is set.

<!-- example: Reliability.Truncated -->
```csharp
byte[] bytes = File.ReadAllBytes("m42.fits");
byte[] half = bytes[..(bytes.Length / 2)];

// The header says how much data follows: a file that stops earlier is reported, not silently shortened.
try
{
    FitsFile.Read(new MemoryStream(half));
}
catch (FitsException e)
{
    Console.WriteLine(e.Message);
}
```
<!-- /example -->

## Limits for untrusted files

The defaults suit files that you trust. For files you do not control, set the limits of [`FitsReadOptions`](api/FitsNet.FitsReadOptions.md) to what your application needs, as in [Files and HDUs](files.md#reading-options).

| Limit | Default | Protects against |
| --- | --- | --- |
| `MaxHeaderBlocks` | 10 000 blocks (28.8 MB) | A header that never ends |
| `MaxTotalHeaderBlocks` | 50 000 blocks | Millions of tiny extensions |
| `MaxInputBytes` | 2 147 483 591 | A stream that is too large to hold in memory (`FitsFile.Read` only) |

Other fixed limits:

- An array that is read in one piece (pixels, a column) cannot exceed what a .NET array holds, about 2 GB. Larger data is read by regions (`ReadRegion`), which avoids loading it.
- A FITS array has at most 999 axes, a table at most 999 columns.
- A region larger than 2 GB is refused with an `ArgumentException`.

## Memory

- `FitsFile.Open` reads the headers only. Data is read when you ask for it, and a region of an image reads only its bytes. Opening a file costs memory proportional to its headers, not to its size.
- `FitsFile.Read` holds the whole file in memory.
- `ReadPixels`, `ReadColumn` and the like return a new array each time. Keep the result if you use it again.
- Compression keeps a bounded number of tiles in flight, and reduces the parallelism when the tiles are very large.

## Dispose

`FitsFile.Open` keeps the file open. Dispose the `FitsFile` when you have finished, with `using`. After that, an HDU that was waiting for its data on disk cannot read it any more.

<!-- example: Reliability.Dispose -->
```csharp
// Open keeps the file open for lazy reading: dispose it. After Dispose the HDUs cannot read their data any more.
ImageHdu image;
using (var file = FitsFile.Open("m42.fits"))
{
    image = (ImageHdu)file.Primary;
    Console.WriteLine(image.ReadPixels<ushort>().Length);       // fine: the file is open
}

// Read has no such constraint: it loads everything in memory and the stream is yours.
using var loaded = FitsFile.Read(new MemoryStream(File.ReadAllBytes("m42.fits")));
```
<!-- /example -->

## Failed saves

A save that fails leaves the target file as it was and leaves the headers and HDUs in memory untouched, so you can fix the problem and save again. See [Files and HDUs](files.md#saving) for the case of overwriting the file you opened.

<!-- example: Reliability.FailedSave -->
```csharp
// A failed save leaves the file and the HDUs as they were: a Save to a path writes a temporary file first.
var image = ImageHdu.Create<short>(new short[4], 2, 2);
var file = new FitsFile().Add(image);
try
{
    file.Save(Path.Combine("no-such-directory", "out.fits"));
}
catch (IOException e)
{
    Console.WriteLine($"{e.GetType().Name}: nothing was written, header still has {image.Header.Count} cards");
}
```
<!-- /example -->

## Threads

A [`FitsFile`](api/FitsNet.FitsFile.md) is not thread-safe. Use it from one thread at a time, which includes the HDUs and headers that belong to it. To read in parallel, open the file once per thread.

The library uses threads on its own only for the tile compression and decompression, controlled by `MaxDegreeOfParallelism`; the result never depends on the number of threads.

[`FitsTime`](api/FitsNet.Wcs.FitsTime.md) is an immutable value type.

## Faithful copies

A file that is read and saved without a change is written back byte for byte, with the cards and comments as they were written. A header you modify is rewritten, with the same card order. The library never recomputes a checksum that is wrong in an HDU you did not touch, since that would hide the damage: see [Checksums](checksums.md).
