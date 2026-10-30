"""Generates tile-compressed images with astropy (reference implementation) for the FitsNet tests.

Besides the compressed files it writes the expected decompressed pixels as .npy-like raw big-endian dumps
(<name>.expected) so the C# tests do not need numpy.
"""
import sys
import numpy as np
from astropy.io import fits

out = sys.argv[1]
rng = np.random.default_rng(42)

ny, nx = 23, 37  # deliberately not multiples of the tile size


def smooth(dtype, scale):
    y, x = np.mgrid[0:ny, 0:nx]
    base = 100 * np.sin(x / 5.0) + 60 * np.cos(y / 4.0) + rng.normal(0, 2, (ny, nx))
    return (base * scale).astype(dtype)


images = {
    "i16": smooth('>i2', 10),
    "i32": smooth('>i4', 1000),
    "u8": (np.abs(smooth('>i2', 1)) % 256).astype('u1'),
    "f32": smooth('>f4', 1) + np.float32(0.3),
    "f64": smooth('>f8', 1) + 0.3,
}
# a mask-like image for PLIO (small non-negative integers, long runs)
mask = np.zeros((ny, nx), dtype='>i4')
mask[3:9, 5:20] = 1
mask[10:20, 0:10] = 7
mask[15:22, 25:36] = 300
images["mask"] = mask

specs = [
    ("rice", "RICE_1", {}),
    ("gzip1", "GZIP_1", {}),
    ("gzip2", "GZIP_2", {}),
    ("plio", "PLIO_1", {}),
    ("hcomp", "HCOMPRESS_1", {}),
    ("none", "NOCOMPRESS", {}),
]

manifest = []
for iname, data in images.items():
    for sname, ctype, extra in specs:
        if ctype == "PLIO_1" and iname not in ("mask",):
            continue
        if ctype == "HCOMPRESS_1" and iname in ("u8", "f64"):
            pass
        if iname == "mask" and ctype not in ("PLIO_1", "RICE_1", "GZIP_1"):
            continue
        kwargs = dict(compression_type=ctype, name="IMG", tile_shape=(8, 16))
        if ctype == "HCOMPRESS_1":
            kwargs["tile_shape"] = (16, 16)
            kwargs["hcomp_scale"] = 0  # lossless for integers
        if data.dtype.kind == 'f':
            kwargs["quantize_level"] = 16.0
            kwargs["quantize_method"] = 1  # SUBTRACTIVE_DITHER_1
            kwargs["dither_seed"] = 7
            if ctype == "HCOMPRESS_1":
                kwargs["hcomp_scale"] = 0
        try:
            hdu = fits.CompImageHDU(data, **kwargs)
        except Exception as e:  # unsupported combination for this dtype
            print("skip", iname, sname, e)
            continue
        fname = f"comp_{iname}_{sname}.fits"
        try:
            fits.HDUList([fits.PrimaryHDU(), hdu]).writeto(f"{out}/{fname}", overwrite=True)
        except Exception as e:
            print("skip write", fname, e)
            continue
        # what astropy decodes (the oracle)
        with fits.open(f"{out}/{fname}") as h:
            dec = np.asarray(h[1].data)
        dec.astype(dec.dtype.newbyteorder('>')).tofile(f"{out}/{fname}.expected")
        manifest.append(f"{fname} {dec.dtype.str} {ny} {nx}")

# float images with the other quantization options
for qname, qmethod in (("nodither", -1), ("dither2", 2)):
    try:
        hdu = fits.CompImageHDU(images["f32"], compression_type="RICE_1", name="IMG", tile_shape=(8, 16),
                                quantize_level=16.0, quantize_method=qmethod, dither_seed=11)
        fname = f"comp_f32_rice_{qname}.fits"
        fits.HDUList([fits.PrimaryHDU(), hdu]).writeto(f"{out}/{fname}", overwrite=True)
        with fits.open(f"{out}/{fname}") as h:
            dec = np.asarray(h[1].data)
        dec.astype(dec.dtype.newbyteorder('>')).tofile(f"{out}/{fname}.expected")
        manifest.append(f"{fname} {dec.dtype.str} {ny} {nx}")
    except Exception as e:
        print("skip", qname, e)

# lossless float via GZIP with no quantization
try:
    hdu = fits.CompImageHDU(images["f32"], compression_type="GZIP_1", name="IMG", tile_shape=(8, 16), quantize_level=0)
    fits.HDUList([fits.PrimaryHDU(), hdu]).writeto(f"{out}/comp_f32_gzip_lossless.fits", overwrite=True)
    with fits.open(f"{out}/comp_f32_gzip_lossless.fits") as h:
        dec = np.asarray(h[1].data)
    dec.astype(dec.dtype.newbyteorder('>')).tofile(f"{out}/comp_f32_gzip_lossless.fits.expected")
    manifest.append(f"comp_f32_gzip_lossless.fits {dec.dtype.str} {ny} {nx}")
except Exception as e:
    print("skip lossless", e)

# the original pixel data so tests can compare lossless codecs exactly
for iname, data in images.items():
    data.astype(data.dtype.newbyteorder('>')).tofile(f"{out}/orig_{iname}.raw")
open(f"{out}/compressed_manifest.txt", "w").write("\n".join(manifest) + "\n")
print("ok", len(manifest))
