#!/bin/bash
# Revision 2: the optimized CPU path (CPU-opt) against the multithreaded CPU paths of configuration D, in independent
# processes (default 5) with the order of the variants rotated between them (Section 7.12 of the paper, Table 16).
# Requires the .NET 8 SDK; uses data/ru_10MB.bin. Results go to results_run/opt/ in the repository root.
# Run on an otherwise idle machine. Time on configuration B: about 25 min for 5 processes.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
OUT="$HERE/../results_run/opt"
P=${1:-5}
mkdir -p "$OUT"
cd "$HERE/AssocMeasure"
dotnet build -c Release -nologo > "$OUT/build.log" 2>&1
APP=bin/Release/net8.0/AssocMeasure.dll
{ dotnet "$APP" env; date -u; lscpu || true; free -g || true; } > "$OUT/env.txt" 2>&1
for ((p = 0; p < P; p++)); do
  echo "process $p"
  { echo "process $p start $(date -u +%FT%TZ)"; grep MHz /proc/cpuinfo || true; cat /proc/loadavg; } >> "$OUT/clocks.txt"
  dotnet "$APP" opt ../../data/ru_10MB.bin "$p" "$OUT/opt_p$p.json" > "$OUT/opt_p$p.log"
  { echo "process $p end $(date -u +%FT%TZ)"; grep MHz /proc/cpuinfo || true; cat /proc/loadavg; } >> "$OUT/clocks.txt"
done
echo "done: $OUT"
