# World coordinates

A **world coordinate system** (WCS) links the pixels of an image, or the axes of any array, to physical quantities: a position on the sky, a wavelength, a time. The keywords are described in section 8 of the standard. [`WorldCoordinateSystem`](api/FitsNet.Wcs.WorldCoordinateSystem.md) reads them and does the conversion in both directions.

## Pixel to sky and back

`GetWorldCoordinateSystem()` reads the description from the header of any HDU. `WorldCoordinateSystem.FromHeader` does the same from a [`FitsHeader`](api/FitsNet.FitsHeader.md) alone.

<!-- example: Wcs.Celestial -->
```csharp
using var file = FitsFile.Open("m42.fits");

// Read the description from the header of any HDU (or from a FitsHeader with WorldCoordinateSystem.FromHeader).
WorldCoordinateSystem wcs = file.Primary.GetWorldCoordinateSystem();
Console.WriteLine($"{wcs.ProjectionCode} projection, {wcs.AxisCount} axes, celestial: {wcs.IsCelestial}");

// Pixels (the center of the first pixel is 1.0) to the sky. Celestial coordinates are in degrees.
double[] sky = wcs.PixelToWorld([320.5, 240.5]);
Console.WriteLine($"RA {sky[wcs.LongitudeAxis]:F5}, Dec {sky[wcs.LatitudeAxis]:F5}");

// And back.
double[] pixel = wcs.WorldToPixel([83.83, -5.38]);
Console.WriteLine($"({pixel[0]:F2}, {pixel[1]:F2})");
```
<!-- /example -->

Rules of the conversion:

- **Pixels** follow the FITS convention: the center of the first pixel is `1.0`. This is not the zero-based index of an array; the index `i` of an array is the pixel `i + 1.0`.
- **Celestial coordinates** are in degrees, whatever the `CUNITi` is. The other axes are in the unit of their `CUNITi`, as written in the header.
- `PixelToWorld` takes and returns one value per axis, in the order of the axes. `LongitudeAxis` and `LatitudeAxis` give the zero-based index of the celestial axes, or -1 when there are none.
- The description can use `PCi_j`, `CDi_j` or the old `CROTAi` for the rotation, and `PVi_m`, `LONPOLE` and `LATPOLE` for the projection.

### Projections

The 27 celestial projections of the standard are supported. The first four characters of `CTYPEi` name the pair of axes: `RA--` with `DEC-`, `xLON` with `xLAT` (`GLON`/`GLAT`, `ELON`/`ELAT`...) or `yzLN` with `yzLT`, and the projection code is in characters 6 to 8 (`RA---TAN`). The celestial unit (`CUNITi`) can be `deg`, `arcmin`, `arcsec`, `mas` or `rad`; both axes must use the same one.

| Family | Codes |
| --- | --- |
| Zenithal | `AZP`, `SZP`, `TAN`, `STG`, `SIN`, `ARC`, `ZPN`, `ZEA`, `AIR` |
| Cylindrical | `CYP`, `CEA`, `CAR`, `MER` |
| Pseudocylindrical, conic, polyconic | `SFL`, `PAR`, `MOL`, `AIT`, `COP`, `COE`, `COD`, `COO`, `BON`, `PCO` |
| Quad cube | `TSC`, `CSC`, `QSC` |
| HEALPix | `HPX` |

`ProjectionCode` tells which one a description uses. A code that is not one of these throws a `NotSupportedException`. Distortion conventions appended to the code (`-SIP`, `-TPV`) are not part of the standard and are **not applied**: the plain projection is used, and `UnappliedDistortion` tells you that the description asked for one.

### Points that have no image

Some positions cannot be converted: a pixel outside the edge of the sphere in a `SIN` projection, a point on the far side of the sky in a `TAN` projection. They give `NaN` for the two celestial values, and the other axes are converted normally. No exception is thrown.

<!-- example: Wcs.Outside -->
```csharp
var header = new FitsHeader()
    .Set("CTYPE1", "RA---SIN").Set("CTYPE2", "DEC--SIN")
    .Set("CRPIX1", 100.0).Set("CRPIX2", 100.0)
    .Set("CRVAL1", 150.0).Set("CRVAL2", 30.0)
    .Set("CDELT1", -1.0).Set("CDELT2", 1.0);
var wcs = WorldCoordinateSystem.FromHeader(header);

// A point that has no image in the projection (here: beyond the edge of the sphere) gives NaN, not an exception.
double[] far = wcs.PixelToWorld([1000.0, 1000.0]);
Console.WriteLine(double.IsNaN(far[0]));                       // True

// The same for the opposite direction: the far side of the sky is not on the plane.
double[] hidden = wcs.WorldToPixel([330.0, -30.0]);
Console.WriteLine(double.IsNaN(hidden[0]));                    // True
```
<!-- /example -->

## Many points

The overloads with two spans convert many points without allocating anything. The points follow one another: `AxisCount` values for each. Both spans must have the same layout, and the output must be at least as long as the input.

<!-- example: Wcs.Batch -->
```csharp
using var file = FitsFile.Open("m42.fits");
var wcs = file.Primary.GetWorldCoordinateSystem();

// Many points at once, without allocating: x and y of each point one after the other.
const int width = 640;
var pixels = new double[width * 2];
for (int x = 0; x < width; x++)
{
    pixels[2 * x] = x + 1;        // FITS pixel numbers start at 1
    pixels[2 * x + 1] = 240.5;
}
var world = new double[pixels.Length];
wcs.PixelToWorld(pixels, world);
Console.WriteLine($"The middle row goes from RA {world[0]:F3} to RA {world[2 * (width - 1)]:F3}");
```
<!-- /example -->

## Spectral axes

Axes with a spectral type (`FREQ`, `ENER`, `WAVN`, `VRAD`, `WAVE`, `VOPT`, `ZOPT`, `AWAV`, `VELO`, `BETA`) are converted too. The axis can be sampled linearly, logarithmically (`-LOG`), or linearly in another spectral variable (the `-F2W`, `-W2F`, `-F2V`... algorithms of Table 26 of the standard). The types that depend on a rest frequency (`VRAD`, `VOPT`, `ZOPT`, `VELO`, `BETA`) with a nonlinear algorithm need `RESTFRQ` or `RESTWAV`, otherwise a `FitsException` tells you so. The grism algorithms and the lookup table (`-GRI`, `-GRA`, `-TAB`) are not supported and throw a `NotSupportedException`; see [Standard coverage](standard-coverage.md#world-coordinates). `AxisKinds` says what each axis is.

| [`WorldAxisKind`](api/FitsNet.Wcs.WorldAxisKind.md) | Axis |
| --- | --- |
| `Linear` | Anything that is not one of the following |
| `Celestial` | Longitude or latitude |
| `Spectral` | Frequency, wavelength, velocity... |
| `Time` | A time axis, see [Time](time.md) |
| `Stokes`, `Complex`, `Phase`, `TimeLag`, `TemporalFrequency` | The other axes of the standard |

<!-- example: Wcs.Spectral -->
```csharp
// A cube with two sky axes and a frequency axis sampled linearly.
var header = new FitsHeader()
    .Set("CTYPE1", "RA---TAN").Set("CTYPE2", "DEC--TAN").Set("CTYPE3", "FREQ")
    .Set("CRPIX1", 1.0).Set("CRPIX2", 1.0).Set("CRPIX3", 1.0)
    .Set("CRVAL1", 10.0).Set("CRVAL2", 20.0).Set("CRVAL3", 1.4204e9)
    .Set("CDELT1", -0.01).Set("CDELT2", 0.01).Set("CDELT3", 1.0e5)
    .Set("CUNIT3", "Hz");
var wcs = WorldCoordinateSystem.FromHeader(header);

double[] world = wcs.PixelToWorld([1.0, 1.0, 11.0]);
Console.WriteLine($"{world[2]} Hz");                           // 1421400000 Hz
Console.WriteLine(string.Join(", ", wcs.AxisKinds));           // Celestial, Celestial, Spectral

// A nonlinear spectral axis: sampled linearly in frequency, expressed as a wavelength (the -F2W algorithm).
header.Set("CTYPE3", "WAVE-F2W").Set("CRVAL3", 0.2110611).Set("CDELT3", -1.0e-6).Set("CUNIT3", "m");
var wavelengths = WorldCoordinateSystem.FromHeader(header);
Console.WriteLine(wavelengths.PixelToWorld([1.0, 1.0, 11.0])[2]);
```
<!-- /example -->

## Alternate descriptions

A header can contain several descriptions of the same axes, named with a letter from `A` to `Z` appended to the keywords (`CTYPE1A`, `CRVAL2B`...). `WorldCoordinateSystem.AlternatesIn` lists the letters found, without the primary description. `FromHeader` and `GetWorldCoordinateSystem` take the letter as an optional last argument; a space (the default) is the primary description. `Name` is the `WCSNAMEa` of the description.

<!-- example: Wcs.Alternates -->
```csharp
// A header can hold several descriptions of the same axes: the primary one, and alternates named A to Z.
var header = new FitsHeader()
    .Set("CTYPE1", "RA---TAN").Set("CTYPE2", "DEC--TAN")
    .Set("CRPIX1", 50.0).Set("CRPIX2", 50.0).Set("CRVAL1", 10.0).Set("CRVAL2", 10.0)
    .Set("CDELT1", -0.1).Set("CDELT2", 0.1)
    .Set("WCSNAMEA", "galactic")
    .Set("CTYPE1A", "GLON-CAR").Set("CTYPE2A", "GLAT-CAR")
    .Set("CRPIX1A", 50.0).Set("CRPIX2A", 50.0).Set("CRVAL1A", 100.0).Set("CRVAL2A", 5.0)
    .Set("CDELT1A", -0.1).Set("CDELT2A", 0.1);

foreach (char letter in WorldCoordinateSystem.AlternatesIn(header))
{
    var wcs = WorldCoordinateSystem.FromHeader(header, letter);
    Console.WriteLine($"{letter}: {wcs.Name}, {wcs.ProjectionCode}");     // A: galactic, CAR
}
```
<!-- /example -->

## Properties of a description

| Property | Keyword | Meaning |
| --- | --- | --- |
| `AxisCount` | `WCSAXES` | Number of axes |
| `CoordinateTypes`, `Units` | `CTYPEi`, `CUNITi` | As written |
| `ReferencePixel`, `ReferenceValue` | `CRPIXj`, `CRVALi` | As written |
| `RadeSys`, `Equinox` | `RADESYS`, `EQUINOX` | The reference frame; when `RADESYS` is missing, the default of the standard is used (`FK4`, `FK5` or `ICRS` depending on the equinox) |
| `LongitudePole`, `LatitudePole` | `LONPOLE`, `LATPOLE` | `null` when not given |
| `TimeAxis` | | Index of the time axis, or -1 |

## Errors

| Case | Exception |
| --- | --- |
| Inconsistent keywords: one celestial axis without the other, two longitudes, different projections on the two axes, a pole outside its range | `FitsException` |
| A projection or spectral algorithm that is not in the standard | `NotSupportedException` |
| An array of the wrong length passed to a conversion | `ArgumentException` |

<!-- example: Wcs.Errors -->
```csharp
var header = new FitsHeader().Set("CTYPE1", "RA---ZZZ").Set("CTYPE2", "DEC--ZZZ");
try
{
    WorldCoordinateSystem.FromHeader(header);
}
catch (NotSupportedException e)
{
    Console.WriteLine(e.Message);       // not one of the 27 projections of the FITS standard
}

try
{
    WorldCoordinateSystem.FromHeader(new FitsHeader().Set("CTYPE1", "RA---TAN"));
}
catch (FitsException e)
{
    Console.WriteLine(e.Message);       // a celestial description needs a longitude and a latitude axis
}
```
<!-- /example -->
