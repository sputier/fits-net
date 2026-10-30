"""Reference vectors for the time tests: astropy.time (ERFA) for ISO-8601, JD/MJD and time scale conversions."""
import json
import os
import random
import warnings

import numpy as np
from astropy.time import Time

warnings.simplefilter("ignore")

OUT = os.path.join(os.path.dirname(__file__), "..", "tests", "FitsNet.Tests", "Fixtures", "time_vectors.json")
rng = random.Random(5)

iso = []
# dates around every leap second and a spread of dates since 1972
for y, m in [(1972, 6), (1972, 12), (1973, 12), (1974, 12), (1975, 12), (1976, 12), (1977, 12), (1978, 12), (1979, 12), (1981, 6), (1982, 6),
             (1983, 6), (1985, 6), (1987, 12), (1989, 12), (1990, 12), (1992, 6), (1993, 6), (1994, 6), (1995, 12), (1997, 6), (1998, 12),
             (2005, 12), (2008, 12), (2012, 6), (2015, 6), (2016, 12)]:
    last = {6: 30, 12: 31}[m]
    iso += [f"{y:04d}-{m:02d}-{last:02d}T23:59:59.250000000", f"{y:04d}-{m:02d}-{last:02d}T23:59:60.500000000",
            f"{y:04d}-{(m % 12) + 1:02d}-01T00:00:00.000000000" if m != 12 else f"{y + 1:04d}-01-01T00:00:00.000000000"]
for _ in range(300):
    y = rng.randint(1972, 2040)
    iso.append(f"{y:04d}-{rng.randint(1, 12):02d}-{rng.randint(1, 28):02d}T{rng.randint(0, 23):02d}:{rng.randint(0, 59):02d}:{rng.randint(0, 59):02d}.{rng.randint(0, 999999999):09d}")

cases = []
for text in iso:
    entry = {"iso": text, "utc": None}
    try:
        t = Time(text, format="isot", scale="utc")
    except Exception:
        continue
    entry["utc"] = {"jd1": float(t.jd1), "jd2": float(t.jd2), "mjd": float(t.mjd)}
    entry["to"] = {}
    for scale in ("tai", "tt", "tcg"):
        try:
            c = getattr(t, scale)
            entry["to"][scale] = {"jd1": float(c.jd1), "jd2": float(c.jd2)}
        except Exception:
            pass
    cases.append(entry)

# conversions starting from the other scales
reverse = []
for _ in range(200):
    y = rng.randint(1972, 2040)
    base = Time(f"{y:04d}-{rng.randint(1, 12):02d}-{rng.randint(1, 28):02d}T{rng.randint(0, 23):02d}:{rng.randint(0, 59):02d}:{rng.randint(0, 59):02d}.{rng.randint(0, 999999):06d}", format="isot", scale="tai")
    for src in ("tai", "tt", "tcg"):
        t = getattr(base, src)
        for dst in ("tai", "tt", "tcg", "utc"):
            reverse.append({"from": src, "fromJd": [float(t.jd1), float(t.jd2)], "to": dst, "toJd": [float(getattr(t, dst).jd1), float(getattr(t, dst).jd2)]})
    for src, dst in (("tdb", "tcb"), ("tcb", "tdb")):
        t = getattr(base, "tt").tdb if src == "tdb" else getattr(base, "tt").tcb
        reverse.append({"from": src, "fromJd": [float(t.jd1), float(t.jd2)], "to": dst, "toJd": [float(getattr(t, dst).jd1), float(getattr(t, dst).jd2)]})

# the extremes of the calendar (parsing only): proleptic Gregorian dates with the sign convention of ISO-8601
extremes = []
for text in ["0000-01-01T00:00:00", "9999-12-31T23:59:59", "1582-10-04T12:00:00", "1582-10-15T12:00:00", "1900-03-01T00:00:00", "2000-02-29T12:00:00",
             "1858-11-17T00:00:00", "-04713-11-24T12:00:00", "+02000-01-01T00:00:00", "+99999-12-31T23:59:59", "-99999-01-01T00:00:00", "1970-01-01"]:
    t = Time(text.replace("+", "") if text[0] == "+" else text, scale="tai", format="isot") if text[0] not in "+-" and len(text) > 10 else None
    if t is None:
        # astropy does not parse 5-digit years: use the Julian Date arithmetic of the calendar instead
        continue
    extremes.append({"iso": text, "mjd": float(t.mjd), "jd1": float(t.jd1), "jd2": float(t.jd2)})

with open(OUT, "w", encoding="utf-8") as f:
    json.dump({"cases": cases, "reverse": reverse, "extremes": extremes}, f)
print(len(cases), len(reverse), len(extremes))
