"""Builds the tables of docs/comparison.md from the raw results kept in benchmarks/comparison/results.

    python benchmarks/comparison/make_report.py

The Python results (results/cfitsio-N.json, results/astropy-N.json) are three passes of bench_cfitsio.py and bench_astropy.py; the best
median of the passes is kept, because a laptop that scales its clock only ever makes a pass slower than it could be. The .NET results are
the BenchmarkDotNet reports (results/dotnet/*-report-github.md, job MediumRun), whose Median column is used.
The text between the markers of docs/comparison.md is replaced; the rest of the page is written by hand.
"""
import glob
import html
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
RESULTS = os.path.join(HERE, "results")
PAGE = os.path.normpath(os.path.join(HERE, "..", "..", "docs", "comparison.md"))
BEGIN, END = "<!-- BEGIN TABLES -->", "<!-- END TABLES -->"


def python_results(engine):
    runs = [json.load(open(p))["ms"] for p in sorted(glob.glob(os.path.join(RESULTS, f"{engine}-[0-9].json")))]
    return {k: min(r[k] for r in runs) for k in runs[0]}


UNITS = {"ns": 1e-6, "us": 1e-3, "μs": 1e-3, "µs": 1e-3, "ms": 1.0, "s": 1000.0}


def to_ms(text):
    number, unit = text.replace(",", "").split()
    return float(number) * UNITS[unit]


def dotnet_results():
    """{(file, method, params): median in ms} from the BenchmarkDotNet markdown reports."""
    table = {}
    for path in glob.glob(os.path.join(RESULTS, "dotnet", "*-report-github.md")):
        name = os.path.basename(path).split("-report")[0]
        lines = [l for l in open(path, encoding="utf-8") if l.startswith("|")]
        header = [h.strip() for h in lines[0].strip().strip("|").split("|")]
        median = header.index("Median")
        first_param = 1
        last_param = header.index("Mean")
        for line in lines[2:]:
            cells = [html.unescape(c.strip().replace("**", "").strip("'")) for c in line.strip().strip("|").split("|")]
            method = cells[0].strip("'")
            params = tuple(cells[first_param:last_param])
            table[(name, method, params)] = to_ms(cells[median])
    return table


NET = dotnet_results()
CF = python_results("cfitsio")
AP = python_results("astropy")


def net(file, method, *params):
    return NET[(file, method, tuple(str(p) for p in params))]


def fmt(ms):
    if ms is None:
        return "–"
    if ms < 1:
        return f"{ms:.2f}"
    if ms < 10:
        return f"{ms:.1f}"
    return f"{ms:,.0f}".replace(",", " ")


def versus(other, ours):
    """How our time compares to another library: 'x faster' when ours is smaller."""
    if other is None:
        return "–"
    ratio = other / ours
    if ratio >= 1.05:
        return f"**{ratio:.1f}× faster**" if ratio >= 1.5 else f"{ratio:.1f}× faster"
    if ratio <= 0.95:
        return f"*{1 / ratio:.1f}× slower*"
    return "same"


def table(title, rows, note=None):
    out = [f"### {title}", "", "| Operation | Eleris.FitsNet (ms) | cfitsio (ms) | astropy (ms) | against cfitsio | against astropy |",
           "|---|---:|---:|---:|---|---|"]
    for label, ours, cf, ap in rows:
        out.append(f"| {label} | {fmt(ours)} | {fmt(cf)} | {fmt(ap)} | {versus(cf, ours)} | {versus(ap, ours)} |")
    if note:
        out += ["", note]
    return "\n".join(out) + "\n"


IO, COMP, FLOAT = "ImageIoBenchmarks", "CompressionBenchmarks", "FloatCompressionBenchmarks"
BIN, ASC = "BinaryTableBenchmarks", "AsciiTableBenchmarks"

images = table("Images: reading, cutouts, writing, checksums (one 3536 × 3536 frame, 25 MB)", [
    ("Open, header only", net(IO, "Open (header only)") , CF["Open (header only)"], AP["Open (header only)"]),
    ("Open + read `ushort` pixels", net(IO, "Open + ReadPixels<ushort> (BZERO convention)"), CF["Open + read ushort"], AP["Open + read ushort (memmap=False)"]),
    ("Open + read `float32`", net(IO, "Open + ReadPixels<float>"), CF["Open + read float32"], AP["Open + read float32"]),
    ("Open + read `float64`", net(IO, "Open + ReadPixels<double>"), CF["Open + read float64"], AP["Open + read float64"]),
    ("Cutout 256 × 256", net(IO, "Cutout 256x256 (lazy file)"), CF["Cutout 256x256"], AP["Cutout 256x256"]),
    ("Cutout 16 × 3536 (a narrow column)", net(IO, "Cutout 16 x 3536 (a narrow column)"), CF["Cutout 16 x 3536 (narrow column)"], AP["Cutout 16 x 3536 (narrow column)"]),
    ("Cutout 3536 × 64 (64 whole rows)", net(IO, "Cutout 3536 x 64 (64 whole rows)"), CF["Cutout 3536 x 64 (64 whole rows)"], AP["Cutout 3536 x 64 (64 whole rows)"]),
    ("Create the image from pixels + save to memory", net(IO, "ImageHdu.Create<ushort>") + net(IO, "Save to memory, no checksum"), CF["Save to memory (ushort, no checksum)"], AP["Save to memory (ushort, no checksum)"]),
    ("Create the image from pixels + save to memory with `CHECKSUM` + `DATASUM`", net(IO, "ImageHdu.Create<ushort>") + net(IO, "Save to memory, CHECKSUM + DATASUM"), CF["Save to memory, CHECKSUM + DATASUM"], AP["Save to memory, CHECKSUM + DATASUM"]),
    ("Verify `CHECKSUM` + `DATASUM`", net(IO, "VerifyChecksums (lazy file)"), CF["Open + verify CHECKSUM/DATASUM (reads data)"], AP["Open from memory + verify CHECKSUM/DATASUM (reads data)"]),
])


def compression_rows(parallelism):
    rows = []
    for label, algo, key in [("Rice", "Rice", "Rice"), ("GZIP_1", "Gzip1", "Gzip1"), ("GZIP_2", "Gzip2", "Gzip2"), ("HCOMPRESS", "HCompress", "HCompress")]:
        rows.append((f"Compress {label}", net(COMP, "Compress", algo, parallelism), CF[f"Compress {key} (ushort, to a file)"], AP[f"Compress {key} (ushort, to memory)"]))
        rows.append((f"Decompress {label}", net(COMP, "Decompress to ushort", algo, parallelism), CF[f"Decompress {key} to ushort"], AP[f"Decompress {key} to ushort"]))
    q = "SubtractiveDither1"
    rows.append(("Compress `float32` (Rice, dither 1)", net(FLOAT, "Compress float32", q, parallelism), CF["Compress float32 (Rice, SUBTRACTIVE_DITHER_1)"], AP["Compress float32 (Rice, SUBTRACTIVE_DITHER_1)"]))
    rows.append(("Decompress `float32` (Rice, dither 1)", net(FLOAT, "Decompress float32", q, parallelism), CF["Decompress float32 (Rice, SUBTRACTIVE_DITHER_1)"], AP["Decompress float32 (Rice, SUBTRACTIVE_DITHER_1)"]))
    return rows


one_core = table("Tile compression on one core (`MaxDegreeOfParallelism = 1`)", compression_rows(1))
all_cores = table("Tile compression on all 8 cores (`MaxDegreeOfParallelism = -1`)", compression_rows(-1),
                  "cfitsio and astropy work on one core whatever the machine: their columns are the same as in the previous table.")

N = 200_000
tables = table("Binary tables, 200 000 rows of 40 bytes (6 columns)", [
    ("Create the table from column arrays", net(BIN, "Create the table from column arrays", N), None, AP["Binary table 200k: create from columns"]),
    ("Read a `double` column", net(BIN, "ReadColumn<double> (lazy file)", N), CF["Binary table 200k: open + read double column"], AP["Binary table 200k: open + read double column"]),
    ("Read a string column", net(BIN, "ReadStringColumn (in memory)", N), CF["Binary table 200k: open + read string column (list)"], AP["Binary table 200k: open + read string column (list)"]),
    ("Save", net(BIN, "Save to a stream", N), CF["Binary table 200k: save (to a file)"], AP["Binary table 200k: save to memory"]),
], "Eleris.FitsNet reads the `double` column from a file opened beforehand and the string column from a table already in memory; cfitsio and astropy open the file at each run (0.1 ms and 0.4 ms).")

ascii_tables = table("ASCII tables, 200 000 rows (4 columns)", [
    ("Create the table from column arrays", net(ASC, "Create an ASCII table", N), None, AP["ASCII table 200k: create"]),
    ("Read an `F12.5` column", net(ASC, "Read a float column (F format)", N), CF["ASCII table 200k: open + read F column"], AP["ASCII table 200k: open + read F column"]),
    ("Read an `E16.8` column", net(ASC, "Read a float column (E format)", N), CF["ASCII table 200k: open + read E column"], AP["ASCII table 200k: open + read E column"]),
    ("Read an integer column", net(ASC, "Read an integer column", N), CF["ASCII table 200k: open + read integer column"], AP["ASCII table 200k: open + read integer column"]),
], "Eleris.FitsNet reads tables that are already in memory; cfitsio and astropy open the file at each run.")

generated = "\n".join([images, one_core, all_cores, tables, ascii_tables])
text = open(PAGE, encoding="utf-8").read()
start, end = text.index(BEGIN) + len(BEGIN), text.index(END)
open(PAGE, "w", encoding="utf-8", newline="\n").write(text[:start] + "\n\n" + generated + "\n" + text[end:])
print("wrote", PAGE)
