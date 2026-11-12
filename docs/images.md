# Images

An [`ImageHdu`](api/Eleris.FitsNet.ImageHdu.md) holds an array of 0 to 999 dimensions. The primary HDU of most files is an image, and so are the `IMAGE` extensions.

## Create an image

`ImageHdu.Create<T>` takes the pixels and the length of each axis. The first axis varies fastest: for a 100 x 50 image, the pixel `(x, y)` is at index `y * 100 + x`. The pixel type decides `BITPIX`:

| C# type | `BITPIX` | Stored as |
| --- | --- | --- |
| `byte` | 8 | Unsigned 8-bit |
| `short` | 16 | Signed 16-bit |
| `int` | 32 | Signed 32-bit |
| `long` | 64 | Signed 64-bit |
| `float` | -32 | IEEE single |
| `double` | -64 | IEEE double |
| `sbyte`, `ushort`, `uint`, `ulong` | 8, 16, 32, 64 | Signed type with a `BZERO` offset, see [below](#unsigned-integers) |

<!-- example: Images.Types -->
```csharp
// The pixel type decides BITPIX: byte -> 8, short -> 16, int -> 32, long -> 64, float -> -32, double -> -64.
var floats = ImageHdu.Create<float>(new float[100 * 50], 100, 50);
Console.WriteLine(floats.BitPix);                  // Single

// A 3-dimensional cube: 64 x 64 pixels, 10 planes. The first axis varies fastest.
var cube = ImageHdu.Create<ushort>(new ushort[64 * 64 * 10], 64, 64, 10);
Console.WriteLine(string.Join(" x ", cube.Axes));  // 64 x 64 x 10
Console.WriteLine(cube.PixelCount);                // 40960

// An image can be empty (no axes): a primary HDU that carries only a header.
var empty = new ImageHdu();
Console.WriteLine(empty.Axes.Count);              // 0
```
<!-- /example -->

### Plane by plane

`ImageHdu.CreateFromPlanes<T>` makes an image from planes held in separate buffers, such as the channels of a color image, without putting them together in one array. A plane is all the pixels that share one index on the last axis: one `ReadOnlyMemory<T>` per plane, in order, each with the length of the other axes multiplied together (a 2D image can be given row by row in the same way). The file written is byte for byte the one `Create<T>` writes for the same pixels.

<!-- example: Images.Planes -->
```csharp
// Three planes computed separately, for example the red, green and blue channels of a color image.
const int width = 64, height = 48;
float[] red = new float[width * height], green = new float[width * height], blue = new float[width * height];
Array.Fill(green, 0.5f);

// The planes are not copied into one array: they are read when the image is saved (or its pixels read).
var color = ImageHdu.CreateFromPlanes<float>([red, green, blue], width, height, 3);
Console.WriteLine(string.Join(" x ", color.Axes));   // 64 x 48 x 3
new FitsFile().Add(color).Save("color.fits");
```
<!-- /example -->

The planes are not copied, not even into the bytes of the file: they are converted, a block at a time, each time the data is needed, which is when the file is saved, its checksums computed or its pixels read. The image keeps them after a save. So they must stay valid and unchanged as long as the image uses them, until it is discarded or `SetPixels<T>` has replaced its data: do not return a pooled buffer before, and do not change a plane during a save, which reads the planes once for the checksums and once for the file. A large cube is then held in memory once, in the buffers you already have. An array of arrays converts to the list of planes with `[.. planes.Select(p => (ReadOnlyMemory<float>)p)]`.

### Band by band

An image larger than the memory, or computed a few rows at a time, is written with a [`FitsImageWriter<T>`](api/Eleris.FitsNet.FitsImageWriter-1.md) instead: the header is given first, then the rows, a band of consecutive rows at a time, and the image never exists in memory as a whole. A row is `NAXIS1` values, a plane is `NAXIS2` rows, and the planes follow (the `NAXIS3` planes of a cube). The bands are given in the order of the file, plane 0 from its first row to its last, then plane 1, and so on; a band can have any number of rows of one plane. `Complete` then publishes the file.

<!-- example: Images.Bands -->
```csharp
// An image computed 64 rows at a time and never held whole in memory: a mosaic, for example.
const int width = 1000, height = 750, bandHeight = 64;
var header = new FitsHeader()
    .Set("BITPIX", -32).Set("NAXIS", 2).Set("NAXIS1", width).Set("NAXIS2", height)
    .Set("OBJECT", "M42 mosaic");

// The rows go to a temporary file; the file appears at its path only when Complete succeeds.
using var writer = FitsImageWriter<float>.Create("mosaic.fits", header, ChecksumPolicy.Always, FitsSaveMode.CreateNew);
var band = new float[width * bandHeight];
for (int row = 0; row < height; row += bandHeight)
{
    int rows = Math.Min(bandHeight, height - row);
    Span<float> values = band.AsSpan(0, rows * width);
    for (int i = 0; i < values.Length; i++) values[i] = row + i / width;   // compute the rows of the band here
    writer.WriteRows(plane: 0, firstRow: row, values);
}
writer.Complete();
```
<!-- /example -->

The header holds `BITPIX`, `NAXIS` and the axes, and any other card, written as given: `BSCALE`, `BZERO`, `BLANK`, a WCS. `SIMPLE` may be left out, and the header of an image you have, `image.Header`, can be given as it is. `BITPIX` must be the one of the type of the values, as in the table above, and the values are the stored ones: for `sbyte`, `ushort`, `uint` and `ulong` the header must have the `BZERO` of the convention, which `ImageHdu.Create<T>` writes. The file is byte for byte the one `Save` writes for an image of the same header and pixels, `CHECKSUM` and `DATASUM` included: they are summed while the rows are written, and filled in before the file is published. They are always computed for the rows given: sums already in the header are replaced (with `ChecksumPolicy.Auto`) or removed (with `Never`), never kept.

The guarantees are those of [saving to a path](files.md#saving): the file is written to a temporary file next to the target, which is only replaced, or in `FitsSaveMode.CreateNew` mode only created, by `Complete` (with the limits that [`CreateNew`](files.md#never-replace-a-file) has on Linux and macOS). Unlike `Save`, the writer cannot attach an open `FitsFile` to the new file: do not write over a file that a `FitsFile` is reading. Rows out of order, a band that is not whole rows or that goes past the end of its plane, and a `Complete` with rows missing throw an exception. Any exception, a cancellation through the `CancellationToken` of `WriteRows` or `Complete` included, abandons the write: the temporary file is deleted, an existing target is left as it was, and the writer can no longer be used. So does disposing of the writer before `Complete`. The writer keeps no band: each one is converted to the bytes of the file through a buffer of 256 KB, so the memory it uses does not depend on the number of rows. Writing a 32 MB image this way allocates about 100 KB.

## Read pixels

Two methods read the whole data:

- `ReadPixels<T>()` returns **physical** values: `BZERO + BSCALE * stored`.
- `ReadRawPixels<T>()` returns the **stored** values. `T` must be the type of `BITPIX`, otherwise an `InvalidOperationException` is thrown.

For `ReadPixels<T>()`, `float` and `double` always work. An integer type works when it is the type of `BITPIX` and the data is not scaled, or when it is the unsigned type of the standard convention. Any other combination throws an `InvalidOperationException` that asks for `float` or `double`. Reading 64-bit integers as `double` loses precision above 2^53.

### Scaling

<!-- example: Images.Scaling -->
```csharp
// Physical value = BZERO + BSCALE * stored value. ReadPixels applies it, ReadRawPixels does not.
var image = ImageHdu.Create<short>([0, 100, 200, 300], 4);
image.Header.Set("BSCALE", 0.5).Set("BZERO", 10.0);

double[] physical = image.ReadPixels<double>();    // 10 60 110 160
short[] stored = image.ReadRawPixels<short>();     // 0 100 200 300
Console.WriteLine($"{physical[1]} from {stored[1]}");
```
<!-- /example -->

### Undefined pixels

`BLANK` is the stored value that means "undefined" in an integer image. `ReadPixels<float>()` and `ReadPixels<double>()` turn it into `NaN`. Integer reads, and `ReadRawPixels`, return the value as it is, since an integer has no `NaN`.

<!-- example: Images.Blank -->
```csharp
// BLANK is the stored value that means "undefined" in an integer image. Read as float or double, it becomes NaN.
var image = ImageHdu.Create<short>([10, -1, 30], 3);
image.Header.Set("BLANK", -1);

float[] values = image.ReadPixels<float>();
Console.WriteLine(float.IsNaN(values[1]));          // True
```
<!-- /example -->

### Unsigned integers

FITS has no unsigned integers. The convention is to store them as signed values with an offset: `ushort` as `short` with `BZERO = 32768`, and likewise `uint`, `ulong`, and `sbyte` (`byte` with `BZERO = -128`). The library applies the convention in both directions.

<!-- example: Images.Unsigned -->
```csharp
// FITS has no unsigned integers: ushort pixels are stored as signed 16-bit values with BZERO = 32768.
// The library applies the convention on both sides, so you only ever see ushort.
var image = ImageHdu.Create<ushort>([0, 1, 40000, 65535], 4);
Console.WriteLine(image.BitPix);                                  // Int16
Console.WriteLine(image.Header.Get<double>("BZERO"));             // 32768

ushort[] physical = image.ReadPixels<ushort>();                   // 0 1 40000 65535
short[] stored = image.ReadRawPixels<short>();                    // -32768 -32767 7232 32767
Console.WriteLine($"{physical[3]} is stored as {stored[3]}");
```
<!-- /example -->

## Regions

On a file opened with `FitsFile.Open`, `ReadRegion<T>` reads only the bytes of the region, so a cutout of an image of any size costs what the cutout costs. `origin` and `shape` are zero-based and have one entry per axis, the first axis first. The region is limited to 2 GB.

`Subimage` returns the region as a new [`ImageHdu`](api/Eleris.FitsNet.ImageHdu.md). It keeps the header, moves `CRPIXn` (and the alternate descriptions) and the IRAF `LTVn` offsets so that the coordinates stay right, and drops `CHECKSUM` and `DATASUM`, which would not be valid any more. `ReadRawRegion<T>` is the stored-value version of `ReadRegion<T>`.

<!-- example: Images.Regions -->
```csharp
using var file = FitsFile.Open("m42.fits");
var image = (ImageHdu)file.Primary;

// Origin and shape are zero-based, one entry per axis, the first axis first.
ushort[] cutout = image.ReadRegion<ushort>(origin: [100, 50], shape: [32, 16]);
Console.WriteLine(cutout.Length);                   // 512

// Subimage gives a new image HDU for the region, with the WCS reference pixel moved accordingly.
ImageHdu crop = image.Subimage(origin: [100, 50], shape: [32, 16]);
Console.WriteLine($"{crop.Axes[0]} x {crop.Axes[1]}");
Console.WriteLine(crop.Header.Get<double>("CRPIX1"));   // 320.5 - 100 = 220.5
new FitsFile().Add(crop).Save("m42-crop.fits");
```
<!-- /example -->

## Replace the data

`SetPixels<T>` replaces the pixels, the type and the shape of an existing image. The other cards of the header are kept, except `BSCALE`, `BZERO` and `BLANK`, which describe the old data and are reset (`BZERO` is set again for the unsigned types).

<!-- example: Images.SetPixels -->
```csharp
var image = ImageHdu.Create<short>(new short[4], 2, 2);
image.Header.Set("OBJECT", "kept");

// SetPixels replaces the data (and the type and shape); the other cards of the header are kept.
image.SetPixels<float>([1f, 2f, 3f, 4f, 5f, 6f], 3, 2);
Console.WriteLine($"{image.BitPix} {string.Join(" x ", image.Axes)} {image.Header.Get<string>("OBJECT")}");
```
<!-- /example -->

## Random groups

Random groups are an old structure, kept for radio interferometry data. A [`RandomGroupsHdu`](api/Eleris.FitsNet.RandomGroupsHdu.md) is the primary HDU only. It holds a number of groups; each has a few parameters and a small array. `ReadParameters(i)` and `ReadData(i)` return the physical values of the group `i`, as `double`.

<!-- example: Images.RandomGroups -->
```csharp
// Random groups (an old structure, still used for radio interferometry) store many small arrays,
// each with a few parameters. Here: 3 groups of 2 parameters and a 2 x 2 data array.
float[] values =
[
    0.1f, 0.2f,   1, 2, 3, 4,     // group 0: parameters, then data
    0.3f, 0.4f,   5, 6, 7, 8,
    0.5f, 0.6f,   9, 10, 11, 12,
];
var groups = RandomGroupsHdu.Create<float>(values, groupCount: 3, parameterCount: 2, dataAxes: [2, 2]);

double[] parameters = groups.ReadParameters(1);     // about 0.3 and 0.4 (floats read as doubles)
double[] data = groups.ReadData(1);                 // 5 6 7 8
Console.WriteLine($"{groups.GroupCount} groups; group 1: {parameters[0]}, {data[3]}");
```
<!-- /example -->

## Where to go next

- Add coordinates to an image: [World coordinates](wcs.md).
- Make it smaller: [Compression](compression.md).
