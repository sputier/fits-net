# Sample data

`vespera/` holds three raw frames produced by a Vaonis **Vespera Pro 2** smart telescope (IMX678 sensor, Bayer pattern `GBRG`):

| File | Target | Notes |
|---|---|---|
| `img-0022-M42-broadband.fits` | M42 | broadband |
| `img-0078-M45.fits` | M45 | |
| `img-1200-M42-dualband.fits` | M42 | dual-band filter |

Each file is a primary HDU of 3536 x 3536 pixels, `BITPIX = 16` with `BZERO = 32768` (unsigned 16 bit, the values only use 12 bits), 25 MB.
They are real data, used by the tests (`RealFilesTests`, checked against astropy) and by the benchmarks. Do not modify them: the tests compare
against statistics computed from these exact files.
