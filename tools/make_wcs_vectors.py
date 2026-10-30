"""Reference vectors for the WCS tests: astropy (wcslib) pixel -> world on grids, for every projection and several frames."""
import json
import math
import os
import warnings

import numpy as np
from astropy.io import fits
from astropy.wcs import WCS

warnings.simplefilter("ignore")

OUT = os.path.join(os.path.dirname(__file__), "..", "tests", "FitsNet.Tests", "Fixtures", "wcs_vectors.json")

cases = []


def add(name, cards, pixels):
    h = fits.Header()
    for k, v in cards:
        h[k] = v
    try:
        w = WCS(h)
        pix = np.array(pixels, dtype=float)
        world = w.wcs_pix2world(pix, 1)
    except Exception as e:  # wcslib refuses the description: not a case for the tests
        print("skipped", name, str(e).splitlines()[0][:80])
        return
    pts = []
    for p, q in zip(pix, world):
        entry = {"pix": [float(x) for x in p], "world": [None if not math.isfinite(x) else float(x) for x in q]}
        if all(math.isfinite(x) for x in q):
            try:
                back = w.wcs_world2pix(np.array([q]), 1)[0]
                entry["back"] = [None if not math.isfinite(x) else float(x) for x in back]
            except Exception:
                entry["back"] = None
        pts.append(entry)
    case = {"name": name, "cards": [[k, v] for k, v in cards], "points": pts}
    if w.wcs.lng >= 0 and w.naxis == 2:
        # world positions chosen independently of the pixel grid: the whole sphere, the antipode, the poles, the neighbourhood of CRVAL
        crval = w.wcs.crval
        lons = [crval[w.wcs.lng] + d for d in (0.0, 1e-7, 1e-5, 1e-3, 0.5, 5.0, 30.0, 90.0, -90.0, 179.9, 180.0, -5.0)]
        lats = [-90.0, -75.0, -30.0, -1e-3, 0.0, 1e-3, 30.0, 60.0, 89.9999, 90.0]
        worlds = [[lo, la] for lo in lons for la in lats]
        # the antipode of the reference position is where the native pole is undefined: nobody can agree on its image
        def antipode_distance(lo, la):
            d2r = math.pi / 180
            a = (lo - crval[w.wcs.lng]) * d2r
            l0 = -crval[w.wcs.lat] * d2r
            c = math.sin(l0) * math.sin(la * d2r) + math.cos(l0) * math.cos(la * d2r) * math.cos(a - math.pi)
            return math.degrees(math.acos(max(-1, min(1, c))))
        worlds = [p for p in worlds if antipode_distance(*p) > 1e-3]
        worlds += [[crval[w.wcs.lng] + dl, crval[w.wcs.lat] + db] for dl in (1e-7, 1e-5, 1e-3, -1e-3) for db in (0.0, 1e-7, 1e-5, 1e-3, -1e-3)]
        if w.wcs.lng == 1:
            worlds = [[b, a] for a, b in worlds]
        arr = np.array(worlds, dtype=float)
        try:
            back = w.wcs_world2pix(arr, 1)
            native = w.wcs.s2p(arr, 1)
            phi, theta = native["phi"], native["theta"]
            ok = np.isfinite(back).all(axis=1)
            # Positions where the answer is a matter of rounding are not tests: the poles and the cut meridian of the native sphere (the
            # native longitude is undefined or jumps by 360 degrees) and the edge of the projection (valid on one side, invalid on the other).
            keep = []
            for i, a_ in enumerate(arr):
                if abs(theta[i]) > 90 - 1e-9 or abs(abs(phi[i]) - 180) < 1e-9 or abs(a_[w.wcs.lat]) > 90 - 1e-9:
                    continue
                edge = False
                for dl, db in ((1e-6, 0), (-1e-6, 0), (0, 1e-6), (0, -1e-6)):
                    q = a_.copy()
                    q[w.wcs.lng] += dl
                    q[w.wcs.lat] += db
                    try:
                        r = w.wcs_world2pix(np.array([q]), 1)[0]
                    except Exception:
                        r = np.array([np.nan, np.nan])
                    if np.isfinite(r).all() != ok[i] or (ok[i] and np.abs(r).max() > 1e9):
                        edge = True
                        break
                # zenithal projections: the horizon (native latitude 0) is the edge of the hemisphere
                if abs(theta[i]) < 1e-9 and w.wcs.ctype[w.wcs.lng][5:8] in ("AZP", "SZP", "TAN", "STG", "SIN", "ARC", "ZPN", "ZEA", "AIR"):
                    edge = True
                if not edge:
                    keep.append(i)
            case["worlds"] = [{"world": [float(x) for x in arr[i]], "pix": [None if not math.isfinite(x) else float(x) for x in back[i]]} for i in keep]
        except Exception as e:
            print("no worlds for", name, e)
    cases.append(case)


def grid2(n=15, span=120.0, cx=100.0, cy=100.0):
    xs = np.linspace(-span, span, n)
    return [[cx + a, cy + b] for a in xs for b in xs]


def celestial(proj, lon_type="RA--", lat_type="DEC-", crval=(150.0, 30.0), cdelt=(-1.0, 1.0), pv=None, extra=None, span=120.0):
    pad = lambda t: (t + "----")[:4]
    code = proj
    cards = [
        ("WCSAXES", 2),
        ("CTYPE1", f"{pad(lon_type)}-{code}"),
        ("CTYPE2", f"{pad(lat_type)}-{code}"),
        ("CRPIX1", 100.0), ("CRPIX2", 100.0),
        ("CRVAL1", crval[0]), ("CRVAL2", crval[1]),
        ("CDELT1", cdelt[0]), ("CDELT2", cdelt[1]),
        ("CUNIT1", "deg"), ("CUNIT2", "deg"),
    ]
    for k, v in (pv or {}).items():
        cards.append((k, v))
    for k, v in (extra or []):
        cards.append((k, v))
    return cards, grid2(span=span)


# projection -> list of (label, pv, cdelt scale)
PROJ = {
    "AZP": [("", {}), ("mu2g20", {"PV2_1": 2.0, "PV2_2": 20.0}), ("mu0p5", {"PV2_1": 0.5})],
    "SZP": [("", {}), ("slant", {"PV2_1": 0.5, "PV2_2": 30.0, "PV2_3": 70.0})],
    "TAN": [("", {})],
    "STG": [("", {})],
    "SIN": [("", {}), ("slant", {"PV2_1": 0.3, "PV2_2": -0.2})],
    "ARC": [("", {})],
    "ZPN": [("", {"PV2_0": 0.0, "PV2_1": 1.0, "PV2_2": 0.05, "PV2_3": -0.01}), ("linear", {"PV2_1": 1.0})],
    "ZEA": [("", {})],
    "AIR": [("", {}), ("tb45", {"PV2_1": 45.0})],
    "CYP": [("", {}), ("params", {"PV2_1": 2.0, "PV2_2": 0.8})],
    "CEA": [("", {}), ("lambda", {"PV2_1": 0.7})],
    "CAR": [("", {})],
    "MER": [("", {})],
    "SFL": [("", {})],
    "PAR": [("", {})],
    "MOL": [("", {})],
    "AIT": [("", {})],
    "COP": [("", {"PV2_1": 40.0}), ("eta", {"PV2_1": 40.0, "PV2_2": 15.0}), ("south", {"PV2_1": -35.0, "PV2_2": 10.0})],
    "COE": [("", {"PV2_1": 40.0, "PV2_2": 10.0}), ("eta0", {"PV2_1": 40.0})],
    "COD": [("", {"PV2_1": 35.0, "PV2_2": 12.0}), ("eta0", {"PV2_1": 35.0})],
    "COO": [("", {"PV2_1": 45.0, "PV2_2": 20.0}), ("eta0", {"PV2_1": 45.0})],
    "BON": [("", {"PV2_1": 45.0}), ("south", {"PV2_1": -30.0})],
    "PCO": [("", {})],
    "TSC": [("", {})],
    "CSC": [("", {})],
    "QSC": [("", {})],
    "HPX": [("", {}), ("h6k3", {"PV2_1": 6.0, "PV2_2": 3.0}), ("h4k4", {"PV2_1": 4.0, "PV2_2": 4.0})],
}

CRVALS = [(150.0, 30.0), (10.0, 85.0), (0.0, 0.0), (210.0, -40.0)]

for proj, variants in PROJ.items():
    for label, pv in variants:
        for crval in CRVALS:
            cards, pix = celestial(proj, crval=crval, pv=pv, cdelt=(-1.0, 1.0))
            add(f"{proj}-{label}-crval{crval[0]:g}_{crval[1]:g}", cards, pix)

# rotations, CD matrices, CROTA, units, LONPOLE/LATPOLE, shifted fiducial points, galactic names, alternate descriptions
rho = math.radians(25.0)
for proj, pv in [("TAN", {}), ("SIN", {}), ("MOL", {}), ("CAR", {}), ("COE", {"PV2_1": 40.0, "PV2_2": 10.0}), ("HPX", {})]:
    cards, pix = celestial(proj, crval=(83.0, -5.0), pv=pv, cdelt=(-0.5, 0.5),
                           extra=[("PC1_1", math.cos(rho)), ("PC1_2", -math.sin(rho)), ("PC2_1", math.sin(rho)), ("PC2_2", math.cos(rho))])
    add(f"{proj}-PC", cards, pix)
    cards, pix = celestial(proj, crval=(83.0, -5.0), pv=pv, cdelt=(1.0, 1.0),
                           extra=[("CD1_1", -0.5 * math.cos(rho)), ("CD1_2", 0.5 * math.sin(rho)), ("CD2_1", 0.5 * math.sin(rho)), ("CD2_2", 0.5 * math.cos(rho))])
    cards = [c for c in cards if c[0] not in ("CDELT1", "CDELT2")]
    add(f"{proj}-CD", cards, pix)
    cards, pix = celestial(proj, crval=(83.0, -5.0), pv=pv, cdelt=(-0.5, 0.5), extra=[("CROTA2", 25.0)])
    add(f"{proj}-CROTA", cards, pix)

for proj, pv in [("CAR", {}), ("MER", {}), ("AIT", {}), ("TAN", {}), ("PCO", {}), ("BON", {"PV2_1": 40.0}), ("COP", {"PV2_1": 40.0})]:
    for lonpole, latpole in [(180.0, None), (60.0, None), (0.0, -50.0)]:
        extra = [("LONPOLE", lonpole)] + ([("LATPOLE", latpole)] if latpole is not None else [])
        cards, pix = celestial(proj, crval=(120.0, 20.0), pv=pv, extra=extra)
        add(f"{proj}-lonpole{lonpole:g}-latpole{latpole}", cards, pix)

for proj, pv in [("CAR", {}), ("SFL", {}), ("MOL", {}), ("COE", {"PV2_1": 40.0, "PV2_2": 10.0})]:
    cards, pix = celestial(proj, crval=(30.0, 10.0), pv=pv, extra=[("PV1_0", 1.0), ("PV1_1", 20.0), ("PV1_2", 15.0)])
    add(f"{proj}-fiducial", cards, pix)

cards, pix = celestial("TAN", lon_type="GLON", lat_type="GLAT", crval=(266.4, -28.9), cdelt=(-0.01, 0.01), span=100.0)
add("TAN-galactic", cards, pix)
cards, pix = celestial("SIN", lon_type="ELON", lat_type="ELAT", crval=(10.0, 20.0), cdelt=(-0.01, 0.01))
add("SIN-ecliptic", cards, pix)
cards, pix = celestial("ARC", lon_type="HPLN", lat_type="HPLT", crval=(0.0, 0.0), cdelt=(0.0006, 0.0006))
add("ARC-helioprojective", cards, pix)
cards, pix = celestial("TAN", lon_type="AZ--", lat_type="EL--", crval=(0.0, 0.0), cdelt=(0.0006, 0.0006))
add("TAN-unknown-names", cards, pix) if False else None

cards, pix = celestial("TAN", crval=(150.0, 30.0), cdelt=(-3.0, 3.0))
cards = [c for c in cards if c[0] not in ("CUNIT1", "CUNIT2")] + [("CUNIT1", "arcsec"), ("CUNIT2", "arcsec")]
cards = [c for c in cards if c[0] not in ("CDELT1", "CDELT2", "CRVAL1", "CRVAL2")] + [
    ("CDELT1", -3.0 * 3600), ("CDELT2", 3.0 * 3600), ("CRVAL1", 150.0 * 3600), ("CRVAL2", 30.0 * 3600)]
add("TAN-arcsec", cards, pix)

# an alternate description
cards = [("WCSAXES", 2), ("CTYPE1", "RA---TAN"), ("CTYPE2", "DEC--TAN"), ("CRPIX1", 50.0), ("CRPIX2", 50.0), ("CRVAL1", 10.0), ("CRVAL2", 10.0),
         ("CDELT1", -0.1), ("CDELT2", 0.1),
         ("WCSAXESA", 2), ("CTYPE1A", "GLON-CAR"), ("CTYPE2A", "GLAT-CAR"), ("CRPIX1A", 20.0), ("CRPIX2A", 20.0), ("CRVAL1A", 100.0),
         ("CRVAL2A", 5.0), ("CDELT1A", -0.2), ("CDELT2A", 0.2), ("WCSNAMEA", "galactic")]
h = fits.Header()
for k, v in cards:
    h[k] = v
wa = WCS(h, key="A")
pix = np.array(grid2(11, 60.0, 50.0, 50.0))
world = wa.wcs_pix2world(pix, 1)
cases.append({"name": "alternate-A", "alternate": "A", "cards": [[k, v] for k, v in cards],
              "points": [{"pix": [float(x) for x in p], "world": [float(x) for x in q]} for p, q in zip(pix, world)]})

# linear axes mixed with celestial ones (3 and 4 axes), with a coupling in the PC matrix
cards = [("WCSAXES", 3), ("CTYPE1", "RA---TAN"), ("CTYPE2", "DEC--TAN"), ("CTYPE3", "LINEAR"), ("CRPIX1", 10.0), ("CRPIX2", 10.0), ("CRPIX3", 3.0),
         ("CRVAL1", 45.0), ("CRVAL2", -10.0), ("CRVAL3", 100.0), ("CDELT1", -0.01), ("CDELT2", 0.01), ("CDELT3", 5.0),
         ("PC3_1", 0.1), ("PC3_3", 1.0), ("CUNIT3", "m")]
pts = [[x, y, z] for x in (1.0, 10.0, 25.5) for y in (1.0, 12.0, 40.0) for z in (1.0, 3.0, 7.5)]
add("linear-3d", cards, pts)

cards = [("WCSAXES", 2), ("CTYPE1", "X"), ("CTYPE2", "Y"), ("CRPIX1", 5.0), ("CRPIX2", 7.0), ("CRVAL1", -3.0), ("CRVAL2", 8.0), ("CDELT1", 0.5), ("CDELT2", 2.0),
         ("PC1_1", 0.8), ("PC1_2", 0.6), ("PC2_1", -0.6), ("PC2_2", 0.8)]
add("linear-2d-rotated", cards, [[x, y] for x in range(1, 12, 3) for y in range(1, 12, 3)])

# spectral axes (section 8.4): every type, every algorithm code
REST = [("RESTFRQ", 1.420405751e9)]
SPECTRAL = [
    ("FREQ", 1.4204e9, 1e5, REST), ("FREQ-LOG", 1.4204e9, 1e5, REST), ("ENER", 9.4e-25, 1e-29, REST), ("WAVN", 4.74, 1e-3, REST),
    ("VRAD", 1e5, 1e3, REST), ("VRAD-W2F", 1e5, 1e3, REST), ("VRAD-V2F", -2e5, 1e3, REST), ("WAVE", 0.21106, 1e-5, REST), ("WAVE-F2W", 0.21106, 1e-5, REST),
    ("WAVE-LOG", 0.21106, 1e-5, REST), ("WAVE-A2W", 0.21106, 1e-5, REST), ("WAVE-V2W", 0.21106, 1e-5, REST), ("VOPT", 5e4, 1e3, REST),
    ("VOPT-F2W", 5e4, 1e3, REST), ("VOPT-A2W", 5e4, 1e3, REST), ("ZOPT", 0.1, 1e-3, REST), ("ZOPT-F2W", 0.1, 1e-3, REST),
    ("AWAV", 5.5e-7, 1e-9, REST), ("AWAV-F2A", 5.5e-7, 1e-9, REST), ("AWAV-W2A", 5.5e-7, 1e-9, REST), ("AWAV-V2A", 5.52e-7, 1e-9, [("RESTWAV", 5.5e-7)]),
    ("VELO", 3e7, 1e4, REST), ("VELO-F2V", 3e7, 1e4, REST), ("VELO-W2V", 3e7, 1e4, REST), ("VELO-A2V", 3e6, 1e4, [("RESTWAV", 5.5e-7)]), ("BETA", 0.1, 1e-4, REST),
    ("BETA-F2V", 0.1, 1e-4, REST), ("FREQ-W2F", 1.4204e9, 1e5, REST), ("FREQ-A2F", 1.4204e9, 1e5, REST), ("FREQ-V2F", 1.4e9, 1e5, REST),
    ("WAVE", 0.21106, 1e-5, [("RESTWAV", 0.21106)]), ("VRAD", 1e5, 1e3, [("RESTWAV", 0.2112)]),
]
for ctype, crval, cdelt, extra in SPECTRAL:
    cards = [("WCSAXES", 1), ("CTYPE1", ctype), ("CRPIX1", 10.0), ("CRVAL1", crval), ("CDELT1", cdelt)] + list(extra)
    add("spec-" + ctype + ("-restwav" if extra and extra[0][0] == "RESTWAV" else ""), cards, [[float(i)] for i in range(1, 31)])

# unit conversions: astropy answers in SI units, the references here are scaled back to the unit of the header
for ctype, unit, factor, crval, cdelt, extra in [("FREQ", "GHz", 1e9, 1.4204, 1e-4, []), ("WAVE", "um", 1e-6, 21106.0, 10.0, []),
                                                   ("VRAD", "km/s", 1e3, 100.0, 1.0, REST), ("WAVE-F2W", "nm", 1e-9, 500.0, 0.1, []),
                                                   ("AWAV", "Angstrom", 1e-10, 5500.0, 1.0, [])]:
    cards = [("WCSAXES", 1), ("CTYPE1", ctype), ("CRPIX1", 10.0), ("CRVAL1", crval), ("CDELT1", cdelt), ("CUNIT1", unit)] + list(extra)
    h = fits.Header()
    for k, v in cards:
        h[k] = v
    w = WCS(h)
    pix = np.array([[float(i)] for i in range(1, 21)])
    world = w.wcs_pix2world(pix, 1)
    cases.append({"name": f"spec-unit-{ctype}-{unit}", "cards": [[k, v] for k, v in cards],
                  "points": [{"pix": [float(p[0])], "world": [float(q[0]) / factor]} for p, q in zip(pix, world)]})

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(cases, f)
print(len(cases), "cases", sum(len(c["points"]) for c in cases), "points")
nan = sum(1 for c in cases for p in c["points"] if any(v is None for v in p["world"]))
print(nan, "points without image")
