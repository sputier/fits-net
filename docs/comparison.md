# Comparison with cfitsio and astropy

Eleris.FitsNet against [cfitsio](https://heasarc.gsfc.nasa.gov/fitsio/) 4.06 (the reference C library, used here through the
`fitsio` Python package, which bundles it) and [astropy](https://www.astropy.org) 8.0.1 (`astropy.io.fits`, which has its own copy of
the compression code of cfitsio), on the same files, on the same machine, in the same session. This page reports what was measured,
including the cases where Eleris.FitsNet is slower.

## What to remember

* **Reading and writing uncompressed data**: Eleris.FitsNet is 2 to 4 times faster than cfitsio and 4 to 25 times faster than astropy for
  images, and faster for binary and ASCII tables.
* **Tile compression on one core**: Eleris.FitsNet is *slower* than cfitsio for most operations. The biggest gaps are Rice compression
  (2.3 times slower), `GZIP_2` compression (1.9 times), and the decompression of quantized `float32` images (1.9 times). It is faster for
  `float32` compression and for `HCOMPRESS` decompression.
* **Tile compression on all cores**: the tiles are independent, and Eleris.FitsNet compresses and decompresses them in parallel
  (`MaxDegreeOfParallelism`, all cores by default). On the 8 cores of the test machine it is faster than cfitsio for every operation,
  by 1.4 to 6 times. cfitsio and astropy work on one core.
* astropy is itself slower than cfitsio for several operations (its Python layer costs time), so a result "as fast as astropy" is not
  "as fast as cfitsio".

## Method

| | |
|---|---|
| Machine | Intel Core Ultra 7 258V (8 cores), Windows 11 |
| Eleris.FitsNet | .NET 10.0.12, Release, [BenchmarkDotNet](https://benchmarkdotnet.org) 0.15.8, job `MediumRun` (2 launches, 10 warm-up and 15 measured iterations); the median is used |
| cfitsio | `fitsio` 1.4.2, which bundles cfitsio 4.060; Python 3.14.5, numpy 2.5.1 |
| astropy | astropy 8.0.1, Python 3.14.5, `memmap=False` for the pixel reads |
| Data | The three real frames of `data/vespera` (Vaonis Vespera Pro 2, 3536 × 3536, 12 bits stored as 16 bits, 25 MB); synthetic tables |
| Python protocol | The median of at least 5 and up to 200 runs (6 to 8 seconds per operation), three complete passes, and the **best of the three medians** is kept: on a laptop that adapts its clock, a pass can only be slower than the machine can do, never faster |

Every figure is in **milliseconds** for the whole operation, from the call to the result in memory. Reading and writing are done on files
that the system has in its cache.

### What is, and is not, the same work

* The Python figures include the cost of Python itself and of the numpy array that receives the result. For the I/O of pixels, which
  takes a few milliseconds, that cost is small but not zero.
* Files are opened at each run by cfitsio and astropy. For Eleris.FitsNet the "Open +" operations include the opening; the table reads
  and the cutouts are noted in the tables when they work on a file or a table that is already open.
* Creating an image and saving it are measured together for Eleris.FitsNet, because the conversion of the pixels to the big-endian
  representation is done when the image is created; the Python libraries do it when they write.
* cfitsio writes its output to a **file**, not to memory, for the compressions and the tables: its in-memory files (`mem://`) grow by
  reallocating at every tile and take 8 seconds to write what a file takes 70 milliseconds to receive. Eleris.FitsNet compresses to memory.
  The comparison is therefore slightly in favour of Eleris.FitsNet for the compression.
* astropy compresses and decompresses on one core; so does cfitsio. The compression options are the same everywhere: one image row per
  tile for Rice and GZIP, 16 rows for `HCOMPRESS` (lossless), `SUBTRACTIVE_DITHER_1` with a quantization level of 4 for the `float32` image
  of the dual band frame.
* Compressed files written by Eleris.FitsNet are read back by astropy, and the other way round: the interoperability job of the continuous
  integration checks it on every push. The compression ratios are not compared on this page.

## Results

A figure in bold is a gap of 1.5 times or more in favour of Eleris.FitsNet, an italic one a gap against it. Ratios between 0.95 and 1.05
are shown as "same". The run-to-run variation of the machine is about 10 to 30 percent: do not read anything into a ratio close to 1.

<!-- BEGIN TABLES -->

### Images: reading, cutouts, writing, checksums (one 3536 × 3536 frame, 25 MB)

| Operation | Eleris.FitsNet (ms) | cfitsio (ms) | astropy (ms) | against cfitsio | against astropy |
|---|---:|---:|---:|---|---|
| Open, header only | 0.03 | 0.09 | 0.36 | **2.7× faster** | **11.1× faster** |
| Open + read `ushort` pixels | 6.2 | 14 | 43 | **2.3× faster** | **7.0× faster** |
| Open + read `float32` | 11 | 29 | 47 | **2.7× faster** | **4.4× faster** |
| Open + read `float64` | 11 | 43 | 60 | **3.8× faster** | **5.4× faster** |
| Cutout 256 × 256 | 0.40 | 1.0 | 3.9 | **2.5× faster** | **9.6× faster** |
| Cutout 16 × 3536 (a narrow column) | 6.0 | 14 | 44 | **2.4× faster** | **7.3× faster** |
| Cutout 3536 × 64 (64 whole rows) | 0.17 | 0.31 | 1.1 | **1.8× faster** | **6.3× faster** |
| Create the image from pixels + save to memory | 4.8 | 9.2 | 47 | **1.9× faster** | **9.7× faster** |
| Create the image from pixels + save to memory with `CHECKSUM` + `DATASUM` | 6.6 | 16 | 75 | **2.4× faster** | **11.3× faster** |
| Verify `CHECKSUM` + `DATASUM` | 5.7 | 15 | 146 | **2.6× faster** | **25.6× faster** |

### Tile compression on one core (`MaxDegreeOfParallelism = 1`)

| Operation | Eleris.FitsNet (ms) | cfitsio (ms) | astropy (ms) | against cfitsio | against astropy |
|---|---:|---:|---:|---|---|
| Compress Rice | 154 | 68 | 131 | *2.3× slower* | *1.2× slower* |
| Decompress Rice | 75 | 62 | 112 | *1.2× slower* | 1.5× faster |
| Compress GZIP_1 | 525 | 397 | 757 | *1.3× slower* | 1.4× faster |
| Decompress GZIP_1 | 131 | 119 | 159 | *1.1× slower* | 1.2× faster |
| Compress GZIP_2 | 531 | 276 | 924 | *1.9× slower* | **1.7× faster** |
| Decompress GZIP_2 | 103 | 106 | 222 | same | **2.2× faster** |
| Compress HCOMPRESS | 313 | 247 | 304 | *1.3× slower* | same |
| Decompress HCOMPRESS | 274 | 328 | 374 | 1.2× faster | 1.4× faster |
| Compress `float32` (Rice, dither 1) | 293 | 522 | 434 | **1.8× faster** | 1.5× faster |
| Decompress `float32` (Rice, dither 1) | 138 | 72 | 109 | *1.9× slower* | *1.3× slower* |

### Tile compression on all 8 cores (`MaxDegreeOfParallelism = -1`)

| Operation | Eleris.FitsNet (ms) | cfitsio (ms) | astropy (ms) | against cfitsio | against astropy |
|---|---:|---:|---:|---|---|
| Compress Rice | 49 | 68 | 131 | 1.4× faster | **2.7× faster** |
| Decompress Rice | 26 | 62 | 112 | **2.4× faster** | **4.3× faster** |
| Compress GZIP_1 | 103 | 397 | 757 | **3.9× faster** | **7.4× faster** |
| Decompress GZIP_1 | 38 | 119 | 159 | **3.1× faster** | **4.2× faster** |
| Compress GZIP_2 | 112 | 276 | 924 | **2.5× faster** | **8.2× faster** |
| Decompress GZIP_2 | 45 | 106 | 222 | **2.3× faster** | **4.9× faster** |
| Compress HCOMPRESS | 86 | 247 | 304 | **2.9× faster** | **3.5× faster** |
| Decompress HCOMPRESS | 71 | 328 | 374 | **4.6× faster** | **5.3× faster** |
| Compress `float32` (Rice, dither 1) | 84 | 522 | 434 | **6.2× faster** | **5.2× faster** |
| Decompress `float32` (Rice, dither 1) | 35 | 72 | 109 | **2.1× faster** | **3.1× faster** |

cfitsio and astropy work on one core whatever the machine: their columns are the same as in the previous table.

### Binary tables, 200 000 rows of 40 bytes (6 columns)

| Operation | Eleris.FitsNet (ms) | cfitsio (ms) | astropy (ms) | against cfitsio | against astropy |
|---|---:|---:|---:|---|---|
| Create the table from column arrays | 7.2 | – | 84 | – | **11.6× faster** |
| Read a `double` column | 2.5 | 6.9 | 7.9 | **2.7× faster** | **3.1× faster** |
| Read a string column | 12 | 29 | 61 | **2.4× faster** | **5.1× faster** |
| Save | 3.0 | 49 | 72 | **16.6× faster** | **24.4× faster** |

Eleris.FitsNet reads the `double` column from a file opened beforehand and the string column from a table already in memory; cfitsio and astropy open the file at each run (0.1 ms and 0.4 ms).

### ASCII tables, 200 000 rows (4 columns)

| Operation | Eleris.FitsNet (ms) | cfitsio (ms) | astropy (ms) | against cfitsio | against astropy |
|---|---:|---:|---:|---|---|
| Create the table from column arrays | 44 | – | 88 | – | **2.0× faster** |
| Read an `F12.5` column | 9.5 | 55 | 44 | **5.8× faster** | **4.6× faster** |
| Read an `E16.8` column | 10 | 53 | 48 | **5.1× faster** | **4.7× faster** |
| Read an integer column | 1.5 | 34 | 37 | **22.7× faster** | **24.7× faster** |

Eleris.FitsNet reads tables that are already in memory; cfitsio and astropy open the file at each run.

<!-- END TABLES -->

## Reproduce

The raw results of this page are in `benchmarks/comparison/results`, and the tables above are generated from them by
`benchmarks/comparison/make_report.py`.

```powershell
pip install fitsio astropy numpy
# Eleris.FitsNet: images and compression, then tables (long: allow an hour)
dotnet run -c Release --project benchmarks/Eleris.FitsNet.Benchmarks -- --filter "*ImageIoBenchmarks*" "*CompressionBenchmarks*" "*FloatCompressionBenchmarks*" --job medium
dotnet run -c Release --project benchmarks/Eleris.FitsNet.Benchmarks.Tables -- --filter "*BinaryTableBenchmarks*" "*AsciiTableBenchmarks*" --job medium
# cfitsio and astropy: three passes (about 15 minutes)
sh benchmarks/comparison/run_python_passes.sh
```

Copy the `*-report-github.md` files of BenchmarkDotNet into `benchmarks/comparison/results/dotnet`, then run
`python benchmarks/comparison/make_report.py`. Run each series alone on the machine: two benchmarks at the same time disturb each other.
