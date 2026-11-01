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
