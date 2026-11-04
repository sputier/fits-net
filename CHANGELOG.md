# Changelog

## 0.1.0

First release of `Eleris.FitsNet`: a managed .NET library (net8.0 and net10.0, no dependency) to read, write and edit FITS files.

- FITS 4.0: images, ASCII and binary tables, random groups, tile compression of images and tables, `CHECKSUM` / `DATASUM`, world
  coordinate systems (the 27 projections, spectral axes, alternate descriptions), time coordinates, the conventions of the appendices.
  Section by section status in `docs/standard-coverage.md`.
- The `SIP` and `TPV` distortion conventions are applied, in both directions, as astropy does.
- Files are opened lazily; pixels are read and written in one pass; tiles are compressed and decompressed on all the cores.
- A damaged or hostile file fails with a `FitsException`, never with an index, overflow or out-of-memory error.
- Checked against astropy, wcslib and ERFA by the test suite.

Not done yet: `-TAB`, `-GRI`, `-GRA`, `-WAT` and `-DSS` coordinates, `UT1` and `TDB` conversions, the smoothing hint of lossy
`HCOMPRESS_1` data, images of more than 2 GB read in one piece.
