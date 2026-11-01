"""Reads files written by Eleris.FitsNet with astropy and checks them (conformance + values)."""
import sys, numpy as np
from astropy.io import fits

d = sys.argv[1]

with fits.open(f"{d}/written.fits") as h:
    h.verify('exception')
    assert len(h) == 5, len(h)
    assert h[0].header['EXTEND'] is True
    assert h[0].header['OBSERVER'] == "O'Brien"
    assert np.array_equal(h[0].data, np.array([[1, 2, 3], [4, 5, 6]], dtype=np.float32))
    assert np.array_equal(h[1].data, np.array([-1, 0, 32767], dtype=np.int16))
    assert np.array_equal(h[2].data, np.array([0, 1, 65535], dtype=np.uint16)) and h[2].data.dtype == np.uint16
    assert np.array_equal(h[3].data, np.array([0, 2**63, 2**64 - 1], dtype=np.uint64)) and h[3].data.dtype == np.uint64
    assert h[4].header['XTENSION'] == 'IMAGE' and h[4].data.shape == (2, 2, 2)
    assert np.array_equal(h[4].data.ravel(), np.arange(8, dtype=np.float64))
    assert h[1].header['EXPTIME'] == 12.5
    assert h[1].header['LONGKEY'] == 'x' * 150, "long string (CONTINUE) mismatch"

with fits.open(f"{d}/groups.fits") as h:
    h.verify('exception')
    g = h[0].data
    assert g.parnames == ['U', 'V'], g.parnames
    assert np.array_equal(g.par('U'), [1, 2]) and np.array_equal(g.par('V'), [10, 20])
    assert np.array_equal(g.data.ravel(), [0, 1, 2, 3, 4, 5, 6, 7])
with fits.open(f"{d}/tables.fits") as h:
    h.verify('exception')
    assert len(h) == 3, len(h)
    t = h[1]
    assert t.header['EXTNAME'] == 'EVT'
    dat = t.data
    assert list(dat['ID']) == [1, -2, 3]
    assert list(dat['FLAG']) == [True, False, False], list(dat['FLAG'])
    assert list(dat['NAME']) == ['abc', 'de', '']
    assert np.array_equal(dat['U16'], np.array([0, 1, 65535], dtype=np.uint16)) and dat['U16'].dtype == np.uint16
    assert np.array_equal(dat['U64'], np.array([0, 2**63, 2**64 - 1], dtype=np.uint64))
    assert np.array_equal(dat['XY'], np.array([[1, 2], [3, 4], [5, 6]], dtype=np.float32))
    assert np.allclose(dat['CPLX'], [1 + 2j, 3 - 4j, 0j])
    assert dat['GRID'].shape == (3, 2, 3), dat['GRID'].shape   # TDIM=(3,2) -> numpy (2,3) per row
    assert [list(r) for r in dat['VARJ']] == [[1, 2, 3], [4], []]
    assert [list(r) for r in dat['VARQ']] == [[0.5], [], [1.5, 2.5]]
    assert [bool(x) for x in dat['BITS'][0]] == [True] + [False] * 6 + [True]
    assert t.columns['ID'].unit == 'count'
    a = h[2].data
    assert list(a['NAME']) == ['alpha', 'beta', 'gamma']
    assert list(a['N']) == [1, -22, 333]
    assert np.allclose(a['X'], [1.5, -2.25, 1000.125])
    assert np.allclose(a['Y'], [1.5e10, -2.5e-5, 0.0])
import warnings
with warnings.catch_warnings():
    warnings.simplefilter('error')
    with fits.open(f"{d}/checksums.fits", checksum=True) as h:
        assert len(h) == 4, len(h)
        for i, x in enumerate(h):
            assert x.verify_checksum() == 1, f"CHECKSUM of HDU {i}"
            assert x.verify_datasum() == 1, f"DATASUM of HDU {i}"
        assert h[2].data['A'].tolist() == [1, 2, 3]
import math
n = 37 * 23
ramp = np.array([((i * 31) % 4000 - 2000 + (i % 7)) for i in range(n)], dtype=np.int16).reshape(23, 37)
flt = np.array([1000 + 50 * math.sin(i / 40.0) + ((i * 7919) % 1000) / 250.0 for i in range(n)], dtype=np.float32).reshape(23, 37)
for name in ("rice", "gzip1", "gzip2", "none"):
    with fits.open(f"{d}/c_{name}_i16.fits") as h:
        assert isinstance(h[1], fits.CompImageHDU), name
        assert np.array_equal(h[1].data, ramp), name
with fits.open(f"{d}/c_gzip2_f32_lossless.fits") as h:
    assert np.array_equal(h[1].data, flt)
for name in ("nodither", "dither1", "dither2"):
    with fits.open(f"{d}/c_{name}_f32.fits") as h:
        got = h[1].data
        ours = np.fromfile(f"{d}/c_{name}_f32.raw", dtype='>f4').reshape(23, 37)
        assert np.array_equal(got, ours), f"{name}: astropy and Eleris.FitsNet decode differently (max diff {np.abs(got - ours).max()})"
        assert np.abs(got - flt).max() < 1.0, name
mask = np.zeros((23, 37), dtype=np.int32)
for i in range(37 * 23):
    mask.flat[i] = 0 if i % 37 < 10 else (7 if (i // 37) % 3 == 0 else 300)
with fits.open(f"{d}/c_plio.fits") as h:
    assert fits.getheader(f"{d}/c_plio.fits", 1, disable_image_compression=True)['ZCMPTYPE'] == 'PLIO_1'
    assert np.array_equal(h[1].data, mask)
i32 = np.array([(i * 977) % 100000 - 50000 + i for i in range(n)], dtype=np.int32).reshape(23, 37)
with fits.open(f"{d}/c_hcomp_i32.fits") as h:
    assert np.array_equal(h[1].data, i32), "lossless HCOMPRESS"
for name in ("hcomp_lossy", "hcomp_f32"):
    with fits.open(f"{d}/c_{name}.fits") as h:
        ours = np.fromfile(f"{d}/c_{name}.raw", dtype='>i4' if name == "hcomp_lossy" else '>f4').reshape(23, 37)
        assert np.array_equal(h[1].data, ours), f"{name}: astropy and Eleris.FitsNet decode differently"
with fits.open(f"{d}/c_u16.fits") as h:
    assert np.array_equal(h[1].data, np.array([[0, 1], [65535, 40000]], dtype=np.uint16))
print("astropy ok")
