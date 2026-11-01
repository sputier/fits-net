# TimeScales Class

**Namespace:** `Eleris.FitsNet.Wcs`

**Inheritance:** `object` → `TimeScales`

Names, synonyms and conversions of time scales.

```csharp
public static class TimeScales
```

## Methods

| Member | Description |
| --- | --- |
| [`CanConvert(TimeScale, TimeScale)`](#eleris-fitsnet-wcs-timescales-canconvert-eleris-fitsnet-wcs-timescale-eleris-fitsnet-wcs-timescale) | Whether [`FitsTime.To`](Eleris.FitsNet.Wcs.FitsTime.md#eleris-fitsnet-wcs-fitstime-to-eleris-fitsnet-wcs-timescale) can convert between two scales. |
| [`Parse(string)`](#eleris-fitsnet-wcs-timescales-parse-system-string) | The scale named by a TIMESYS-like value, ignoring a realization in parentheses such as `TT(TAI)`; null if unknown. |

## Method details

<a id="eleris-fitsnet-wcs-timescales-canconvert-eleris-fitsnet-wcs-timescale-eleris-fitsnet-wcs-timescale"></a>
### `CanConvert(TimeScale, TimeScale)`

Whether [`FitsTime.To`](Eleris.FitsNet.Wcs.FitsTime.md#eleris-fitsnet-wcs-fitstime-to-eleris-fitsnet-wcs-timescale) can convert between two scales.

```csharp
public static bool CanConvert(TimeScale from, TimeScale to)
```

**Parameters**

- `from` — The scale to convert from.
- `to` — The scale to convert to.

<a id="eleris-fitsnet-wcs-timescales-parse-system-string"></a>
### `Parse(string)`

The scale named by a TIMESYS-like value, ignoring a realization in parentheses such as `TT(TAI)`; null if unknown.

```csharp
public static TimeScale? Parse(string? name)
```

**Parameters**

- `name` — The name, for example `UTC` or `TT(TAI)`.
