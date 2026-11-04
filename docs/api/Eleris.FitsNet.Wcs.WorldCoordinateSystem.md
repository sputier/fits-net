# WorldCoordinateSystem Class

**Namespace:** `Eleris.FitsNet.Wcs`

**Inheritance:** `object` → `WorldCoordinateSystem`

The world coordinate system of an HDU (standard section 8): the link between pixel coordinates and world coordinates, built from the `CRPIXj`, `CRVALi`, `CDELTi`, `CTYPEi`, `CUNITi`, `PCi_j` / `CDi_j` / `CROTAi`, `PVi_m`, `LONPOLE`, `LATPOLE` keywords of one coordinate description (primary or alternate A to Z). Pixel coordinates follow the FITS convention: the centre of the first pixel is 1.0. Celestial coordinates are in degrees, the other axes in the unit of their `CUNITi`.

```csharp
public sealed class WorldCoordinateSystem
```

## Properties

| Member | Description |
| --- | --- |
| [`Alternate`](#eleris-fitsnet-wcs-worldcoordinatesystem-alternate) | The alternate description letter (a space for the primary one). |
| [`AxisCount`](#eleris-fitsnet-wcs-worldcoordinatesystem-axiscount) | Number of world coordinate axes (WCSAXESa). |
| [`AxisKinds`](#eleris-fitsnet-wcs-worldcoordinatesystem-axiskinds) | What each axis is: linear, celestial, spectral, time, or one of the other time-related axes of section 9.6. |
| [`CoordinateTypes`](#eleris-fitsnet-wcs-worldcoordinatesystem-coordinatetypes) | The CTYPEi values. |
| [`Distortion`](#eleris-fitsnet-wcs-worldcoordinatesystem-distortion) | The distortion convention that the transformations apply: `SIP` (`-SIP` after the projection code, with the keywords `A_ORDER`, `A_p_q`, `B_ORDER`, `B_p_q`, and optionally `AP_p_q`, `BP_p_q`), `TPV` (the projection code `TPV`, with the polynomials `PV1_0` to `PV1_39` and `PV2_0` to `PV2_39` of SCAMP), or an empty string when there is none. |
| [`Equinox`](#eleris-fitsnet-wcs-worldcoordinatesystem-equinox) | Equinox of the celestial coordinates in years (EQUINOXa), if given. |
| [`IsCelestial`](#eleris-fitsnet-wcs-worldcoordinatesystem-iscelestial) | Whether the description has a longitude and a latitude axis. |
| [`LatitudeAxis`](#eleris-fitsnet-wcs-worldcoordinatesystem-latitudeaxis) | Zero-based index of the latitude axis, or -1. |
| [`LatitudePole`](#eleris-fitsnet-wcs-worldcoordinatesystem-latitudepole) | LATPOLEa as given in the header (null: default). |
| [`LongitudeAxis`](#eleris-fitsnet-wcs-worldcoordinatesystem-longitudeaxis) | Zero-based index of the longitude axis, or -1. |
| [`LongitudePole`](#eleris-fitsnet-wcs-worldcoordinatesystem-longitudepole) | LONPOLEa as given in the header (null: default). |
| [`Name`](#eleris-fitsnet-wcs-worldcoordinatesystem-name) | WCSNAMEa, the name of the description. |
| [`ProjectionCode`](#eleris-fitsnet-wcs-worldcoordinatesystem-projectioncode) | The three-letter projection code of the celestial axes (for example `TAN`); empty when there are none. |
| [`RadeSys`](#eleris-fitsnet-wcs-worldcoordinatesystem-radesys) | Reference system of the celestial coordinates (RADESYSa), such as `ICRS` or `FK5`. |
| [`ReferencePixel`](#eleris-fitsnet-wcs-worldcoordinatesystem-referencepixel) | The reference pixel, CRPIXj. |
| [`ReferenceValue`](#eleris-fitsnet-wcs-worldcoordinatesystem-referencevalue) | The reference values, CRVALi, in the unit of CUNITi as written in the header. |
| [`TimeAxis`](#eleris-fitsnet-wcs-worldcoordinatesystem-timeaxis) | Zero-based index of the time axis, or -1. |
| [`UnappliedDistortion`](#eleris-fitsnet-wcs-worldcoordinatesystem-unapplieddistortion) | A distortion convention written after the projection code of the celestial CTYPEs that is not applied: the transformation is then the plain projection. |
| [`Units`](#eleris-fitsnet-wcs-worldcoordinatesystem-units) | The CUNITi values as written in the header (celestial axes are nevertheless converted to degrees). |

## Methods

| Member | Description |
| --- | --- |
| [`AlternatesIn(FitsHeader)`](#eleris-fitsnet-wcs-worldcoordinatesystem-alternatesin-eleris-fitsnet-fitsheader) | The alternate descriptions present in a header (the primary one is not listed). |
| [`FromHeader(FitsHeader, char)`](#eleris-fitsnet-wcs-worldcoordinatesystem-fromheader-eleris-fitsnet-fitsheader-system-char) | Reads the coordinate description `alternate` (a space, or a letter from A to Z) of a header. |
| [`PhasePeriod(int)`](#eleris-fitsnet-wcs-worldcoordinatesystem-phaseperiod-system-int32) | CPERIia: the constant period of a phase axis (zero-based axis index), if given. |
| [`PhaseZeroPoint(int)`](#eleris-fitsnet-wcs-worldcoordinatesystem-phasezeropoint-system-int32) | CZPHSia: the time at the zero point of a phase axis (zero-based axis index), if given. |
| [`PixelToWorld(ReadOnlySpan<double>)`](#eleris-fitsnet-wcs-worldcoordinatesystem-pixeltoworld-system-readonlyspan-system-double) | Converts FITS pixel coordinates (one per axis, the first pixel is centred on 1.0) to world coordinates. |
| [`PixelToWorld(ReadOnlySpan<double>, Span<double>)`](#eleris-fitsnet-wcs-worldcoordinatesystem-pixeltoworld-system-readonlyspan-system-double-system-span-system-double) | Converts many points at once without allocating: `pixels` holds the points one after the other ([`WorldCoordinateSystem.AxisCount`](#eleris-fitsnet-wcs-worldcoordinatesystem-axiscount) values each) and `world` receives the world coordinates in the same layout. |
| [`WorldToPixel(ReadOnlySpan<double>)`](#eleris-fitsnet-wcs-worldcoordinatesystem-worldtopixel-system-readonlyspan-system-double) | The inverse of [`WorldCoordinateSystem.PixelToWorld`](#eleris-fitsnet-wcs-worldcoordinatesystem-pixeltoworld-system-readonlyspan-system-double); NaN for the pixel coordinates that depend on a world position that cannot be reached. |
| [`WorldToPixel(ReadOnlySpan<double>, Span<double>)`](#eleris-fitsnet-wcs-worldcoordinatesystem-worldtopixel-system-readonlyspan-system-double-system-span-system-double) | Converts many positions at once without allocating (see [`WorldCoordinateSystem.PixelToWorld`](#eleris-fitsnet-wcs-worldcoordinatesystem-pixeltoworld-system-readonlyspan-system-double-system-span-system-double)). |

## Property details

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-alternate"></a>
### `Alternate`

The alternate description letter (a space for the primary one).

```csharp
public char Alternate { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-axiscount"></a>
### `AxisCount`

Number of world coordinate axes (WCSAXESa).

```csharp
public int AxisCount { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-axiskinds"></a>
### `AxisKinds`

What each axis is: linear, celestial, spectral, time, or one of the other time-related axes of section 9.6.

```csharp
public IReadOnlyList<WorldAxisKind> AxisKinds { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-coordinatetypes"></a>
### `CoordinateTypes`

The CTYPEi values.

```csharp
public IReadOnlyList<string> CoordinateTypes { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-distortion"></a>
### `Distortion`

The distortion convention that the transformations apply: `SIP` (`-SIP` after the projection code, with the keywords `A_ORDER`, `A_p_q`, `B_ORDER`, `B_p_q`, and optionally `AP_p_q`, `BP_p_q`), `TPV` (the projection code `TPV`, with the polynomials `PV1_0` to `PV1_39` and `PV2_0` to `PV2_39` of SCAMP), or an empty string when there is none. Neither is part of the standard.

```csharp
public string Distortion { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-equinox"></a>
### `Equinox`

Equinox of the celestial coordinates in years (EQUINOXa), if given.

```csharp
public double? Equinox { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-iscelestial"></a>
### `IsCelestial`

Whether the description has a longitude and a latitude axis.

```csharp
public bool IsCelestial { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-latitudeaxis"></a>
### `LatitudeAxis`

Zero-based index of the latitude axis, or -1.

```csharp
public int LatitudeAxis { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-latitudepole"></a>
### `LatitudePole`

LATPOLEa as given in the header (null: default).

```csharp
public double? LatitudePole { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-longitudeaxis"></a>
### `LongitudeAxis`

Zero-based index of the longitude axis, or -1.

```csharp
public int LongitudeAxis { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-longitudepole"></a>
### `LongitudePole`

LONPOLEa as given in the header (null: default).

```csharp
public double? LongitudePole { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-name"></a>
### `Name`

WCSNAMEa, the name of the description.

```csharp
public string? Name { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-projectioncode"></a>
### `ProjectionCode`

The three-letter projection code of the celestial axes (for example `TAN`); empty when there are none. A description with the `TPV` distortion convention gives `TPV` here, and is converted with the tangent projection (see [`WorldCoordinateSystem.Distortion`](#eleris-fitsnet-wcs-worldcoordinatesystem-distortion)).

```csharp
public string ProjectionCode { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-radesys"></a>
### `RadeSys`

Reference system of the celestial coordinates (RADESYSa), such as `ICRS` or `FK5`.

```csharp
public string? RadeSys { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-referencepixel"></a>
### `ReferencePixel`

The reference pixel, CRPIXj.

```csharp
public IReadOnlyList<double> ReferencePixel { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-referencevalue"></a>
### `ReferenceValue`

The reference values, CRVALi, in the unit of CUNITi as written in the header.

```csharp
public IReadOnlyList<double> ReferenceValue { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-timeaxis"></a>
### `TimeAxis`

Zero-based index of the time axis, or -1. An image has at most one.

```csharp
public int TimeAxis { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-unapplieddistortion"></a>
### `UnappliedDistortion`

A distortion convention written after the projection code of the celestial CTYPEs that is not applied: the transformation is then the plain projection. This is the case of `-SIP` without the SIP keywords, of a SIP description that is not on the primary description or not on the first two axes, and of any other convention (`-DSS`, `-WAT`...). Empty when nothing is ignored.

```csharp
public string UnappliedDistortion { get; }
```

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-units"></a>
### `Units`

The CUNITi values as written in the header (celestial axes are nevertheless converted to degrees).

```csharp
public IReadOnlyList<string> Units { get; }
```

## Method details

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-alternatesin-eleris-fitsnet-fitsheader"></a>
### `AlternatesIn(FitsHeader)`

The alternate descriptions present in a header (the primary one is not listed).

```csharp
public static IReadOnlyList<char> AlternatesIn(FitsHeader header)
```

**Parameters**

- `header` — The header.

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-fromheader-eleris-fitsnet-fitsheader-system-char"></a>
### `FromHeader(FitsHeader, char)`

Reads the coordinate description `alternate` (a space, or a letter from A to Z) of a header.

```csharp
public static WorldCoordinateSystem FromHeader(FitsHeader header, char alternate = ' ')
```

**Parameters**

- `header` — The header.
- `alternate` — The alternate description: a space for the primary one, or a letter from A to Z.

**Exceptions**

- [`FitsException`](Eleris.FitsNet.FitsException.md) — The keywords are inconsistent or the description is unusable.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The description uses a projection or algorithm that is not part of the standard.

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-phaseperiod-system-int32"></a>
### `PhasePeriod(int)`

CPERIia: the constant period of a phase axis (zero-based axis index), if given.

```csharp
public double? PhasePeriod(int axis)
```

**Parameters**

- `axis` — The zero-based index of the axis.

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-phasezeropoint-system-int32"></a>
### `PhaseZeroPoint(int)`

CZPHSia: the time at the zero point of a phase axis (zero-based axis index), if given.

```csharp
public double? PhaseZeroPoint(int axis)
```

**Parameters**

- `axis` — The zero-based index of the axis.

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-pixeltoworld-system-readonlyspan-system-double"></a>
### `PixelToWorld(ReadOnlySpan<double>)`

Converts FITS pixel coordinates (one per axis, the first pixel is centred on 1.0) to world coordinates. A point that has no image in a celestial projection, such as a point outside the sphere, gives NaN for the two celestial coordinates only.

```csharp
public double[] PixelToWorld(ReadOnlySpan<double> pixel)
```

**Parameters**

- `pixel` — The pixel coordinates, one for each axis; the center of the first pixel is 1.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — `pixel` does not have one value per axis.

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-pixeltoworld-system-readonlyspan-system-double-system-span-system-double"></a>
### `PixelToWorld(ReadOnlySpan<double>, Span<double>)`

Converts many points at once without allocating: `pixels` holds the points one after the other ([`WorldCoordinateSystem.AxisCount`](#eleris-fitsnet-wcs-worldcoordinatesystem-axiscount) values each) and `world` receives the world coordinates in the same layout.

```csharp
public void PixelToWorld(ReadOnlySpan<double> pixels, Span<double> world)
```

**Parameters**

- `pixels` — The pixel coordinates of the points one after the other, [`WorldCoordinateSystem.AxisCount`](#eleris-fitsnet-wcs-worldcoordinatesystem-axiscount) values for each point.
- `world` — Receives the world coordinates, in the same layout; it must be at least as long as `pixels`.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The lengths are not a whole number of points, or `world` is too short.

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-worldtopixel-system-readonlyspan-system-double"></a>
### `WorldToPixel(ReadOnlySpan<double>)`

The inverse of [`WorldCoordinateSystem.PixelToWorld`](#eleris-fitsnet-wcs-worldcoordinatesystem-pixeltoworld-system-readonlyspan-system-double); NaN for the pixel coordinates that depend on a world position that cannot be reached.

```csharp
public double[] WorldToPixel(ReadOnlySpan<double> world)
```

**Parameters**

- `world` — The world coordinates, one for each axis (celestial coordinates in degrees).

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — `world` does not have one value per axis.

<a id="eleris-fitsnet-wcs-worldcoordinatesystem-worldtopixel-system-readonlyspan-system-double-system-span-system-double"></a>
### `WorldToPixel(ReadOnlySpan<double>, Span<double>)`

Converts many positions at once without allocating (see [`WorldCoordinateSystem.PixelToWorld`](#eleris-fitsnet-wcs-worldcoordinatesystem-pixeltoworld-system-readonlyspan-system-double-system-span-system-double)).

```csharp
public void WorldToPixel(ReadOnlySpan<double> world, Span<double> pixels)
```

**Parameters**

- `world` — The world coordinates, one for each axis (celestial coordinates in degrees).
- `pixels` — Receives the pixel coordinates, in the same layout; it must be at least as long as `world`.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The lengths are not a whole number of points, or `pixels` is too short.
