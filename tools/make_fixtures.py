"""Generates reference FITS files with astropy (the reference implementation) for the FitsNet tests."""
import sys, numpy as np
from astropy.io import fits

out = sys.argv[1]

# 1. int16 image 3x2 (NAXIS1=3, NAXIS2=2), values chosen so row/column order is checkable
a = np.arange(6, dtype='>i2').reshape(2, 3) - 3
fits.PrimaryHDU(a).writeto(f"{out}/int16.fits", overwrite=True)

# 2. unsigned 16-bit via BZERO=32768 (astropy writes uint16 that way)
u = np.array([[0, 1, 32768], [65535, 40000, 7]], dtype=np.uint16)
fits.PrimaryHDU(u).writeto(f"{out}/uint16.fits", overwrite=True)

# 3. float32 with BSCALE/BZERO/BLANK stored as int16
raw = np.array([[1, 2, -32768], [4, 5, 6]], dtype='>i2')
h = fits.PrimaryHDU(raw)
h.header['BSCALE'] = 0.5
h.header['BZERO'] = 10.0
h.header['BLANK'] = -32768
h.writeto(f"{out}/scaled.fits", overwrite=True, output_verify='ignore')

# 4. multi-extension: primary empty + two image extensions (float64, int32) with names
p = fits.PrimaryHDU()
p.header['OBSERVER'] = 'Fits Net'
e1 = fits.ImageHDU(np.linspace(0, 1, 12, dtype='>f8').reshape(3, 4), name='SCI')
e2 = fits.ImageHDU(np.arange(8, dtype='>i4').reshape(2, 2, 2), name='CUBE')
e1.header['EXPTIME'] = (30.5, 'seconds')
fits.HDUList([p, e1, e2]).writeto(f"{out}/multi.fits", overwrite=True)

# 5. uint32, int64, uint64, int8 conventions
fits.PrimaryHDU(np.array([0, 1, 4294967295, 2147483648], dtype=np.uint32)).writeto(f"{out}/uint32.fits", overwrite=True)
fits.PrimaryHDU(np.array([-5, 0, 2**40], dtype='>i8')).writeto(f"{out}/int64.fits", overwrite=True)
fits.PrimaryHDU(np.array([0, 1, 2**63, 2**64 - 1], dtype=np.uint64)).writeto(f"{out}/uint64.fits", overwrite=True)
fits.PrimaryHDU(np.array([-128, -1, 0, 127], dtype=np.int8)).writeto(f"{out}/int8.fits", overwrite=True)

# 6. random groups: 3 groups, 2 params, data 2x2 float32
from astropy.io.fits import GroupData, GroupsHDU
data = np.arange(3 * 4, dtype='>f4').reshape(3, 1, 1, 2, 2)  # (groups, ..., naxis2.., ) layout used by astropy
pars = [np.array([1.0, 2.0, 3.0], dtype='>f4'), np.array([10.0, 20.0, 30.0], dtype='>f4')]
g = GroupData(data, parnames=['U', 'V'], pardata=pars, bitpix=-32)
gh = GroupsHDU(g)
gh.header['PTYPE1'] = 'U'
fits.HDUList([gh]).writeto(f"{out}/groups.fits", overwrite=True)
print("ok")
