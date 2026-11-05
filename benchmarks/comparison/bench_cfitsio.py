"""The operations of the Eleris.FitsNet benchmarks, with cfitsio (through the fitsio package, which bundles it).

Same files as the .NET benchmarks (data/vespera), medians in milliseconds, written to results/cfitsio-<BENCH_PASS>.json next to this script.
Run with: python benchmarks/comparison/bench_cfitsio.py
"""
import json
import os
import statistics
import sys
import tempfile
import time

import fitsio
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
RESULTS_DIR = os.path.join(HERE, "results")
os.makedirs(RESULTS_DIR, exist_ok=True)
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", "data", "vespera"))
BROAD = os.path.join(ROOT, "img-0022-M42-broadband.fits")
DUAL = os.path.join(ROOT, "img-1200-M42-dualband.fits")
RESULTS = {}
SIZES = {}
TMP = tempfile.mkdtemp(prefix="cfitsio-bench-")


def bench(name, fn, warmup=3, budget=6.0, minimum=5, maximum=200):
    for _ in range(warmup):
        fn()
    samples = []
    start = time.perf_counter()
    while len(samples) < maximum:
        t0 = time.perf_counter()
        fn()
        samples.append((time.perf_counter() - t0) * 1000)
        if time.perf_counter() - start > budget and len(samples) >= minimum:
            break
    RESULTS[name] = statistics.median(samples)
    print(f"{name:<58} {RESULTS[name]:10.3f} ms  (n={len(samples)})", flush=True)


SCRATCH = os.path.join(TMP, "scratch.fits")


def scratch():
    """A file in the temporary directory, replaced at every call: cfitsio's mem:// files grow by reallocation at every tile (8 s to
    write what takes 50 ms to a file), so the compression and the tables are written to a file in the cache of the system instead."""
    return fitsio.FITS(SCRATCH, "rw", clobber=True)


print(sys.version.split()[0], "fitsio", fitsio.__version__, "cfitsio", fitsio.cfitsio_version(), "numpy", np.__version__, flush=True)

with fitsio.FITS(BROAD) as f:
    u16 = f[0].read()
print("pixels", u16.dtype, u16.shape, flush=True)
with fitsio.FITS(DUAL) as f:
    dual = f[0].read().astype(np.float32)

# ---- plain image I/O -------------------------------------------------------------------------------------------------


def open_header():
    with fitsio.FITS(BROAD) as f:
        return f[0].read_header()["NAXIS1"]


def read_u16():
    with fitsio.FITS(BROAD) as f:
        return f[0].read()


def read_float32():
    with fitsio.FITS(BROAD) as f:
        return f[0].read().astype(np.float32)


def read_float64():
    with fitsio.FITS(BROAD) as f:
        return f[0].read().astype(np.float64)


def cutout_256():
    with fitsio.FITS(BROAD) as f:
        return f[0][1500:1756, 1500:1756]


def narrow_column():
    with fitsio.FITS(BROAD) as f:
        return f[0][:, 1000:1016]


def whole_rows():
    with fitsio.FITS(BROAD) as f:
        return f[0][2000:2064, :]


bench("Open (header only)", open_header)
bench("Open + read ushort", read_u16)
bench("Open + read float32", read_float32)
bench("Open + read float64", read_float64)
bench("Cutout 256x256", cutout_256)
bench("Cutout 16 x 3536 (narrow column)", narrow_column)
bench("Cutout 3536 x 64 (64 whole rows)", whole_rows)

# ---- writing ---------------------------------------------------------------------------------------------------------


def save_memory():
    with fitsio.FITS("mem://", "rw") as f:
        f.write(u16)


def save_checksum():
    with fitsio.FITS("mem://", "rw") as f:
        f.write(u16)
        f[0].write_checksum()


bench("Save to memory (ushort, no checksum)", save_memory)
bench("Save to memory, CHECKSUM + DATASUM", save_checksum)

checked = os.path.join(TMP, "checksummed.fits")
with fitsio.FITS(checked, "rw", clobber=True) as f:
    f.write(u16)
    f[0].write_checksum()


def verify_checksums():
    with fitsio.FITS(checked) as f:
        return f[0].verify_checksum()


bench("Open + verify CHECKSUM/DATASUM (reads data)", verify_checksums)

# ---- tile compression: integers --------------------------------------------------------------------------------------
# one row per tile for Rice and GZIP, 16 rows for HCOMPRESS (the same as the .NET benchmarks)
for label, algo, tile in [("Rice", "RICE", (1, 3536)), ("Gzip1", "GZIP", (1, 3536)), ("Gzip2", "GZIP_2", (1, 3536)),
                          ("HCompress", "HCOMPRESS", (16, 3536))]:
    def compress(algo=algo, tile=tile):
        with scratch() as f:
            f.write(u16, compress=algo, tile_dims=tile)

    path = os.path.join(TMP, f"{label}.fits")
    try:
        with fitsio.FITS(path, "rw", clobber=True) as f:
            f.write(u16, compress=algo, tile_dims=tile)
        SIZES[label] = os.path.getsize(path)

        def decompress(path=path):
            with fitsio.FITS(path) as f:
                return f[1].read()

        bench(f"Compress {label} (ushort, to a file)", compress, budget=8.0)
        bench(f"Decompress {label} to ushort", decompress, budget=8.0)
        print(f"    ({label}: {SIZES[label] / 1e6:.2f} MB)", flush=True)
    except Exception as e:  # noqa: BLE001
        print(f"{label}: failed: {e}", flush=True)

# ---- tile compression: float32 with quantization ---------------------------------------------------------------------


def compress_float():
    with scratch() as f:
        f.write(dual, compress="RICE", tile_dims=(16, 3536), qlevel=4.0, qmethod="SUBTRACTIVE_DITHER_1")


floatpath = os.path.join(TMP, "float.fits")
with fitsio.FITS(floatpath, "rw", clobber=True) as f:
    f.write(dual, compress="RICE", tile_dims=(16, 3536), qlevel=4.0, qmethod="SUBTRACTIVE_DITHER_1")
SIZES["Float"] = os.path.getsize(floatpath)


def decompress_float():
    with fitsio.FITS(floatpath) as f:
        return f[1].read()


bench("Compress float32 (Rice, SUBTRACTIVE_DITHER_1)", compress_float, budget=8.0)
bench("Decompress float32 (Rice, SUBTRACTIVE_DITHER_1)", decompress_float, budget=8.0)
print(f"    (float: {SIZES['Float'] / 1e6:.2f} MB)", flush=True)

# ---- tables ----------------------------------------------------------------------------------------------------------
rows = 200_000
rng = np.random.default_rng(7)
table = np.zeros(rows, dtype=[("ID", ">i4"), ("RA", ">f8"), ("FLUX", ">f4", (3,)), ("FLAGS", ">i2"), ("NAME", "S12"), ("OK", "?")])
table["ID"] = np.arange(rows)
table["RA"] = rng.random(rows) * 360
table["FLUX"] = rng.random((rows, 3)).astype(np.float32)
table["FLAGS"] = np.arange(rows) % 1000
table["NAME"] = [f"src{i % 100000}".encode() for i in range(rows)]
table["OK"] = np.arange(rows) % 3 != 0
tpath = os.path.join(TMP, "table.fits")
with fitsio.FITS(tpath, "rw", clobber=True) as f:
    f.write(table)


def table_save():
    with scratch() as f:
        f.write(table)


def table_read(column):
    def run():
        with fitsio.FITS(tpath) as f:
            return f[1][column][:]
    return run


bench("Binary table 200k: save (to a file)", table_save)
bench("Binary table 200k: open + read int column", table_read("ID"))
bench("Binary table 200k: open + read double column", table_read("RA"))
bench("Binary table 200k: open + read string column (list)", lambda: table_read("NAME")().tolist())

atable = np.zeros(rows, dtype=[("ID", "i4"), ("X", "f8"), ("Y", "f8"), ("NAME", "S12")])
atable["ID"] = np.arange(rows)
atable["X"] = rng.random(rows) * 1000
atable["Y"] = rng.random(rows) - 0.5
atable["NAME"] = [f"obj{i % 5000}".encode() for i in range(rows)]
apath = os.path.join(TMP, "ascii.fits")
try:
    with fitsio.FITS(apath, "rw", clobber=True) as f:
        f.create_table_hdu(atable, table_type="ascii")
        f[1].write(atable)

    def ascii_read(column):
        def run():
            with fitsio.FITS(apath) as f:
                return f[1][column][:]
        return run

    def ascii_save():
        with scratch() as f:
            f.create_table_hdu(atable, table_type="ascii")
            f[1].write(atable)

    bench("ASCII table 200k: save (to a file)", ascii_save, budget=8.0)
    bench("ASCII table 200k: open + read F column", ascii_read("X"), budget=8.0)
    bench("ASCII table 200k: open + read E column", ascii_read("Y"), budget=8.0)
    bench("ASCII table 200k: open + read integer column", ascii_read("ID"), budget=8.0)
except Exception as e:  # noqa: BLE001
    print("ASCII tables failed:", e, flush=True)

with open(os.path.join(RESULTS_DIR, f"cfitsio-{os.environ.get('BENCH_PASS', '0')}.json"), "w") as out:
    json.dump({"cfitsio": fitsio.cfitsio_version(), "fitsio": fitsio.__version__, "ms": RESULTS, "bytes": SIZES}, out, indent=1)
print("done", flush=True)
