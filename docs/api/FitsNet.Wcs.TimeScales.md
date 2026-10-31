# TimeScales Class

**Namespace:** `FitsNet.Wcs`

**Inheritance:** `object` → `TimeScales`

Names, synonyms and conversions of time scales.

```csharp
public static class TimeScales
```

## Methods

| Member | Description |
| --- | --- |
| [`CanConvert(TimeScale, TimeScale)`](#fitsnet-wcs-timescales-canconvert-fitsnet-wcs-timescale-fitsnet-wcs-timescale) | Whether [`FitsTime.To`](FitsNet.Wcs.FitsTime.md#fitsnet-wcs-fitstime-to-fitsnet-wcs-timescale) can convert between two scales. |
| [`Parse(string)`](#fitsnet-wcs-timescales-parse-system-string) | The scale named by a TIMESYS-like value, ignoring a realization in parentheses such as `TT(TAI)`; null if unknown. |

## Method details

<a id="fitsnet-wcs-timescales-canconvert-fitsnet-wcs-timescale-fitsnet-wcs-timescale"></a>
### `CanConvert(TimeScale, TimeScale)`

Whether [`FitsTime.To`](FitsNet.Wcs.FitsTime.md#fitsnet-wcs-fitstime-to-fitsnet-wcs-timescale) can convert between two scales.

```csharp
public static bool CanConvert(TimeScale from, TimeScale to)
```

**Parameters**

- `from` — The scale to convert from.
- `to` — The scale to convert to.

<a id="fitsnet-wcs-timescales-parse-system-string"></a>
### `Parse(string)`

The scale named by a TIMESYS-like value, ignoring a realization in parentheses such as `TT(TAI)`; null if unknown.

```csharp
public static TimeScale? Parse(string? name)
```

**Parameters**

- `name` — The name, for example `UTC` or `TT(TAI)`.
