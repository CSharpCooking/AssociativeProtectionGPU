#!/bin/bash
# NIST SP 800-22 Rev. 1a reference implementation (STS): 100 bitstreams of 10^6 bits, all 15 tests, default
# parameters (the setting of Table 9), or the Block Frequency test alone (block length M = 128) with "bf".
#
# usage: STS_DIR=/path/to/sts analysis/run_nist.sh <binary file> <label> <output directory> [bf]
#
# STS_DIR is the built "sts" directory of the suite. The paper used the NIST reference code as mirrored at
# https://github.com/terrillmoore/NIST-Statistical-Test-Suite (commit 07cd577, built with its makefile on
# Ubuntu 24.04). The file must hold at least
# 12,500,000 bytes (10^8 bits). The suite writes into its own experiments/ directory; the report and the
# per-test statistics are copied to <output directory>.
# Note: assess may return a nonzero exit code on success, so there is no "set -e" here.
if [ -z "$STS_DIR" ] || [ ! -x "$STS_DIR/assess" ]; then echo "set STS_DIR to the built sts directory"; exit 1; fi
if [ $# -lt 3 ]; then echo "usage: STS_DIR=... $0 <binary file> <label> <output directory> [bf]"; exit 1; fi
IN=$(readlink -f "$1"); LABEL="$2"; mkdir -p "$3"; OUT=$(readlink -f "$3")
cd "$STS_DIR" || exit 1
if [ "$4" = "bf" ]; then
  # input from file, binary format; test selection: only the second test (Block Frequency), default parameters
  printf "0\n%s\n0\n010000000000000\n0\n100\n1\n" "$IN" | ./assess 1000000 > "$OUT/$LABEL.log" 2>&1
  cp experiments/AlgorithmTesting/finalAnalysisReport.txt "$OUT/${LABEL}_finalAnalysisReport.txt"
else
  # input from file, all tests, default parameters, 100 bitstreams, binary format
  printf "0\n%s\n1\n0\n100\n1\n" "$IN" | ./assess 1000000 > "$OUT/$LABEL.log" 2>&1
  echo "assess exit code: $?" >> "$OUT/$LABEL.log"
  cp experiments/AlgorithmTesting/finalAnalysisReport.txt "$OUT/${LABEL}_finalAnalysisReport.txt"
  mkdir -p "$OUT/${LABEL}_detail"
  for d in experiments/AlgorithmTesting/*/; do
    n=$(basename "$d")
    cp "$d/stats.txt" "$OUT/${LABEL}_detail/${n}_stats.txt" 2>/dev/null
    cp "$d/results.txt" "$OUT/${LABEL}_detail/${n}_results.txt" 2>/dev/null
  done
fi
echo "done $LABEL"
