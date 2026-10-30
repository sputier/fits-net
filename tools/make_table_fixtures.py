"""Generates reference table FITS files with astropy for the FitsNet tests."""
import sys, numpy as np
from astropy.io import fits

out = sys.argv[1]

bits = np.zeros((3, 8), dtype=bool)
bits[0, [0, 7]] = True
bits[1, 3] = True
bits[2, :] = True

var_j = np.empty(3, dtype=object)
var_j[0] = np.array([1, 2, 3], dtype='>i4'); var_j[1] = np.array([4], dtype='>i4'); var_j[2] = np.array([], dtype='>i4')
var_d = np.empty(3, dtype=object)
var_d[0] = np.array([0.5], dtype='>f8'); var_d[1] = np.array([], dtype='>f8'); var_d[2] = np.array([1.5, 2.5], dtype='>f8')

cols = [
    fits.Column(name='ID', format='J', unit='count', array=np.array([1, 2, 3], dtype='>i4')),
    fits.Column(name='FLAG', format='L', array=np.array([True, False, True])),
    fits.Column(name='BITS', format='8X', array=bits),
    fits.Column(name='NAME', format='5A', array=np.array(['abc', 'de', 'fghij'])),
    fits.Column(name='FLUX', format='3E', array=np.arange(9, dtype='>f4').reshape(3, 3)),
    fits.Column(name='U16', format='I', bzero=32768, array=np.array([0, 1, 65535], dtype=np.uint16)),
    fits.Column(name='U64', format='K', bzero=9223372036854775808, array=np.array([0, 2**63, 2**64 - 1], dtype=np.uint64)),
    fits.Column(name='DBL', format='D', array=np.array([1.5, 2.5, 3.5], dtype='>f8')),
    fits.Column(name='CPLX', format='M', array=np.array([1 + 2j, 3 - 4j, 0j], dtype='>c16')),
    fits.Column(name='CPLXS', format='C', array=np.array([1 + 2j, 3 - 4j, 0j], dtype='>c8')),
    fits.Column(name='NUL', format='J', null=-99, array=np.array([1, -99, 3], dtype='>i4')),
    fits.Column(name='DIM', format='6J', dim='(3,2)', array=np.arange(18, dtype='>i4').reshape(3, 6)),
    fits.Column(name='VARJ', format='PJ()', array=var_j),
    fits.Column(name='VARQ', format='QD()', array=var_d),
    fits.Column(name='SHORTS', format='2I', array=np.array([[1, -2], [3, 4], [-5, 6]], dtype='>i2')),
    fits.Column(name='BYTES', format='2B', array=np.array([[1, 2], [3, 255], [0, 7]], dtype='u1')),
]
t = fits.BinTableHDU.from_columns(cols, name='EVENTS')
t.header['OBSERVER'] = 'Fits Net'
fits.HDUList([fits.PrimaryHDU(), t]).writeto(f"{out}/bintable.fits", overwrite=True)

a = fits.TableHDU.from_columns([
    fits.Column(name='NAME', format='A8', array=np.array(['alpha', 'beta', 'gamma'])),
    fits.Column(name='N', format='I5', array=np.array([1, -22, 333])),
    fits.Column(name='X', format='F10.3', array=np.array([1.5, -2.25, 1000.125])),
    fits.Column(name='Y', format='E15.7', array=np.array([1.5e10, -2.5e-5, 0.0])),
    fits.Column(name='Z', format='D25.17', array=np.array([3.141592653589793, 1e-300, -2.0])),
], name='ASCII')
fits.HDUList([fits.PrimaryHDU(), a]).writeto(f"{out}/asciitable.fits", overwrite=True)
print("ok")
