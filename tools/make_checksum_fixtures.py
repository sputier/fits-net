"""Generates FITS files with CHECKSUM/DATASUM (astropy) and reference encoding vectors for the Eleris.FitsNet tests."""
import sys, numpy as np
from astropy.io import fits

out = sys.argv[1]

# image primary + float image extension + binary table + ascii table, all with checksums
p = fits.PrimaryHDU(np.arange(20, dtype='>i2').reshape(4, 5))
p.header['OBJECT'] = 'checksum test'
e = fits.ImageHDU(np.linspace(0, 1, 9, dtype='>f4').reshape(3, 3), name='SCI')
b = fits.BinTableHDU.from_columns([
    fits.Column(name='A', format='J', array=np.array([1, 2, 3], dtype='>i4')),
    fits.Column(name='S', format='4A', array=np.array(['ab', 'cd', 'efgh'])),
], name='TAB')
a = fits.TableHDU.from_columns([
    fits.Column(name='N', format='I4', array=np.array([1, 22, 333])),
    fits.Column(name='T', format='A5', array=np.array(['x', 'yy', 'zzz'])),
], name='ASC')
fits.HDUList([p, e, b, a]).writeto(f"{out}/checksum.fits", overwrite=True, checksum=True)

# a file whose data was altered after the checksum was written (DATASUM and CHECKSUM must be reported invalid)
data = bytearray(open(f"{out}/checksum.fits", "rb").read())
data[2880 + 10] ^= 0xFF  # flip a bit pattern inside the primary data
open(f"{out}/checksum_corrupt.fits", "wb").write(bytes(data))

# encoding vectors from astropy's reference implementation
hdu = fits.PrimaryHDU()
values = [0, 1, 0xFFFFFFFF, 0x12345678, 0xDEADBEEF, 0x3A3B3C3D, 0x5B5C5D5E, 0x40404040, 0xFFFF0000, 0x0000FFFF, 123456789]
with open(f"{out}/checksum_vectors.txt", "w") as f:
    for v in values:
        f.write(f"{v} {hdu._char_encode(v)}\n")
print("ok")
