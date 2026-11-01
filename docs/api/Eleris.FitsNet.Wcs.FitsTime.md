# FitsTime Struct

**Namespace:** `Eleris.FitsNet.Wcs`

**Inheritance:** `object` → `ValueType` → `FitsTime`

An instant on a time scale (standard section 9): a day number and the seconds into the day, which keeps nanoseconds over any span of years. ISO-8601 datetime strings, Julian and Modified Julian Dates and conversions between the linear scales are supported.

```csharp
public readonly struct FitsTime : IComparable<FitsTime>, IEquatable<FitsTime>
```

## Properties

| Member | Description |
| --- | --- |
| [`Day`](#eleris-fitsnet-wcs-fitstime-day) | Modified Julian Day number (the instant is during this day of its scale). |
| [`Jd`](#eleris-fitsnet-wcs-fitstime-jd) | The instant as a Julian Date. |
| [`Mjd`](#eleris-fitsnet-wcs-fitstime-mjd) | The instant as a Modified Julian Date (JD - 2400000.5). |
| [`Scale`](#eleris-fitsnet-wcs-fitstime-scale) | The time scale the instant is expressed in. |
| [`SecondOfDay`](#eleris-fitsnet-wcs-fitstime-secondofday) | Seconds elapsed since the start of [`FitsTime.Day`](#eleris-fitsnet-wcs-fitstime-day) (up to 86400 plus the leap second in UTC). |

## Methods

| Member | Description |
| --- | --- |
| [`AddSeconds(double)`](#eleris-fitsnet-wcs-fitstime-addseconds-system-double) | The instant `seconds` SI seconds later (earlier if negative), on the same scale. |
| [`CompareTo(FitsTime)`](#eleris-fitsnet-wcs-fitstime-compareto-eleris-fitsnet-wcs-fitstime) | Orders two instants of the same time scale. |
| [`Equals(FitsTime)`](#eleris-fitsnet-wcs-fitstime-equals-eleris-fitsnet-wcs-fitstime) | Whether two instants are the same representation: same time scale, same day and same second of the day. |
| [`Equals(object)`](#eleris-fitsnet-wcs-fitstime-equals-system-object) | Whether `obj` is an instant equal to this one (see [`FitsTime.Equals`](#eleris-fitsnet-wcs-fitstime-equals-eleris-fitsnet-wcs-fitstime)). |
| [`FromDateTime(long, int, int, int, int, double, TimeScale)`](#eleris-fitsnet-wcs-fitstime-fromdatetime-system-int64-system-int32-system-int32-system-int32-system-int32-system-double-eleris-fitsnet-wcs-timescale) | Creates an instant from a calendar date and time (proleptic Gregorian calendar, year zero exists as in ISO-8601). |
| [`FromJd(double, TimeScale)`](#eleris-fitsnet-wcs-fitstime-fromjd-system-double-eleris-fitsnet-wcs-timescale) | Creates an instant from a Julian Date (its precision is that of a double: about 40 microseconds today). |
| [`FromJdParts(double, double, TimeScale)`](#eleris-fitsnet-wcs-fitstime-fromjdparts-system-double-system-double-eleris-fitsnet-wcs-timescale) | Creates an instant from the two parts of a Julian Date (for example its integer and fractional parts). |
| [`FromMjd(double, TimeScale)`](#eleris-fitsnet-wcs-fitstime-frommjd-system-double-eleris-fitsnet-wcs-timescale) | Creates an instant from a Modified Julian Date. |
| [`FromMjdParts(double, double, TimeScale)`](#eleris-fitsnet-wcs-fitstime-frommjdparts-system-double-system-double-eleris-fitsnet-wcs-timescale) | Creates an instant from the integer and fractional parts of a Modified Julian Date (the fraction may be any size). |
| [`GetHashCode()`](#eleris-fitsnet-wcs-fitstime-gethashcode) | A hash code consistent with [`FitsTime.Equals`](#eleris-fitsnet-wcs-fitstime-equals-eleris-fitsnet-wcs-fitstime). |
| [`Parse(string, TimeScale)`](#eleris-fitsnet-wcs-fitstime-parse-system-string-eleris-fitsnet-wcs-timescale) | Parses a FITS datetime: `CCYY-MM-DD[Thh:mm:ss[.s...]]` or the signed five-digit year form `±CCCCC-MM-DD...`. |
| [`SecondsSince(FitsTime)`](#eleris-fitsnet-wcs-fitstime-secondssince-eleris-fitsnet-wcs-fitstime) | SI seconds from `origin` to this instant; both must be on the same scale. |
| [`To(TimeScale)`](#eleris-fitsnet-wcs-fitstime-to-eleris-fitsnet-wcs-timescale) | Converts to another time scale. |
| [`ToDateTime()`](#eleris-fitsnet-wcs-fitstime-todatetime) | The calendar date and time of day of the instant (second 60 on a leap-second day). |
| [`ToIsoString(int)`](#eleris-fitsnet-wcs-fitstime-toisostring-system-int32) | Formats the instant as a FITS datetime with the given number of decimals of the seconds. |
| [`ToString()`](#eleris-fitsnet-wcs-fitstime-tostring) | The instant and its time scale, for example `2020-01-01T00:00:00.000000 UTC`. |
| [`TryParse(string, TimeScale, FitsTime)`](#eleris-fitsnet-wcs-fitstime-tryparse-system-string-eleris-fitsnet-wcs-timescale-eleris-fitsnet-wcs-fitstime) | Like [`FitsTime.Parse`](#eleris-fitsnet-wcs-fitstime-parse-system-string-eleris-fitsnet-wcs-timescale) but returns false instead of throwing. |

## Operators

| Member | Description |
| --- | --- |
| [`static bool operator ==(FitsTime, FitsTime)`](#eleris-fitsnet-wcs-fitstime-op-equality-eleris-fitsnet-wcs-fitstime-eleris-fitsnet-wcs-fitstime) | Equality of two instants. |
| [`static bool operator !=(FitsTime, FitsTime)`](#eleris-fitsnet-wcs-fitstime-op-inequality-eleris-fitsnet-wcs-fitstime-eleris-fitsnet-wcs-fitstime) | Inequality of two instants. |

## Property details

<a id="eleris-fitsnet-wcs-fitstime-day"></a>
### `Day`

Modified Julian Day number (the instant is during this day of its scale).

```csharp
public long Day { get; }
```

<a id="eleris-fitsnet-wcs-fitstime-jd"></a>
### `Jd`

The instant as a Julian Date.

```csharp
public double Jd { get; }
```

<a id="eleris-fitsnet-wcs-fitstime-mjd"></a>
### `Mjd`

The instant as a Modified Julian Date (JD - 2400000.5). On a UTC leap day the fraction of the day is scaled, as usual.

```csharp
public double Mjd { get; }
```

<a id="eleris-fitsnet-wcs-fitstime-scale"></a>
### `Scale`

The time scale the instant is expressed in.

```csharp
public TimeScale Scale { get; }
```

<a id="eleris-fitsnet-wcs-fitstime-secondofday"></a>
### `SecondOfDay`

Seconds elapsed since the start of [`FitsTime.Day`](#eleris-fitsnet-wcs-fitstime-day) (up to 86400 plus the leap second in UTC).

```csharp
public double SecondOfDay { get; }
```

## Method details

<a id="eleris-fitsnet-wcs-fitstime-addseconds-system-double"></a>
### `AddSeconds(double)`

The instant `seconds` SI seconds later (earlier if negative), on the same scale.

```csharp
public FitsTime AddSeconds(double seconds)
```

**Parameters**

- `seconds` — The duration in SI seconds; a negative value goes back in time.

<a id="eleris-fitsnet-wcs-fitstime-compareto-eleris-fitsnet-wcs-fitstime"></a>
### `CompareTo(FitsTime)`

Orders two instants of the same time scale.

```csharp
public int CompareTo(FitsTime other)
```

**Parameters**

- `other` — The instant to compare with, on the same time scale.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The instants are on different scales: convert one first.

<a id="eleris-fitsnet-wcs-fitstime-equals-eleris-fitsnet-wcs-fitstime"></a>
### `Equals(FitsTime)`

Whether two instants are the same representation: same time scale, same day and same second of the day. Two instants on different scales are not equal even when they are the same moment; compare [`FitsTime.SecondsSince`](#eleris-fitsnet-wcs-fitstime-secondssince-eleris-fitsnet-wcs-fitstime) after converting one with [`FitsTime.To`](#eleris-fitsnet-wcs-fitstime-to-eleris-fitsnet-wcs-timescale).

```csharp
public bool Equals(FitsTime other)
```

**Parameters**

- `other` — The instant to compare with.

<a id="eleris-fitsnet-wcs-fitstime-equals-system-object"></a>
### `Equals(object)`

Whether `obj` is an instant equal to this one (see [`FitsTime.Equals`](#eleris-fitsnet-wcs-fitstime-equals-eleris-fitsnet-wcs-fitstime)).

```csharp
public override bool Equals(object? obj)
```

**Parameters**

- `obj` — The object to compare with.

<a id="eleris-fitsnet-wcs-fitstime-fromdatetime-system-int64-system-int32-system-int32-system-int32-system-int32-system-double-eleris-fitsnet-wcs-timescale"></a>
### `FromDateTime(long, int, int, int, int, double, TimeScale)`

Creates an instant from a calendar date and time (proleptic Gregorian calendar, year zero exists as in ISO-8601).

```csharp
public static FitsTime FromDateTime(long year, int month, int day, int hour = 0, int minute = 0, double second = 0, TimeScale scale = TimeScale.UTC)
```

**Parameters**

- `year` — The year, from -99999 to 99999 (year 0 is 1 BCE).
- `month` — The month, 1 to 12.
- `day` — The day of the month, starting at 1.
- `hour` — The hour, 0 to 23.
- `minute` — The minute, 0 to 59.
- `second` — The seconds with their decimals, at least 0 and less than 60; 60 is accepted only for a leap second.
- `scale` — The time scale the date and time are expressed in.

<a id="eleris-fitsnet-wcs-fitstime-fromjd-system-double-eleris-fitsnet-wcs-timescale"></a>
### `FromJd(double, TimeScale)`

Creates an instant from a Julian Date (its precision is that of a double: about 40 microseconds today).

```csharp
public static FitsTime FromJd(double jd, TimeScale scale = TimeScale.UTC)
```

**Parameters**

- `jd` — The Julian Date.
- `scale` — The time scale the date is expressed in.

<a id="eleris-fitsnet-wcs-fitstime-fromjdparts-system-double-system-double-eleris-fitsnet-wcs-timescale"></a>
### `FromJdParts(double, double, TimeScale)`

Creates an instant from the two parts of a Julian Date (for example its integer and fractional parts).

```csharp
public static FitsTime FromJdParts(double part1, double part2, TimeScale scale = TimeScale.UTC)
```

**Parameters**

- `part1` — The first part of the Julian Date, for example its integer part.
- `part2` — The second part of the Julian Date, for example its fraction; the date is the sum of the two parts.
- `scale` — The time scale the date is expressed in.

<a id="eleris-fitsnet-wcs-fitstime-frommjd-system-double-eleris-fitsnet-wcs-timescale"></a>
### `FromMjd(double, TimeScale)`

Creates an instant from a Modified Julian Date.

```csharp
public static FitsTime FromMjd(double mjd, TimeScale scale = TimeScale.UTC)
```

**Parameters**

- `mjd` — The Modified Julian Date (JD - 2400000.5).
- `scale` — The time scale the date is expressed in.

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The date is not a number or lies outside the years -99999 to +99999.

<a id="eleris-fitsnet-wcs-fitstime-frommjdparts-system-double-system-double-eleris-fitsnet-wcs-timescale"></a>
### `FromMjdParts(double, double, TimeScale)`

Creates an instant from the integer and fractional parts of a Modified Julian Date (the fraction may be any size). The separate name avoids confusing it with [`FitsTime.FromMjd`](#eleris-fitsnet-wcs-fitstime-frommjd-system-double-eleris-fitsnet-wcs-timescale) and a time scale.

```csharp
public static FitsTime FromMjdParts(double integer, double fraction, TimeScale scale = TimeScale.UTC)
```

**Parameters**

- `integer` — The integer part of the Modified Julian Date.
- `fraction` — The fractional part, which may exceed one day.
- `scale` — The time scale the date is expressed in.

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — A part is not a number or the date lies outside the years -99999 to +99999.

<a id="eleris-fitsnet-wcs-fitstime-gethashcode"></a>
### `GetHashCode()`

A hash code consistent with [`FitsTime.Equals`](#eleris-fitsnet-wcs-fitstime-equals-eleris-fitsnet-wcs-fitstime).

```csharp
public override int GetHashCode()
```

<a id="eleris-fitsnet-wcs-fitstime-parse-system-string-eleris-fitsnet-wcs-timescale"></a>
### `Parse(string, TimeScale)`

Parses a FITS datetime: `CCYY-MM-DD[Thh:mm:ss[.s...]]` or the signed five-digit year form `±CCCCC-MM-DD...`. Time zones are not allowed. Second 60 is accepted in UTC on a day that ends with a leap second.

```csharp
public static FitsTime Parse(string text, TimeScale scale = TimeScale.UTC)
```

**Parameters**

- `text` — The text: `CCYY-MM-DD[Thh:mm:ss[.s...]]` or the signed five-digit year form.
- `scale` — The time scale the text is expressed in.

**Exceptions**

- [`FormatException`](https://learn.microsoft.com/dotnet/api/system.formatexception) — The text is not a FITS datetime.

<a id="eleris-fitsnet-wcs-fitstime-secondssince-eleris-fitsnet-wcs-fitstime"></a>
### `SecondsSince(FitsTime)`

SI seconds from `origin` to this instant; both must be on the same scale.

```csharp
public double SecondsSince(FitsTime origin)
```

**Parameters**

- `origin` — The instant to measure from, on the same time scale.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The instants are on different scales.

<a id="eleris-fitsnet-wcs-fitstime-to-eleris-fitsnet-wcs-timescale"></a>
### `To(TimeScale)`

Converts to another time scale. TAI, TT, UTC (from 1972), GPS and TCG convert among each other (TCG with the constant rate L\_G of the standard), TDB and TCB convert between them (L\_B and TDB\_0 of the standard). Anything else, such as UT1 or TT to TDB, needs external data and throws [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception).

```csharp
public FitsTime To(TimeScale target)
```

**Parameters**

- `target` — The time scale to convert to.

**Exceptions**

- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The conversion needs data that is not carried, or involves UTC before 1972.

<a id="eleris-fitsnet-wcs-fitstime-todatetime"></a>
### `ToDateTime()`

The calendar date and time of day of the instant (second 60 on a leap-second day).

```csharp
public (long Year, int Month, int Day, int Hour, int Minute, double Second) ToDateTime()
```

<a id="eleris-fitsnet-wcs-fitstime-toisostring-system-int32"></a>
### `ToIsoString(int)`

Formats the instant as a FITS datetime with the given number of decimals of the seconds.

```csharp
public string ToIsoString(int decimals = 3)
```

**Parameters**

- `decimals` — The number of decimals of the seconds, 0 to 9.

<a id="eleris-fitsnet-wcs-fitstime-tostring"></a>
### `ToString()`

The instant and its time scale, for example `2020-01-01T00:00:00.000000 UTC`.

```csharp
public override string ToString()
```

<a id="eleris-fitsnet-wcs-fitstime-tryparse-system-string-eleris-fitsnet-wcs-timescale-eleris-fitsnet-wcs-fitstime"></a>
### `TryParse(string, TimeScale, FitsTime)`

Like [`FitsTime.Parse`](#eleris-fitsnet-wcs-fitstime-parse-system-string-eleris-fitsnet-wcs-timescale) but returns false instead of throwing.

```csharp
public static bool TryParse(string? text, TimeScale scale, out FitsTime result)
```

**Parameters**

- `text` — The text to parse.
- `scale` — The time scale the text is expressed in.
- `result` — The instant when the text is valid.

## Operator details

<a id="eleris-fitsnet-wcs-fitstime-op-equality-eleris-fitsnet-wcs-fitstime-eleris-fitsnet-wcs-fitstime"></a>
### `static bool operator ==(FitsTime, FitsTime)` (equality)

Equality of two instants.

```csharp
public static bool operator ==(FitsTime a, FitsTime b)
```

**Parameters**

- `a` — The first instant.
- `b` — The second instant.

<a id="eleris-fitsnet-wcs-fitstime-op-inequality-eleris-fitsnet-wcs-fitstime-eleris-fitsnet-wcs-fitstime"></a>
### `static bool operator !=(FitsTime, FitsTime)` (inequality)

Inequality of two instants.

```csharp
public static bool operator !=(FitsTime a, FitsTime b)
```

**Parameters**

- `a` — The first instant.
- `b` — The second instant.
