# Time

Section 9 of the standard describes how a FITS file records time: the time scale, the unit, the reference time, and the keywords that say when an observation took place. Eleris.FitsNet has two types for this:

- [`FitsTime`](api/Eleris.FitsNet.Wcs.FitsTime.md), an instant on a given time scale;
- [`TimeFrame`](api/Eleris.FitsNet.Wcs.TimeFrame.md), the description of how the time values of an HDU are to be read.

## Instants

A [`FitsTime`](api/Eleris.FitsNet.Wcs.FitsTime.md) is a day number and the seconds into the day, so nanoseconds are kept over any span of years. It knows its [`TimeScale`](api/Eleris.FitsNet.Wcs.TimeScale.md). You make one from a FITS datetime string (`Parse`, `TryParse`), from a calendar date (`FromDateTime`), or from a Modified Julian Date or a Julian Date (`FromMjd`, `FromJd`, and the `...Parts` variants that keep the precision when the date is given in two parts).

<!-- example: Time.Instants -->
```csharp
// Parse reads a FITS datetime; the time scale is part of the instant.
FitsTime utc = FitsTime.Parse("2016-12-31T23:59:60.5", TimeScale.UTC);   // a leap second
Console.WriteLine(utc.ToIsoString(3));                                    // 2016-12-31T23:59:60.500
Console.WriteLine($"MJD {utc.Mjd:F6}, JD {utc.Jd:F6}");

// Conversions between the scales that can be computed without outside data.
FitsTime tai = utc.To(TimeScale.TAI);
FitsTime tt = utc.To(TimeScale.TT);
Console.WriteLine($"TAI {tai.ToIsoString(1)}  TT {tt.ToIsoString(1)}");

// Arithmetic is in SI seconds, leap seconds included.
FitsTime later = utc.AddSeconds(1.0);
Console.WriteLine($"{later.ToIsoString(1)}, {later.SecondsSince(utc)} s later");

// Build an instant from a date, a Modified Julian Date or a Julian Date.
FitsTime j2000 = FitsTime.FromDateTime(2000, 1, 1, 12, 0, 0, TimeScale.TT);
Console.WriteLine(j2000.Jd == FitsTime.FromJd(2451545.0, TimeScale.TT).Jd);           // True
```
<!-- /example -->

Useful members:

| Member | Result |
| --- | --- |
| `ToIsoString(decimals)` | The FITS datetime string, with that many decimals of the second |
| `Mjd`, `Jd` | The Modified Julian Date and the Julian Date |
| `ToDateTime()` | Year, month, day, hour, minute and second |
| `AddSeconds(s)` | The instant `s` SI seconds later; leap seconds count |
| `SecondsSince(other)` | The difference in SI seconds, on the same scale |
| `To(scale)` | The same instant on another scale |

`FitsTime` is a value type: it can be compared (`==`, `CompareTo`) and used as a dictionary key. Two instants are equal only when they are on the same scale; convert one before comparing.

### Time scales

| `TimeScale` | Name | Notes |
| --- | --- | --- |
| `UTC` | Coordinated Universal Time | Leap seconds are known from 1972. `23:59:60` is a valid second on the days that have one. |
| `TAI` | International Atomic Time | |
| `TT` | Terrestrial Time | Also written `TDT` or `ET` in headers. |
| `GPS` | GPS time | TAI - 19 s. |
| `TCG` | Geocentric Coordinate Time | |
| `TCB`, `TDB` | Barycentric Coordinate Time, Barycentric Dynamical Time | Convert between them, not to the others. |
| `UT1` | Universal Time | Needs Earth orientation data: no conversion. |
| `Local` | A local clock | No conversion. |

`TimeScales.Parse` reads the names that appear in `TIMESYS`, including the synonyms (`IAT`, `TDT`, `ET`, `GMT`) and a realization in parentheses such as `TT(TAI)`. `TimeScales.CanConvert(from, to)` tells whether `To` can work.

`To` converts between `TAI`, `TT`, `UTC`, `GPS` and `TCG`, and between `TCB` and `TDB`. Everything else throws a `NotSupportedException`: the conversions that need data the library does not carry (`UT1`, which needs Earth orientation; `TT` to `TDB`, which needs a time ephemeris), UTC before 1972, and `Local`.

<!-- example: Time.Invalid -->
```csharp
// TryParse does not throw. Time zones are not part of FITS datetimes, so a trailing Z is refused.
Console.WriteLine(FitsTime.TryParse("2020-01-01T00:00:00", TimeScale.UTC, out _));    // True
Console.WriteLine(FitsTime.TryParse("2020-01-01T00:00:00Z", TimeScale.UTC, out _));   // False

// Conversions that need data the library does not carry (Earth orientation, a time ephemeris) are refused.
try
{
    FitsTime.Parse("2020-01-01", TimeScale.UTC).To(TimeScale.UT1);
}
catch (NotSupportedException e)
{
    Console.WriteLine(e.Message);
}
```
<!-- /example -->

Dates are limited to the years -99999 to +99999, which is what a FITS datetime can write. A date outside it throws an `ArgumentOutOfRangeException`. `Parse` throws a `FormatException` on a bad string, and `TryParse` returns `false`. Time zones are not part of a FITS datetime: a trailing `Z` is refused.

## Time frames

A [`TimeFrame`](api/Eleris.FitsNet.Wcs.TimeFrame.md) gathers the time keywords of a header: `TIMESYS`, `TIMEUNIT`, the reference time (`MJDREF`, `JDREF` or `DATEREF`), `TIMEOFFS`, `TREFPOS`, the observation dates (`DATE-OBS`, `DATE-BEG`, `DATE-AVG`, `DATE-END` and their `MJD-` forms), `TSTART`, `TSTOP`, `XPOSURE`, `TELAPSE`, the errors and the resolution. Each is a property: `Scale`, `Unit`, `SecondsPerUnit`, `ReferenceTime`, `ReferencePosition`, `Observation`, `Begin`, `Average`, `End`, `Start`, `Stop`, `Exposure`, `ElapsedTime`, and so on. The properties of an absent keyword are `null`, or the default of the standard where there is one (`TOPOCENTER`, `DE405`, seconds).

`ToTime(value)` turns a time value of the data into an instant, and `ToValue(instant)` does the reverse.

<!-- example: Time.Frame -->
```csharp
// The keywords of section 9 of the standard describe how the time values of a file are to be read.
var header = new FitsHeader()
    .Set("TIMESYS", "TT")
    .Set("MJDREF", 55197.0)          // the reference time, as a Modified Julian Date
    .Set("TIMEUNIT", "d")
    .Set("TSTART", 100.5)
    .Set("TSTOP", 101.5)
    .Set("TREFPOS", "GEOCENTER");

TimeFrame frame = TimeFrame.FromHeader(header);
Console.WriteLine($"{frame.Scale}, {frame.Unit}, {frame.ReferencePosition}");
Console.WriteLine($"{frame.Start!.Value.ToIsoString(0)} to {frame.Stop!.Value.ToIsoString(0)}");

// Turn the time values of the data into instants, and back.
FitsTime instant = frame.ToTime(100.75);
double value = frame.ToValue(instant);                    // 100.75
Console.WriteLine($"{instant.ToIsoString(0)} {value}");
```
<!-- /example -->

`ToTime` throws an `InvalidOperationException` when the scale is local or unknown, since no reference time applies.

### Table columns and image axes

A time column of a binary table can override the global keywords with `TCTYPn`, `TCUNIn`, `TRPOSn` and `TRDIRn`. A time axis of an image is a coordinate whose `CTYPEi` is `TIME` or the name of a scale (see [World coordinates](wcs.md)). `TimeFrame.ForColumn` and `TimeFrame.ForAxis` build the frame that applies; `column` and `axis` are **one-based**, the `n` and `i` of the keywords.

<!-- example: Time.Columns -->
```csharp
// A time axis of an image, or a time column of a table, can override the global keywords.
var header = new FitsHeader()
    .Set("TIMESYS", "UTC").Set("MJDREF", 58000.0)
    .Set("TTYPE2", "TIME").Set("TCUNI2", "s");             // column 2 holds seconds since the reference time

TimeFrame column = TimeFrame.ForColumn(header, 2);
Console.WriteLine(column.ToTime(86400.0).ToIsoString(0));   // 2017-09-05T00:00:00

// A time axis of an image: CTYPEi names the scale (or says TIME), CUNITi the unit.
var image = new FitsHeader().Set("CTYPE3", "TIME").Set("CUNIT3", "min").Set("MJDREF", 58000.0);
TimeFrame axis = TimeFrame.ForAxis(image, 3);
Console.WriteLine(axis.SecondsPerUnit);                     // 60
```
<!-- /example -->

### Epochs

`TimeFrame.FromJulianEpoch(2000.0)` and `FromBesselianEpoch(1950.0)` give the instants of the epochs of the older header keywords (`JEPOCH`, `BEPOCH`), on the scales that the standard associates with them.
