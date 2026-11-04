# Standard coverage

This page lists, section by section, how Eleris.FitsNet follows the FITS 4.0 standard, and what it does not do. "Supported" means that the feature is implemented and tested. The notes under each section are the limits you should know about.

## Overview

| Section of the standard | Status |
| --- | --- |
| 3 Overall file structure | Supported |
| 4 Headers (keywords, values, commentary, long strings) | Supported |
| 5 Data representation | Supported |
| 6 Random-groups structure | Supported |
| 7.1 Image extension | Supported |
| 7.2 ASCII table extension | Supported |
| 7.3 Binary table extension, variable-length arrays | Supported |
| 8 World coordinate systems | Supported, with the limits below |
| 9 Representations of time coordinates | Supported, with the limits below |
| 10 Compression: 10.1 images, 10.3 tables | Supported, with the limits below |
| Appendix F Prototype extensions (`IUEIMAGE`, `A3DTABLE`) | Supported |
| Appendix G MIME types | Supported |
| Appendix J Checksum | Supported |
| Appendix K Header inheritance | Supported |
| Appendix L Green Bank convention | Supported |

## Files, headers and data

- Files are read and written in blocks of 2880 bytes. Extensions that the library does not know are kept as [`GenericHdu`](api/Eleris.FitsNet.GenericHdu.md): their header and bytes survive a read and a save.
- Keyword values: strings, integers, floating point numbers, logical values, complex numbers and undefined values.
- `HIERARCH` keywords and the long string convention (`CONTINUE`) are not part of the standard and are supported as conventions.
- Arrays that are read in one piece are limited to the size of a .NET array (about 2 GB). Larger images are read by region.
- An array has at most 999 axes and a table at most 999 columns, as the keywords require.

## Tables

- ASCII tables: formats `A`, `I`, `F`, `E` and `D`. Undefined values are supported.
- Binary tables: all the column types (`L`, `X`, `B`, `I`, `J`, `K`, `A`, `E`, `D`, `C`, `M`), `TDIM`, `TSCAL`, `TZERO`, `TNULL`, the unsigned conventions, variable-length arrays (`P` and `Q` descriptors).
- Compressed tables (section 10.3) have not been cross-checked with files written by cfitsio. They are tested by round trips through the library.

## Compression

- Images: `RICE_1`, `GZIP_1`, `GZIP_2`, `PLIO_1`, `HCOMPRESS_1`, `NOCOMPRESS`; quantization of floating point images with subtractive dithering (two variants), or no quantization. Tiles are processed on all the cores and the result does not depend on the number of threads.
- The smoothing hint of lossy `HCOMPRESS_1` data (`ZNAME = 'SMOOTH'` with a value of 1) is not implemented. An image that asks for it throws a `NotSupportedException` on decompression, rather than returning pixels that differ from what cfitsio gives.
- Tables: each column is compressed on its own in tiles of rows, with `RICE_1` (integer columns), `GZIP_1`, `GZIP_2` or `NOCOMPRESS`. `PLIO_1` and `HCOMPRESS_1` are for images only.

## World coordinates

- The 27 celestial projections, the rotation by `PCi_j`, `CDi_j` or `CROTAi`, the poles (`LONPOLE`, `LATPOLE`, `PVi_m`), and alternate descriptions `A` to `Z`.
- Spectral axes, linear, logarithmic or with the algorithms of Table 26 (`-F2W` and the others), when the rest frequency or wavelength is known where it is needed.
- Not supported, with a `NotSupportedException`: the lookup table coordinates (`-TAB`) and the grism algorithms (`-GRI`, `-GRA`).
- Applied, although they are not part of the standard: the distortion conventions `-SIP` (polynomials `A_p_q`, `B_p_q` and their inverses) and `TPV` (the polynomials of SCAMP, up to the seventh degree). `Distortion` tells which one a description uses; the results are checked against astropy.
- Not applied: the other conventions written after the projection code (`-DSS`, `-WAT`...). The conversion uses the plain projection and `UnappliedDistortion` reports the convention.
- Celestial axes can use the units `deg`, `arcmin`, `arcsec`, `mas` and `rad`.
- The conversion of celestial coordinates between reference frames (for example FK5 to Galactic) is not done by this library: `RadeSys` and `Equinox` tell the frame, and the conversion between frames is left to you.

## Time

- Time scales: `TAI`, `TT`, `UTC`, `GPS`, `TCG`, `TCB`, `TDB`, and `UT1` and `Local` as labels.
- Conversions between `TAI`, `TT`, `UTC` (from 1972), `GPS` and `TCG`, and between `TCB` and `TDB`, checked against ERFA.
- Not supported: conversions to or from `UT1`, from `TT` or `TCG` to `TDB` and the other way (they need a time ephemeris), and `UTC` before 1972.
- The keywords of section 9 (`TIMESYS`, `MJDREF`, `TIMEUNIT`, `TREFPOS`, `DATE-OBS`, `TSTART`...) are read, for the whole HDU, for a table column and for an image axis. The positions of the observer and the correction of times between positions (for example from the topocenter to the barycenter) are not computed.

## Checks against other software

The output of the library is compared with astropy for headers, images, tables, compression and checksums, with wcslib for the celestial projections, and with ERFA for the time scales. The reference values are stored in the test suite.
