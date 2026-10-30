# FitsNet

A pure managed .NET library (net10.0 and net8.0, no dependency) to read, write and manipulate **FITS** files, written against the
FITS 4.0 standard and checked against astropy / cfitsio.

```csharp
using var file = FitsFile.Open("frame.fits");               // reads on demand: a 25 MB frame opens in milliseconds
var image = (ImageHdu)file.Primary;
ushort[] pixels = image.ReadPixels<ushort>();                // BZERO / BSCALE / unsigned conventions applied
ushort[] cutout = image.ReadRegion<ushort>([1500, 1500], [256, 256]);   // only these bytes are read

string target = image.Header.Get<string>("OBJECT")!;
image.Header.Set("OBSERVER", "me", "who took it");

var packed = image.Compress(new CompressionOptions { Algorithm = CompressionAlgorithm.Rice });   // tile compression
new FitsFile().Add(packed).Save("frame.fits.fz", ChecksumPolicy.Always);
```

## What is covered

| Standard | Support |
|---|---|
| Blocks, cards, header keywords, all value types, `CONTINUE` long strings, `COMMENT`/`HISTORY`, `HIERARCH` (explicit API) | complete; strict and tolerant reading |
| Primary array and `IMAGE` extensions, `BITPIX` 8/16/32/64/-32/-64, `BSCALE`/`BZERO`/`BLANK`, unsigned conventions | complete |
| Random groups | complete |
| ASCII tables (`TABLE`) | `A`, `I`, `F`, `E`, `D`, `TNULL`, `TBCOL`, implied decimals |
| Binary tables (`BINTABLE`) | `L X B I J K A E D C M P Q`, `TDIM`, `TSCAL`/`TZERO`/`TNULL`, variable-length arrays and heap, `THEAP` |
| `CHECKSUM` / `DATASUM` (appendix J) | verify and write |
| Tile-compressed images (section 10) | `RICE_1`, `GZIP_1`, `GZIP_2`, `PLIO_1`, `HCOMPRESS_1` (lossless and lossy), `NOCOMPRESS`, float quantization with `NO_DITHER` / `SUBTRACTIVE_DITHER_1` / `SUBTRACTIVE_DITHER_2` |
| Tile-compressed tables (section 10.3) | `RICE_1` (integer columns), `GZIP_1`, `GZIP_2`, `NOCOMPRESS`; fixed and variable-length (`P`/`Q`) columns, per-column algorithms, `FZTILELN` / `FZALGOR` / `FZALGn` directives; `CompressedTableHdu.Compress` / `Decompress`. Not cross-checked against cfitsio (no independent tool available) |
| World coordinate systems (section 8) | `WorldCoordinateSystem`: pixel to world and back with `PCi_j` / `CDi_j` / `CROTAi`, alternate descriptions A to Z, the 27 celestial projections (`AZP SZP TAN STG SIN ARC ZPN ZEA AIR CYP CEA CAR MER SFL PAR MOL AIT COP COE COD COO BON PCO TSC CSC QSC HPX`) with `LONPOLE` / `LATPOLE` / `PVi_m` / `PVi_0` fiducial offsets, spectral axes (all types of Table 25, the `-F2W`-style algorithms and `-LOG`), conventional `STOKES` / `COMPLEX` axes. Checked point by point against wcslib (through astropy) on about 49 000 pixels and 20 000 independent world positions, in both directions; `PixelToWorld` / `WorldToPixel` also take spans (many points, no allocation). `-TAB`, `-GRI` and `-GRA` are refused; distortion conventions (`-SIP`, `-TPV`...) are not applied |
| Time (section 9) | `FitsTime` (ISO-8601 including five-digit years and leap seconds, JD/MJD, nanosecond resolution), `TimeFrame` (`TIMESYS`, `MJDREF` / `JDREF` / `DATEREF` with their parts, `TIMEUNIT`, `TIMEOFFS`, `TREFPOS`, errors, `DATE-*` / `MJD-*`, `TSTART` / `TSTOP`, epochs, axis and column overrides), conversions between TAI, TT, UTC (1972 on), GPS, TCG and TDB / TCB checked against ERFA; UTC before 1972 works as a uniform scale for arithmetic only. UT1 and TT to TDB need external data and are refused. Years -99999 to +99999 |
| Conventions of the appendices | conforming extensions `IUEIMAGE` and `A3DTABLE` (F.2), `FOREIGN` / `DUMP` kept verbatim, MIME types (G), header inheritance `INHERIT` (K: `GetEffectiveHeader`), Green Bank keyword/column convention (L) |

Not implemented: the smoothing hint of lossy `HCOMPRESS_1` (such a file is refused with a clear exception). The standard has no
section on hierarchical grouping (it is a registered convention outside the standard), so there is no support for it.

## Design points

* **Reliable on bad input**: damaged or hostile files fail with `FitsException` (never with an out-of-range, overflow or out-of-memory error);
  sizes announced by headers are checked against the real data before anything is allocated.
* **Fast**: no per-pixel allocations, big-endian conversion with vectorised byte swaps, tiles compressed and decompressed in parallel (the output
  does not depend on the number of threads), columns of big tables read by blocks, cutouts read without loading the image.
* **Faithful**: a file read and saved again keeps its cards, comments and bytes; failed saves leave the in-memory model untouched; saving over
  the file you opened is safe.
* **Validated against astropy** both ways: files written by astropy are read identically, and files written here are accepted by astropy
  (including `verify`, checksums and tile compression), with byte-identical `HCOMPRESS_1` output.

## Repository layout

| Path | Content |
|---|---|
| `src/FitsNet` | the library |
| `tests/FitsNet.Tests` | xUnit tests, including the astropy cross-checks (`python` with `astropy` and `numpy` is required; set `FITSNET_SKIP_INTEROP=1` to skip them) |
| `benchmarks/` | BenchmarkDotNet projects, see `benchmarks/README.md` |
| `data/vespera` | three real raw frames from a Vaonis Vespera Pro 2 |
| `tools/` | Python scripts that generate the reference files and check the output with astropy |

```powershell
dotnet test                                                           # about 600 tests
dotnet run -c Release --project benchmarks/FitsNet.Benchmarks -- --filter "*" --job short
```

Thread safety: a `FitsFile` opened with `FitsFile.Open` reads through one file stream and must not be used from several threads at once.
Compression and decompression parallelise internally and are safe to call from several threads on different objects.
