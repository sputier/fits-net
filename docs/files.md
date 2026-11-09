# Files and HDUs

## Open or Read

There are two ways to load a file, with different costs.

| | [`FitsFile.Open(path)`](api/Eleris.FitsNet.FitsFile.md) | [`FitsFile.Read(stream)`](api/Eleris.FitsNet.FitsFile.md) |
| --- | --- | --- |
| Source | A file on disk | Any readable stream |
| Reads at once | The headers | Everything |
| Data | Read from the file when asked | Already in memory |
| Memory | Small, whatever the size of the file | The size of the file |
| Size limit | None for images read by region; 2 GB for one array | 2 GB (the size of a .NET array) |
| You must | Dispose the file | Dispose it (nothing is held, but it is cheap) and close your stream |

Use `Open` for files on disk, especially large ones. Use `Read` for data that does not come from a file: a download, a database column, a test.

<!-- example: Files.ReadStream -->
```csharp
// Read loads the whole file in memory; the stream stays open and belongs to you.
byte[] bytes = File.ReadAllBytes("m42.fits");
using var stream = new MemoryStream(bytes);

using var file = FitsFile.Read(stream);
Console.WriteLine($"{file.Count} HDUs");
```
<!-- /example -->

With `Open`, the file stays open as long as the [`FitsFile`](api/Eleris.FitsNet.FitsFile.md) is not disposed, and the HDUs read their data from it when you ask. Reading a region reads only the bytes of the region:

<!-- example: Files.Lazy -->
```csharp
// Open keeps the file open and reads the data only when you ask for it.
using var file = FitsFile.Open("m42.fits");
var image = (ImageHdu)file.Primary;

// Reading a small region touches only the bytes it needs, however large the image is.
ushort[] corner = image.ReadRegion<ushort>(origin: [0, 0], shape: [8, 8]);
Console.WriteLine($"{corner.Length} pixels read");
```
<!-- /example -->

After `Dispose`, the HDUs that were read lazily cannot read their data any more. Keep the file open as long as you use them.

## The structure of a file

A [`FitsFile`](api/Eleris.FitsNet.FitsFile.md) is a list of HDUs. It can be indexed (`file[0]`), counted (`file.Count`), enumerated (`foreach`), and changed with `Add`, `Insert`, `Remove` and `RemoveAt`; `Add` and `Insert` return the file, so calls can be chained. `Primary` is the first HDU. The rules of the standard are applied for you: the primary HDU must be an image or random groups, so adding a table to an empty file puts an empty image in front of it.

<!-- example: Files.Structure -->
```csharp
var file = new FitsFile();

// The first HDU is the primary HDU. A table cannot be primary, so an empty image is added in front of it when needed.
file.Add(BinaryTableHdu.Create([TableColumn.Create<int>("ID", [1, 2, 3])]));
Console.WriteLine(file.Count);                           // 2: the empty primary image and the table
Console.WriteLine(file.Primary is ImageHdu);             // True

// Extensions can be inserted, removed and looked up like in a list.
file.Add(ImageHdu.Create<byte>(new byte[16], 4, 4));
file.RemoveAt(1);
Hdu last = file[file.Count - 1];
Console.WriteLine(last.GetType().Name);                  // ImageHdu

// Find an extension by its EXTNAME.
Hdu? catalog = file.FirstOrDefault(h => h.Header.Get<string>("EXTNAME") == "CATALOG");
Console.WriteLine(catalog is null);                      // True
```
<!-- /example -->

Rules enforced for you, each with an `ArgumentException` or `InvalidOperationException` that explains it:

- the same HDU cannot be added twice to a file;
- a [`RandomGroupsHdu`](api/Eleris.FitsNet.RandomGroupsHdu.md) can only be the primary HDU, and a file whose primary HDU is random groups cannot get another HDU in front of it;
- removing the primary HDU is refused when the next HDU could not become the primary one (a table, for example): replace it with an empty image instead.

An HDU can be taken from one file and added to another. If its data was still waiting on disk in the first file, it is copied into memory at that moment, so the HDU keeps working after the first file is disposed.

## Saving

[`FitsFile.Save`](api/Eleris.FitsNet.FitsFile.md) writes the file to a stream or to a path.

<!-- example: Files.SaveStream -->
```csharp
using var file = FitsFile.Open("m42.fits");

using var output = new MemoryStream();
file.Save(output);

byte[] bytes = output.ToArray();
Console.WriteLine($"{bytes.Length} bytes, a multiple of {FitsHeader.BlockSize}: {bytes.Length % FitsHeader.BlockSize == 0}");
```
<!-- /example -->

When you save to a path, the data is first written to a temporary file next to the target, flushed, and only then moved over the target. The target is never seen half-written, and a failure at any point leaves it as it was. Symbolic links are followed and written through, and an existing file keeps its attributes and permissions.

Saving over the file you opened is allowed. The lazily read HDUs are attached to the new file, so the objects you hold keep working:

<!-- example: Files.Overwrite -->
```csharp
File.Copy("m42.fits", "m42-copy.fits", overwrite: true);

// Saving over the file that is open is safe: the new content is written to a temporary file, then moved in place.
using var file = FitsFile.Open("m42-copy.fits");
file.Primary.Header.Set("OBSERVER", "A. Astronomer");
file.Save("m42-copy.fits");

// The instance is still usable after the save: it reads from the new file.
Console.WriteLine(((ImageHdu)file.Primary).ReadPixels<ushort>().Length);
```
<!-- /example -->

A failed save throws an `IOException` and leaves the headers and the state of the HDUs as they were before the call. When you overwrite the file you opened from and the new file cannot be put in place or reopened (for example because another program holds the target), the message may say that this instance can no longer read its data: dispose the [`FitsFile`](api/Eleris.FitsNet.FitsFile.md) and open the file again. HDUs that you removed from the file before saving lose access to their data in that case.

A file that is read and saved without a change is written back byte for byte, including the cards and comments as they were written.

### Never replace a file

`Save(path, checksums, FitsSaveMode.CreateNew)` refuses to replace anything. When something exists at the path, the save throws a [`FitsFileExistsException`](api/Eleris.FitsNet.FitsFileExistsException.md), an `IOException` that gives the path in `FileName`. The check is not a test made before writing, which another program could beat: the temporary file is moved to the path by an operation that fails if the path exists. A file created by someone else while the save runs is therefore kept, and the save fails. (On Linux and macOS, .NET makes this move with a hard link, so the guarantee holds on the file systems that support them, which are the usual ones.) In every case nothing is replaced, no temporary file is left, and the headers and the HDUs are as they were before the call. `FitsSaveMode.Overwrite` is the behaviour of the other overloads.

<!-- example: Files.CreateNew -->
```csharp
using var file = FitsFile.Open("m42.fits");
file.Primary.Header.Set("OBSERVER", "A. Astronomer");

// CreateNew never replaces a file, even one that another program creates while the file is being written.
file.Save("m42-reduced.fits", ChecksumPolicy.Auto, FitsSaveMode.CreateNew);
try
{
    file.Save("m42-reduced.fits", ChecksumPolicy.Auto, FitsSaveMode.CreateNew);
}
catch (FitsFileExistsException e)
{
    Console.WriteLine($"Not replaced: {Path.GetFileName(e.FileName)}");   // Not replaced: m42-reduced.fits
}
```
<!-- /example -->

In this mode a symbolic link at the path is an existing file, even when its target does not exist: nothing is written through it.

### Cancellation

The long operations take a `CancellationToken` in an overload of their own: `ReadPixels<T>`, `ReadRawPixels<T>`, `ReadRegion<T>`, `ReadRawRegion<T>` and `Subimage` of an [`ImageHdu`](api/Eleris.FitsNet.ImageHdu.md), `Decompress`, `ReadPixels<T>` and `ReadRawPixels<T>` of a [`CompressedImageHdu`](api/Eleris.FitsNet.CompressedImageHdu.md), and `Save`. The token is checked between blocks of data (and between tiles of a compressed image), and a cancelled operation throws an `OperationCanceledException`.

<!-- example: Files.Cancel -->
```csharp
// A token stops a long read or save between two blocks of data: give up after 30 seconds, or when the user asks.
using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var file = FitsFile.Open("m42.fits");
var image = (ImageHdu)file.Primary;
try
{
    float[] pixels = image.ReadPixels<float>(cancel.Token);
    ushort[] rows = image.ReadRegion<ushort>(origin: [0, 100], shape: [640, 16], cancel.Token);
    file.Save("m42-saved.fits", ChecksumPolicy.Auto, cancel.Token);
    Console.WriteLine($"{pixels.Length} pixels read, {rows.Length} in the rows, file saved");
}
catch (OperationCanceledException)
{
    // A cancelled save leaves no file behind, and an existing target as it was.
    Console.WriteLine("Cancelled");
}
```
<!-- /example -->

A cancelled save to a path deletes its temporary file: the target, if there was one, is as it was, and the headers and the HDUs are as they were before the call. The token is checked until the temporary file is complete; the move that puts it in place is not interrupted. A cancelled save to a stream also restores the headers, but the stream may already hold the beginning of the file.

### Asynchronous I/O

`ReadPixelsAsync<T>`, `ReadRawPixelsAsync<T>`, `ReadRegionAsync<T>` and `ReadRawRegionAsync<T>` of an [`ImageHdu`](api/Eleris.FitsNet.ImageHdu.md), `ReadPixelsAsync<T>` and `ReadRawPixelsAsync<T>` of a [`CompressedImageHdu`](api/Eleris.FitsNet.CompressedImageHdu.md), and `FitsFile.SaveAsync` to a stream or to a path, are the asynchronous versions of the methods above, with the same results, byte for byte, and the same guarantees. The data of a file opened with `Open` is read with asynchronous I/O, and a save writes every block with `WriteAsync` (a save to a path opens its temporary file for asynchronous I/O). The conversion of the pixels, the checksums and the decompression of the tiles stay synchronous: they run between two reads or writes. Flushing the temporary file to the disk and moving it in place have no asynchronous version in the system and are done synchronously.

<!-- example: Files.Async -->
```csharp
// The file is read and written with asynchronous I/O; the conversion of the pixels runs between two reads or writes.
using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var file = FitsFile.Open("m42.fits");
var image = (ImageHdu)file.Primary;

float[] pixels = await image.ReadPixelsAsync<float>(cancel.Token);
ushort[] rows = await image.ReadRegionAsync<ushort>(origin: [0, 100], shape: [640, 16], cancel.Token);
Console.WriteLine($"{pixels.Length} pixels, {rows.Length} in the rows");

// The same guarantees as Save: a temporary file, no file left after a failure, and CreateNew never replaces a file.
await file.SaveAsync("m42-async.fits", ChecksumPolicy.Auto, FitsSaveMode.CreateNew, cancel.Token);
```
<!-- /example -->

How the system carries out the asynchronous I/O depends on the platform. On Windows, the temporary file of a save is written with overlapped I/O. A file opened with `Open` keeps a synchronous handle, which keeps the synchronous reads fast, so .NET carries out its asynchronous reads on its thread pool: that is also what .NET does for every file on Linux and macOS. Either way the calling thread does not wait for the file. Work that does not wait for I/O runs on the thread that calls or continues the method: the conversion of each block, the decompression of the tiles, and the whole read of data that is already in memory (`FitsFile.Read`, `Create`, `CreateFromPlanes`), which returns a completed task. Flushing a large temporary file to the disk can take a noticeable time. Do not use the file or its HDUs until the task has completed.

## Checksums when saving

The second argument of `Save` is a [`ChecksumPolicy`](api/Eleris.FitsNet.ChecksumPolicy.md): `Auto` (the default) keeps up to date the checksums that already exist, `Always` writes them in every HDU, `Never` removes them. See [Checksums](checksums.md).

## Reading options

[`FitsReadOptions`](api/Eleris.FitsNet.FitsReadOptions.md) is the optional last argument of `Open`, `Read` and `FitsHeader.Read`.

| Option | Default | Meaning |
| --- | --- | --- |
| `Strict` | `false` | `true` refuses the deviations from the standard that the library otherwise tolerates, such as a last HDU without its final padding or `SIMPLE = F`. `false` accepts them, as other software does. Data that stops before the end announced by the header is refused in both modes. |
| `MaxHeaderBlocks` | 10 000 | The largest header, in blocks of 2880 bytes. Protects against a header that never ends. |
| `MaxTotalHeaderBlocks` | 50 000 | The headers of the whole file together; it also bounds the number of extensions. |
| `MaxInputBytes` | 2 147 483 591 | `Read(Stream)` only: the most bytes taken from the stream. A stream that is not seekable is copied block by block and refused as soon as it passes the limit. |

<!-- example: Files.Options -->
```csharp
// Strict reading refuses anything that deviates from the standard instead of tolerating it.
var strict = new FitsReadOptions(Strict: true);
using (FitsFile.Open("m42.fits", strict)) { }

// The limits protect a program that reads files it does not control.
var careful = new FitsReadOptions(
    MaxHeaderBlocks: 100,          // one header: at most 100 blocks of 2880 bytes
    MaxTotalHeaderBlocks: 1_000,   // all the headers of the file together
    MaxInputBytes: 100_000_000);   // Read(Stream): at most 100 MB taken from the stream

try
{
    using var stream = File.OpenRead("m42.fits");
    using var file = FitsFile.Read(stream, careful);
    Console.WriteLine($"{file.Count} HDUs");
}
catch (FitsException e)
{
    Console.WriteLine($"Not accepted: {e.Message}");
}
```
<!-- /example -->

Defaults are tuned for files you trust. For files you do not control, lower the limits as in the example above; see [Reliability and limits](reliability.md).
