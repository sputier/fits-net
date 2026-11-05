"""Same operations as the Eleris.FitsNet benchmarks, with astropy, on the same files. Medians in milliseconds, written to results/astropy-<BENCH_PASS>.json.json.
Run with: python benchmarks/comparison/bench_astropy.py"""
import json
import io
import os
import statistics
import sys
import time
import warnings

import numpy as np
from astropy.io import fits
from astropy.io.fits import CompImageHDU, PrimaryHDU, BinTableHDU, TableHDU, Column

warnings.simplefilter("ignore")
HERE = os.path.dirname(os.path.abspath(__file__))
RESULTS_DIR = os.path.join(HERE, "results")
os.makedirs(RESULTS_DIR, exist_ok=True)
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", "data", "vespera"))
BROAD = os.path.join(ROOT, "img-0022-M42-broadband.fits")
DUAL = os.path.join(ROOT, "img-1200-M42-dualband.fits")
RESULTS = {}


def bench(name, fn, iterations=None, warmup=3, budget=6.0):
    for _ in range(warmup):
        fn()
    samples = []
    start = time.perf_counter()
    while len(samples) < (iterations or 200):
        t0 = time.perf_counter()
        fn()
        samples.append((time.perf_counter() - t0) * 1000)
        if iterations is None and time.perf_counter() - start > budget and len(samples) >= 5:
            break
    RESULTS[name] = statistics.median(samples)
    print(f"{name:<58} {RESULTS[name]:10.3f} ms  (n={len(samples)})", flush=True)


print(sys.version.split()[0], "astropy", __import__("astropy").__version__, "numpy", np.__version__, flush=True)

# ---- plain image I/O -------------------------------------------------------------------------------------------------
with fits.open(BROAD) as f:
    hdr = f[0].header
    print("BITPIX", hdr["BITPIX"], "BZERO", hdr.get("BZERO"), "shape", f[0].data.shape, f[0].data.dtype)
    u16 = np.array(f[0].data)          # ushort pixels
f32 = u16.astype(np.float32)


def open_header():
    with fits.open(BROAD) as f:
        _ = f[0].header["NAXIS1"]


def read_u16():
    with fits.open(BROAD, memmap=False) as f:
        return np.array(f[0].data)


def read_u16_memmap_copy():
    with fits.open(BROAD, memmap=True) as f:
        return np.array(f[0].data)


def read_float32():
    with fits.open(BROAD, memmap=False) as f:
        return f[0].data.astype(np.float32)


def read_float64():
    with fits.open(BROAD, memmap=False) as f:
        return f[0].data.astype(np.float64)


def cutout_256():
    with fits.open(BROAD) as f:
        return f[0].section[1500:1756, 1500:1756]


def narrow_column():
    with fits.open(BROAD) as f:
        return f[0].section[:, 1000:1016]


def whole_rows():
    with fits.open(BROAD) as f:
        return f[0].section[2000:2064, :]


bench("Open (header only)", open_header)
bench("Open + read ushort (memmap=False)", read_u16)
bench("Open + read float32", read_float32)
bench("Open + read float64", read_float64)
bench("Cutout 256x256", cutout_256)
bench("Cutout 16 x 3536 (narrow column)", narrow_column)
bench("Cutout 3536 x 64 (64 whole rows)", whole_rows)

# ---- writing ---------------------------------------------------------------------------------------------------------
hdu_u16 = PrimaryHDU(u16)


def save_memory():
    hdu_u16.writeto(io.BytesIO(), overwrite=True)


def save_checksum():
    h = PrimaryHDU(u16)
    h.writeto(io.BytesIO(), overwrite=True, checksum=True)


bench("Save to memory (ushort, no checksum)", save_memory)
bench("Save to memory, CHECKSUM + DATASUM", save_checksum)

buf = io.BytesIO()
PrimaryHDU(u16).writeto(buf, checksum=True)
checksummed = buf.getvalue()


def verify_checksums():
    with fits.open(io.BytesIO(checksummed), checksum=True) as f:
        _ = f[0].data
        return f[0].verify_checksum(), f[0].verify_datasum()


bench("Open from memory + verify CHECKSUM/DATASUM (reads data)", verify_checksums)

# ---- tile compression: integers ------------------------------------------------------------------------------------
# the benchmarks compress one row per tile for Rice and GZIP, 16 rows for HCOMPRESS (astropy's own defaults)
for label, algo, kwargs in [
    ("Rice", "RICE_1", {}),
    ("Gzip1", "GZIP_1", {}),
    ("Gzip2", "GZIP_2", {}),
    ("HCompress", "HCOMPRESS_1", {"tile_shape": (16, 3536)}),
]:
    def compress(algo=algo, kwargs=kwargs):
        h = CompImageHDU(u16, compression_type=algo, **kwargs)
        out = io.BytesIO()
        fits.HDUList([PrimaryHDU(), h]).writeto(out)
        return out.getvalue()

    try:
        data = compress()
    except Exception as e:  # noqa: BLE001
        print(f"{label}: compress failed: {e}")
        continue

    def decompress(data=data):
        with fits.open(io.BytesIO(data)) as f:
            return np.array(f[1].data)

    bench(f"Compress {label} (ushort, to memory)", compress, iterations=None, budget=8.0)
    bench(f"Decompress {label} to ushort", decompress, iterations=None, budget=8.0)
    print(f"    ({label}: {len(data) / 1e6:.2f} MB)")

# ---- tile compression: float32 with quantization ---------------------------------------------------------------------
with fits.open(DUAL) as f:
    dual = np.array(f[0].data).astype(np.float32)


def compress_float():
    h = CompImageHDU(dual, compression_type="RICE_1", quantize_method=2, quantize_level=4.0, dither_seed=1, tile_shape=(16, 3536))
    out = io.BytesIO()
    fits.HDUList([PrimaryHDU(), h]).writeto(out)
    return out.getvalue()


cf = compress_float()


def decompress_float():
    with fits.open(io.BytesIO(cf)) as f:
        return np.array(f[1].data)


bench("Compress float32 (Rice, SUBTRACTIVE_DITHER_1)", compress_float, iterations=None, budget=8.0)
bench("Decompress float32 (Rice, SUBTRACTIVE_DITHER_1)", decompress_float, iterations=None, budget=8.0)

# ---- tables ----------------------------------------------------------------------------------------------------------
rows = 200_000
rng = np.random.default_rng(7)
cols = [
    Column(name="ID", format="J", array=np.arange(rows, dtype=">i4")),
    Column(name="RA", format="D", array=rng.random(rows) * 360),
    Column(name="FLUX", format="3E", array=rng.random((rows, 3)).astype(np.float32)),
    Column(name="FLAGS", format="I", array=(np.arange(rows) % 1000).astype(np.int16)),
    Column(name="NAME", format="12A", array=np.array([f"src{i % 100000}" for i in range(rows)])),
    Column(name="OK", format="L", array=(np.arange(rows) % 3 != 0)),
]
table = BinTableHDU.from_columns(cols)
buf = io.BytesIO()
fits.HDUList([PrimaryHDU(), table]).writeto(buf)
table_bytes = buf.getvalue()


def table_create():
    return BinTableHDU.from_columns(cols)


def table_read_id():
    with fits.open(io.BytesIO(table_bytes)) as f:
        return np.array(f[1].data["ID"])


def table_read_ra():
    with fits.open(io.BytesIO(table_bytes)) as f:
        return np.array(f[1].data["RA"])


def table_read_names():
    with fits.open(io.BytesIO(table_bytes)) as f:
        return f[1].data["NAME"].tolist()


def table_save():
    fits.HDUList([PrimaryHDU(), table]).writeto(io.BytesIO())


bench("Binary table 200k: create from columns", table_create)
bench("Binary table 200k: open + read int column", table_read_id)
bench("Binary table 200k: open + read double column", table_read_ra)
bench("Binary table 200k: open + read string column (list)", table_read_names)
bench("Binary table 200k: save to memory", table_save)

# ASCII table: ID (I), X (F12.5), Y (E16.8), NAME
ascii_cols = [
    Column(name="ID", format="I10", array=np.arange(rows)),
    Column(name="X", format="F12.5", array=rng.random(rows) * 1000),
    Column(name="Y", format="E16.8", array=rng.random(rows) - 0.5),
    Column(name="NAME", format="A12", array=np.array([f"obj{i % 5000}" for i in range(rows)])),
]
atable = TableHDU.from_columns(ascii_cols)
buf = io.BytesIO()
fits.HDUList([PrimaryHDU(), atable]).writeto(buf)
abytes = buf.getvalue()


def ascii_create():
    return TableHDU.from_columns(ascii_cols)


def ascii_read_x():
    with fits.open(io.BytesIO(abytes)) as f:
        return np.array(f[1].data["X"])


def ascii_read_y():
    with fits.open(io.BytesIO(abytes)) as f:
        return np.array(f[1].data["Y"])


def ascii_read_id():
    with fits.open(io.BytesIO(abytes)) as f:
        return np.array(f[1].data["ID"])


def ascii_save():
    fits.HDUList([PrimaryHDU(), atable]).writeto(io.BytesIO())


bench("ASCII table 200k: create", ascii_create, iterations=None, budget=8.0)
bench("ASCII table 200k: open + read F column", ascii_read_x, iterations=None, budget=8.0)
bench("ASCII table 200k: open + read E column", ascii_read_y, iterations=None, budget=8.0)
bench("ASCII table 200k: open + read integer column", ascii_read_id, iterations=None, budget=8.0)
bench("ASCII table 200k: save to memory", ascii_save, iterations=None, budget=8.0)

with open(os.path.join(RESULTS_DIR, f"astropy-{os.environ.get('BENCH_PASS', '0')}.json"), "w") as out:
    json.dump({"astropy": __import__("astropy").__version__, "ms": RESULTS}, out, indent=1)
print("done", flush=True)
