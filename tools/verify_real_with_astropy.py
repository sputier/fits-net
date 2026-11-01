"""Checks that astropy decodes the compressed versions of a real image (written by Eleris.FitsNet) to the original pixels."""
import sys
import numpy as np
from astropy.io import fits

directory, original = sys.argv[1], sys.argv[2]
expected = fits.getdata(original)
assert expected.dtype == np.uint16, expected.dtype

for name in ("rice", "gzip1", "gzip2", "hcompress", "nocompress"):
    with fits.open(f"{directory}/real_{name}.fits", checksum=True) as h:
        assert isinstance(h[1], fits.CompImageHDU), name
        data = h[1].data
        assert data.dtype == np.uint16, (name, data.dtype)
        assert np.array_equal(data, expected), f"{name}: pixels differ"
        assert h[1].header["BAYERPAT"] == "GBRG"
    # the checksums belong to the table that stores the tiles, not to the decompressed view
    with fits.open(f"{directory}/real_{name}.fits", disable_image_compression=True) as table:
        assert table[1].verify_checksum() == 1 and table[1].verify_datasum() == 1, f"{name}: checksum"
print("real ok")
