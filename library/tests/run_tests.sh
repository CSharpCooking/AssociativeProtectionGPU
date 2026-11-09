#!/bin/bash
# Tests of the key validation added in revision 2 (library/KeyValidation.cs and the [r2] lines of CpuGpuStego.cs).
# Usage: ./run_tests.sh [number of keys, default 100000]
set -e
cd "$(dirname "$0")"
python3 extract.py
dotnet run -c Release -- "${1:-100000}"
