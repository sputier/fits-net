# WorldAxisKind Enum

**Namespace:** `FitsNet.Wcs`

The kind of a world coordinate axis.

```csharp
public enum WorldAxisKind : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="fitsnet-wcs-worldaxiskind-linear"></a>`Linear` | 0 | An ordinary linear axis. |
| <a id="fitsnet-wcs-worldaxiskind-celestial"></a>`Celestial` | 1 | Longitude or latitude of a celestial projection. |
| <a id="fitsnet-wcs-worldaxiskind-spectral"></a>`Spectral` | 2 | A spectral coordinate (frequency, wavelength, velocity, ...). |
| <a id="fitsnet-wcs-worldaxiskind-time"></a>`Time` | 3 | Time (`TIME` or one of the time scales of Table 30). |
| <a id="fitsnet-wcs-worldaxiskind-phase"></a>`Phase` | 4 | Temporal phase (`PHASE`). |
| <a id="fitsnet-wcs-worldaxiskind-timelag"></a>`TimeLag` | 5 | Time lag of correlation spectra (`TIMELAG`). |
| <a id="fitsnet-wcs-worldaxiskind-temporalfrequency"></a>`TemporalFrequency` | 6 | Temporal frequency (`FREQUENCY`). |
| <a id="fitsnet-wcs-worldaxiskind-stokes"></a>`Stokes` | 7 | Polarization (`STOKES`); see [`StokesParameters`](FitsNet.StokesParameters.md) for the conventional values (section 8.5). |
| <a id="fitsnet-wcs-worldaxiskind-complex"></a>`Complex` | 8 | Real, imaginary (and weight) components of complex data (`COMPLEX`, section 8.5): coordinates 1, 2 and 3. |
