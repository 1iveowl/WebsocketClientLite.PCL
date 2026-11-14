"""Combines the runs of compare.sh: per workload and variant, the median of the per-run
medians, their range, and the median allocation per operation.

Usage: python3 -I verification/benchmark-async/summarize.py OUTPUT_DIR
"""
import csv
import glob
import os
import statistics
import sys

runs = {}
for path in sorted(glob.glob(os.path.join(sys.argv[1], "run-*.csv"))):
    variant = os.path.basename(path).rsplit("-", 1)[1].split(".")[0]
    lines = [line for line in open(path, encoding="utf-8") if line[:1].islower()]
    for row in csv.DictReader(lines):
        runs.setdefault((row["workload"], variant), []).append(
            (float(row["median_us"]), float(row["alloc_B_per_op"])))

print("| Workload | Runs | Off, µs (median, range) | On, µs (median, range) | On / off | Off, B/op | On, B/op |")
print("| --- | --- | --- | --- | --- | --- | --- |")
for workload in dict.fromkeys(w for w, _ in runs):
    off, on = runs[(workload, "off")], runs[(workload, "on")]
    t_off, t_on = [t for t, _ in off], [t for t, _ in on]
    b_off = statistics.median(b for _, b in off)
    b_on = statistics.median(b for _, b in on)
    print(f"| {workload} | {len(off)} + {len(on)} "
          f"| {statistics.median(t_off):.2f} ({min(t_off):.2f} to {max(t_off):.2f}) "
          f"| {statistics.median(t_on):.2f} ({min(t_on):.2f} to {max(t_on):.2f}) "
          f"| {statistics.median(t_on) / statistics.median(t_off):.3f} | {b_off:.0f} | {b_on:.0f} |")
