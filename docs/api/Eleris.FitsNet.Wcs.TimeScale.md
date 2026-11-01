# TimeScale Enum

**Namespace:** `Eleris.FitsNet.Wcs`

The time scales of Table 30 of the standard (synonyms are folded: TDT and ET into TT, IAT into TAI, GMT into UTC).

```csharp
public enum TimeScale : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-fitsnet-wcs-timescale-tai"></a>`TAI` | 0 | International Atomic Time (also `IAT`). |
| <a id="eleris-fitsnet-wcs-timescale-tt"></a>`TT` | 1 | Terrestrial Time (also `TDT` and `ET`). |
| <a id="eleris-fitsnet-wcs-timescale-utc"></a>`UTC` | 2 | Coordinated Universal Time (also `GMT`); leap seconds are supported from 1972. |
| <a id="eleris-fitsnet-wcs-timescale-ut1"></a>`UT1` | 3 | Universal Time; it cannot be converted without Earth orientation data. |
| <a id="eleris-fitsnet-wcs-timescale-gps"></a>`GPS` | 4 | GPS time, TAI - 19 s. |
| <a id="eleris-fitsnet-wcs-timescale-tcg"></a>`TCG` | 5 | Geocentric Coordinate Time. |
| <a id="eleris-fitsnet-wcs-timescale-tcb"></a>`TCB` | 6 | Barycentric Coordinate Time. |
| <a id="eleris-fitsnet-wcs-timescale-tdb"></a>`TDB` | 7 | Barycentric Dynamical Time. |
| <a id="eleris-fitsnet-wcs-timescale-local"></a>`Local` | 8 | A local clock (simulations, free-running clocks): no conversion exists. |
