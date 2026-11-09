# Changelog

## 0.2.0

Additions only: the API and the behaviour of 0.1.0 are unchanged (checked by package validation against 0.1.0).

- Cancellation: `ImageHdu.ReadPixels`, `ReadRawPixels`, `ReadRegion`, `ReadRawRegion` and `Subimage`, `CompressedImageHdu.Decompress`,
  `ReadPixels` and `ReadRawPixels`, and `FitsFile.Save` have an overload that takes a `CancellationToken`. It is checked between blocks
  of data and between tiles. A cancelled save to a path leaves no temporary file and an existing target untouched, and a cancelled save
  restores the headers.
- `FitsFile.Save(path, checksums, FitsSaveMode.CreateNew)` never replaces a file: the temporary file is put in place by an
  operation that fails if the path exists, so a file that appears during the save is kept too, and of concurrent `CreateNew` saves to
  one path exactly one succeeds. On Windows it is the move itself. On Unix, where the move of .NET checks the path then renames, the
  path is first claimed with an exclusive creation (`O_CREAT | O_EXCL`), then the temporary file is renamed over the claim: until
  then a 16-byte file is at the path, and a process killed in between leaves it there (see `docs/files.md`). The save then throws
  `FitsFileExistsException`, an `IOException`. `FitsSaveMode.Overwrite` is the behaviour of the other overloads.
- Asynchronous I/O: `ImageHdu.ReadPixelsAsync`, `ReadRawPixelsAsync`, `ReadRegionAsync` and `ReadRawRegionAsync`,
  `CompressedImageHdu.ReadPixelsAsync` and `ReadRawPixelsAsync`, `FitsFile.SaveAsync` to a stream and to a path (with `FitsSaveMode`).
  The file is read and written with asynchronous I/O, the conversion and the decompression run between two I/O operations, and the bytes,
  the cancellation and the guarantees of the save are those of the synchronous methods.
- `ImageHdu.CreateFromPlanes<T>` makes an image from one `ReadOnlyMemory<T>` per plane, without copying them into one array or into
  the bytes of the file: they are converted a block at a time when the file is saved. The file is byte for byte the one `Create<T>` writes.

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
