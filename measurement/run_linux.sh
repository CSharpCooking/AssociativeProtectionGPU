#!/bin/bash
# CPU-side measurements in the setting of configuration B (Tables 7, 8, S2, S3 and the key checks of Section 7.4)
# and the input for the NIST tests. Requires the .NET 8 SDK. Results, logs and system information are written to
# results_run/ in the repository root (excluded from version control). Total time on configuration B: about 1 h.
# Run on an otherwise idle machine connected to mains power.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
OUT="$HERE/../results_run"
DATA=../../data
APP=bin/Release/net8.0/AssocMeasure.dll
mkdir -p "$OUT"
cd "$HERE/AssocMeasure"
[ -f "$DATA/en_10MB.bin" ] || { echo "inputs not found in data/"; exit 1; }

echo "build";        dotnet build -c Release -nologo > "$OUT/build.log" 2>&1
echo "environment";  { dotnet "$APP" env; dotnet --info; lscpu || true; free -g || true; } > "$OUT/env.txt" 2>&1
echo "keys";         dotnet "$APP" keys 100000 > "$OUT/keys.log"
cp "${TMPDIR:-/tmp}/assoc_measure/keys.json" "$OUT/keys_100000.json"
echo "equivalence";  dotnet "$APP" equiv 100 1000000 > "$OUT/equivalence.txt"
echo "formation";    dotnet "$APP" form "$DATA/en_10MB.bin" 10 2 "$OUT/form_en_10MB.json" > "$OUT/form.log"
for f in uniform en ru zip zero ff; do
  echo "recognition $f"
  dotnet "$APP" rec "$DATA/${f}_10MB.bin" 10 2 "$OUT/rec_${f}_10MB.json" >> "$OUT/rec.log"
done
echo "paired design: 16 keys x 6 inputs"
dotnet "$APP" dist 16 2 0 "$OUT/dist_10MB.json" "$DATA/uniform_10MB.bin" "$DATA/en_10MB.bin" "$DATA/ru_10MB.bin" \
  "$DATA/zip_10MB.bin" "$DATA/zero_10MB.bin" "$DATA/ff_10MB.bin" > "$OUT/dist.log"
echo "filler sequences for NIST SP 800-22"
dotnet "$APP" filler "$OUT/fillers" 12500000 > "$OUT/filler.log"
echo "done: $OUT"
