"""Reference vectors for the distortion tests: astropy (wcslib) pixel -> world and back, for SIP and TPV descriptions."""
import json
import math
import os
import warnings

import numpy as np
from astropy.io import fits
from astropy.wcs import WCS

warnings.simplefilter("ignore")

OUT = os.path.join(os.path.dirname(__file__), "..", "tests", "Eleris.FitsNet.Tests", "Fixtures", "distortion_vectors.json")
rng = np.random.default_rng(20261104)
cases = []


def base(ctype1, ctype2, crval=(150.0, 30.0), crpix=(1000.5, 750.25), scale=2.5e-4):
    return [
        ("WCSAXES", 2), ("CRPIX1", crpix[0]), ("CRPIX2", crpix[1]), ("CRVAL1", crval[0]), ("CRVAL2", crval[1]),
        ("CTYPE1", ctype1), ("CTYPE2", ctype2), ("CUNIT1", "deg"), ("CUNIT2", "deg"),
        ("CD1_1", -scale * 0.9998), ("CD1_2", scale * 0.02), ("CD2_1", scale * 0.019), ("CD2_2", scale * 1.0001),
    ]


def pixel_grid():
    xs = np.linspace(1, 2000, 9)
    ys = np.linspace(1, 1500, 7)
    grid = [[x, y] for x in xs for y in ys]
    grid += [[1000.5, 750.25], [1000.5001, 750.2499], [1.0, 1.0], [2000.0, 1500.0], [0.5, 0.5], [1234.567, 89.01]]
    return np.array(grid)


def add(name, cards):
    h = fits.Header()
    for k, v in cards:
        h[k] = v
    w = WCS(h)
    pix = pixel_grid()
    world = w.all_pix2world(pix, 1)
    back = w.all_world2pix(world, 1, tolerance=1e-10, maxiter=200, quiet=True)
    pts = []
    for p, q, b in zip(pix, world, back):
        if not all(math.isfinite(x) for x in q):
            continue
        pts.append({"pix": [float(x) for x in p], "world": [float(x) for x in q], "back": [float(x) for x in b] if all(math.isfinite(x) for x in b) else None})
    cases.append({"name": name, "cards": [[k, v] for k, v in cards], "points": pts})
    print(f"{name}: {len(pts)} points, max |back - pix| = {np.nanmax(np.abs(back - pix)):.2e}")


def sip_terms(prefix, order, size):
    """Random coefficients that bend the image by a few pixels at the corners."""
    cards = [(f"{prefix}_ORDER", order)]
    for p in range(order + 1):
        for q in range(order + 1 - p):
            if p + q < 2:
                continue
            # a term of degree d moves the edge of a 1000 pixel half-width by about `size` pixels
            amplitude = size / (1000.0 ** (p + q))
            cards.append((f"{prefix}_{p}_{q}", float(rng.normal() * amplitude)))
    return cards


def fit_inverse(cards, order):
    """AP/BP: polynomials fitted to the inverse of A/B on a grid, as the software that writes SIP files does."""
    h = fits.Header()
    for k, v in cards:
        h[k] = v
    w = WCS(h)
    u, v = np.meshgrid(np.linspace(-1000, 1000, 41), np.linspace(-750, 750, 31))
    u = u.ravel()
    v = v.ravel()
    du, dv = w.sip.a, w.sip.b
    f = sum(du[p, q] * u ** p * v ** q for p in range(du.shape[0]) for q in range(du.shape[1]))
    g = sum(dv[p, q] * u ** p * v ** q for p in range(dv.shape[0]) for q in range(dv.shape[1]))
    big_u, big_v = u + f, v + g   # distorted offsets
    terms = [(p, q) for p in range(order + 1) for q in range(order + 1 - p)]
    design = np.array([big_u ** p * big_v ** q for p, q in terms]).T
    out = []
    for name, target in (("AP", u - big_u), ("BP", v - big_v)):
        coefficients, *_ = np.linalg.lstsq(design, target, rcond=None)
        out.append((f"{name}_ORDER", order))
        out += [(f"{name}_{p}_{q}", float(c)) for (p, q), c in zip(terms, coefficients)]
    return out


# ---- SIP ---------------------------------------------------------------------------------------------------------------
for order, size, ctype, label in [(2, 1.5, "TAN", "order 2"), (3, 4.0, "TAN", "order 3"), (5, 8.0, "TAN", "order 5"), (4, 3.0, "SIN", "order 4 SIN")]:
    cards = base(f"RA---{ctype}-SIP", f"DEC--{ctype}-SIP") + sip_terms("A", order, size) + sip_terms("B", order, size)
    add(f"SIP {label} (no inverse polynomials)", cards)
    add(f"SIP {label} (with AP/BP)", cards + fit_inverse(cards, order + 1))

# the usual case of a plate solver: a small distortion, the reference point away from the centre, the dec close to the pole
cards = base("RA---TAN-SIP", "DEC--TAN-SIP", crval=(10.684, 85.0), crpix=(512.0, 512.0), scale=1e-3) + sip_terms("A", 3, 2.0) + sip_terms("B", 3, 2.0)
add("SIP near the pole", cards)


# ---- TPV ---------------------------------------------------------------------------------------------------------------
def tpv_terms(strength, last):
    """PV1_k and PV2_k: the identity plus small random terms up to the term `last` (39 is the seventh degree)."""
    cards = []
    for axis in (1, 2):
        for k in range(last + 1):
            if k == 1:
                value = 1.0 + float(rng.normal() * strength * 0.01)
            else:
                # the terms are in degrees of the tangent plane: x is about 0.25 degree at the edge
                degree = 0 if k == 0 else 1 if k in (1, 2, 3) else 2 if k < 7 else 3 if k < 12 else 4 if k < 17 else 5 if k < 24 else 6 if k < 31 else 7
                value = float(rng.normal() * strength / (0.25 ** degree)) * 1e-3 * 0.25
            cards.append((f"PV{axis}_{k}", value))
    return cards


def tpv_base(crval=(150.0, 30.0)):
    return [c for c in base("RA---TPV", "DEC--TPV", crval=crval)]


add("TPV linear only", tpv_base() + [("PV1_1", 1.0), ("PV2_1", 1.0)])
add("TPV quadratic and r", tpv_base() + [("PV1_0", 0.001), ("PV1_1", 1.0), ("PV2_1", 1.0), ("PV1_3", 5e-3), ("PV1_4", 1e-3), ("PV2_4", -2e-3), ("PV2_6", 3e-3)])
add("TPV third degree", tpv_base() + tpv_terms(0.02, 11))
add("TPV fifth degree", tpv_base((83.8, -5.4)) + tpv_terms(0.01, 22))
add("TPV seventh degree", tpv_base((210.0, 55.0)) + tpv_terms(0.005, 39))

with open(OUT, "w") as f:
    json.dump(cases, f, indent=1)
print("wrote", OUT)
