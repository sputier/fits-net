# TimeFrame Class

**Namespace:** `Eleris.FitsNet.Wcs`

**Inheritance:** `object` → `TimeFrame`

The time coordinate frame of an HDU (standard section 9): time scale, unit, reference time and position, errors, the global time keywords, and the means to turn the time values of the data (an image axis, a table column) into [`FitsTime`](Eleris.FitsNet.Wcs.FitsTime.md) instants.

```csharp
public sealed class TimeFrame
```

## Properties

| Member | Description |
| --- | --- |
| [`Average`](#eleris-fitsnet-wcs-timeframe-average) | DATE-AVG or MJD-AVG. |
| [`Begin`](#eleris-fitsnet-wcs-timeframe-begin) | DATE-BEG or MJD-BEG. |
| [`BesselianEpoch`](#eleris-fitsnet-wcs-timeframe-besselianepoch) | BEPOCH as an instant on the TT scale (ET). |
| [`ElapsedTime`](#eleris-fitsnet-wcs-timeframe-elapsedtime) | TELAPSE in seconds. |
| [`End`](#eleris-fitsnet-wcs-timeframe-end) | DATE-END or MJD-END. |
| [`Ephemeris`](#eleris-fitsnet-wcs-timeframe-ephemeris) | PLEPHEM: the Solar System ephemeris, `DE405` by default. |
| [`Exposure`](#eleris-fitsnet-wcs-timeframe-exposure) | XPOSURE in seconds. |
| [`HasExplicitReference`](#eleris-fitsnet-wcs-timeframe-hasexplicitreference) | Whether the header gives a reference time (otherwise MJD 0 is assumed). |
| [`JulianEpoch`](#eleris-fitsnet-wcs-timeframe-julianepoch) | JEPOCH as an instant on the TDB scale. |
| [`Observation`](#eleris-fitsnet-wcs-timeframe-observation) | DATE-OBS or MJD-OBS. |
| [`RandomError`](#eleris-fitsnet-wcs-timeframe-randomerror) | TIMRDER (or CRDERia): relative time error. |
| [`ReferenceDirection`](#eleris-fitsnet-wcs-timeframe-referencedirection) | TREFDIR (or TRDIRn): the keywords holding the reference direction, if any. |
| [`ReferencePosition`](#eleris-fitsnet-wcs-timeframe-referenceposition) | TREFPOS (or TRPOSn): where the times are valid, `TOPOCENTER` by default. |
| [`ReferenceTime`](#eleris-fitsnet-wcs-timeframe-referencetime) | The reference time all times are relative to (MJDREF, JDREF or DATEREF; MJD 0 when none is given); null for a local scale. |
| [`Scale`](#eleris-fitsnet-wcs-timeframe-scale) | The recognised time scale, or null for a local or unknown scale. |
| [`SecondsPerUnit`](#eleris-fitsnet-wcs-timeframe-secondsperunit) | Length of the time unit in SI seconds (evaluated at the reference time for the tropical and Besselian years). |
| [`Start`](#eleris-fitsnet-wcs-timeframe-start) | TSTART as an instant. |
| [`Stop`](#eleris-fitsnet-wcs-timeframe-stop) | TSTOP as an instant. |
| [`SystematicError`](#eleris-fitsnet-wcs-timeframe-systematicerror) | TIMSYER (or CSYERia): absolute time error. |
| [`TimeOffset`](#eleris-fitsnet-wcs-timeframe-timeoffset) | TIMEOFFS: the offset added to the reference time, in units of TIMEUNIT. It is for tables: images ignore it, as the standard requires. |
| [`TimePixelReference`](#eleris-fitsnet-wcs-timeframe-timepixelreference) | TIMEPIXR: where in the pixel the time stamp is, from 0 to 1 (0.5 by default). |
| [`TimeResolution`](#eleris-fitsnet-wcs-timeframe-timeresolution) | TIMEDEL: the time resolution in units of [`TimeFrame.Unit`](#eleris-fitsnet-wcs-timeframe-unit). |
| [`TimeSystem`](#eleris-fitsnet-wcs-timeframe-timesystem) | The value of TIMESYS (or of the axis override), for example `UTC` or `TT(TAI)`. |
| [`Unit`](#eleris-fitsnet-wcs-timeframe-unit) | The time unit (TIMEUNIT, CUNITia or TCUNIn), `s` by default. |

## Methods

| Member | Description |
| --- | --- |
| [`ForAxis(FitsHeader, int, char)`](#eleris-fitsnet-wcs-timeframe-foraxis-eleris-fitsnet-fitsheader-system-int32-system-char) | The frame of a time axis of an image or random groups: CTYPEia may name the time scale, CUNITia overrides the unit, CSYERia and CRDERia the errors; the global keywords supply the rest. |
| [`ForColumn(FitsHeader, int)`](#eleris-fitsnet-wcs-timeframe-forcolumn-eleris-fitsnet-fitsheader-system-int32) | The frame of a binary table column: TCTYPn, TCUNIn, TRPOSn and TRDIRn override the global keywords. |
| [`FromBesselianEpoch(double)`](#eleris-fitsnet-wcs-timeframe-frombesselianepoch-system-double) | Instant of a Besselian epoch such as 1950.0 (ET, that is TT). |
| [`FromHeader(FitsHeader)`](#eleris-fitsnet-wcs-timeframe-fromheader-eleris-fitsnet-fitsheader) | The global time frame of a header (TIMESYS, TIMEUNIT, TREFPOS, ...). |
| [`FromJulianEpoch(double)`](#eleris-fitsnet-wcs-timeframe-fromjulianepoch-system-double) | Instant of a Julian epoch such as 2000.0 (TDB). |
| [`IsTimeCoordinateType(string)`](#eleris-fitsnet-wcs-timeframe-istimecoordinatetype-system-string) | Whether a CTYPE names a time axis: `TIME` or a time scale of Table 30 of the standard. |
| [`ToTime(double)`](#eleris-fitsnet-wcs-timeframe-totime-system-double) | The instant of a time value of the data: the reference time, plus TIMEOFFS (in the global TIMEUNIT), plus `value` in the unit of this frame (the unit of the axis or column when it has one). |
| [`ToValue(FitsTime)`](#eleris-fitsnet-wcs-timeframe-tovalue-eleris-fitsnet-wcs-fitstime) | The inverse of [`TimeFrame.ToTime`](#eleris-fitsnet-wcs-timeframe-totime-system-double): the time value of the data for an instant on the same scale. |

## Property details

<a id="eleris-fitsnet-wcs-timeframe-average"></a>
### `Average`

DATE-AVG or MJD-AVG.

```csharp
public FitsTime? Average { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-begin"></a>
### `Begin`

DATE-BEG or MJD-BEG.

```csharp
public FitsTime? Begin { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-besselianepoch"></a>
### `BesselianEpoch`

BEPOCH as an instant on the TT scale (ET).

```csharp
public FitsTime? BesselianEpoch { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-elapsedtime"></a>
### `ElapsedTime`

TELAPSE in seconds.

```csharp
public double? ElapsedTime { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-end"></a>
### `End`

DATE-END or MJD-END.

```csharp
public FitsTime? End { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-ephemeris"></a>
### `Ephemeris`

PLEPHEM: the Solar System ephemeris, `DE405` by default.

```csharp
public string Ephemeris { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-exposure"></a>
### `Exposure`

XPOSURE in seconds.

```csharp
public double? Exposure { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-hasexplicitreference"></a>
### `HasExplicitReference`

Whether the header gives a reference time (otherwise MJD 0 is assumed).

```csharp
public bool HasExplicitReference { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-julianepoch"></a>
### `JulianEpoch`

JEPOCH as an instant on the TDB scale.

```csharp
public FitsTime? JulianEpoch { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-observation"></a>
### `Observation`

DATE-OBS or MJD-OBS.

```csharp
public FitsTime? Observation { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-randomerror"></a>
### `RandomError`

TIMRDER (or CRDERia): relative time error.

```csharp
public double RandomError { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-referencedirection"></a>
### `ReferenceDirection`

TREFDIR (or TRDIRn): the keywords holding the reference direction, if any.

```csharp
public string? ReferenceDirection { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-referenceposition"></a>
### `ReferencePosition`

TREFPOS (or TRPOSn): where the times are valid, `TOPOCENTER` by default.

```csharp
public string ReferencePosition { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-referencetime"></a>
### `ReferenceTime`

The reference time all times are relative to (MJDREF, JDREF or DATEREF; MJD 0 when none is given); null for a local scale.

```csharp
public FitsTime? ReferenceTime { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-scale"></a>
### `Scale`

The recognised time scale, or null for a local or unknown scale.

```csharp
public TimeScale? Scale { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-secondsperunit"></a>
### `SecondsPerUnit`

Length of the time unit in SI seconds (evaluated at the reference time for the tropical and Besselian years).

```csharp
public double SecondsPerUnit { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-start"></a>
### `Start`

TSTART as an instant.

```csharp
public FitsTime? Start { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-stop"></a>
### `Stop`

TSTOP as an instant.

```csharp
public FitsTime? Stop { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-systematicerror"></a>
### `SystematicError`

TIMSYER (or CSYERia): absolute time error.

```csharp
public double SystematicError { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-timeoffset"></a>
### `TimeOffset`

TIMEOFFS: the offset added to the reference time, in units of TIMEUNIT. It is for tables: images ignore it, as the standard requires.

```csharp
public double TimeOffset { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-timepixelreference"></a>
### `TimePixelReference`

TIMEPIXR: where in the pixel the time stamp is, from 0 to 1 (0.5 by default).

```csharp
public double TimePixelReference { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-timeresolution"></a>
### `TimeResolution`

TIMEDEL: the time resolution in units of [`TimeFrame.Unit`](#eleris-fitsnet-wcs-timeframe-unit).

```csharp
public double? TimeResolution { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-timesystem"></a>
### `TimeSystem`

The value of TIMESYS (or of the axis override), for example `UTC` or `TT(TAI)`.

```csharp
public string TimeSystem { get; }
```

<a id="eleris-fitsnet-wcs-timeframe-unit"></a>
### `Unit`

The time unit (TIMEUNIT, CUNITia or TCUNIn), `s` by default.

```csharp
public string Unit { get; }
```

## Method details

<a id="eleris-fitsnet-wcs-timeframe-foraxis-eleris-fitsnet-fitsheader-system-int32-system-char"></a>
### `ForAxis(FitsHeader, int, char)`

The frame of a time axis of an image or random groups: CTYPEia may name the time scale, CUNITia overrides the unit, CSYERia and CRDERia the errors; the global keywords supply the rest.

```csharp
public static TimeFrame ForAxis(FitsHeader header, int axis, char alternate = ' ')
```

**Parameters**

- `header` — The header of the image.
- `axis` — The one-based number of the time axis (the `i` of `CTYPEi`).
- `alternate` — The alternate description, a space for the primary one.

<a id="eleris-fitsnet-wcs-timeframe-forcolumn-eleris-fitsnet-fitsheader-system-int32"></a>
### `ForColumn(FitsHeader, int)`

The frame of a binary table column: TCTYPn, TCUNIn, TRPOSn and TRDIRn override the global keywords.

```csharp
public static TimeFrame ForColumn(FitsHeader header, int column)
```

**Parameters**

- `header` — The header of the binary table.
- `column` — The one-based number of the column (the `n` of `TTYPEn`).

<a id="eleris-fitsnet-wcs-timeframe-frombesselianepoch-system-double"></a>
### `FromBesselianEpoch(double)`

Instant of a Besselian epoch such as 1950.0 (ET, that is TT).

```csharp
public static FitsTime FromBesselianEpoch(double epoch)
```

**Parameters**

- `epoch` — The epoch, for example 1950.0.

<a id="eleris-fitsnet-wcs-timeframe-fromheader-eleris-fitsnet-fitsheader"></a>
### `FromHeader(FitsHeader)`

The global time frame of a header (TIMESYS, TIMEUNIT, TREFPOS, ...).

```csharp
public static TimeFrame FromHeader(FitsHeader header)
```

**Parameters**

- `header` — The header.

<a id="eleris-fitsnet-wcs-timeframe-fromjulianepoch-system-double"></a>
### `FromJulianEpoch(double)`

Instant of a Julian epoch such as 2000.0 (TDB).

```csharp
public static FitsTime FromJulianEpoch(double epoch)
```

**Parameters**

- `epoch` — The epoch, for example 2000.0.

<a id="eleris-fitsnet-wcs-timeframe-istimecoordinatetype-system-string"></a>
### `IsTimeCoordinateType(string)`

Whether a CTYPE names a time axis: `TIME` or a time scale of Table 30 of the standard.

```csharp
public static bool IsTimeCoordinateType(string? ctype)
```

**Parameters**

- `ctype` — The value of a `CTYPEi` keyword.

<a id="eleris-fitsnet-wcs-timeframe-totime-system-double"></a>
### `ToTime(double)`

The instant of a time value of the data: the reference time, plus TIMEOFFS (in the global TIMEUNIT), plus `value` in the unit of this frame (the unit of the axis or column when it has one).

```csharp
public FitsTime ToTime(double value)
```

**Parameters**

- `value` — A time value of the data, in the unit of this frame.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The scale is local or unknown, so no reference time applies.

<a id="eleris-fitsnet-wcs-timeframe-tovalue-eleris-fitsnet-wcs-fitstime"></a>
### `ToValue(FitsTime)`

The inverse of [`TimeFrame.ToTime`](#eleris-fitsnet-wcs-timeframe-totime-system-double): the time value of the data for an instant on the same scale.

```csharp
public double ToValue(FitsTime time)
```

**Parameters**

- `time` — An instant on the time scale of this frame.
