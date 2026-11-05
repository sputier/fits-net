#!/bin/sh
# Three passes of each Python benchmark; make_report.py keeps the best median of each operation.
cd "$(dirname "$0")/../.."
for pass in 1 2 3; do
  BENCH_PASS=$pass python benchmarks/comparison/bench_cfitsio.py
  BENCH_PASS=$pass python benchmarks/comparison/bench_astropy.py
done
