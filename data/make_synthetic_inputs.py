# -*- coding: utf-8 -*-
"""Regenerates the three synthetic inputs (uniform, all-zero, all-255) and checks all six inputs against SHA256SUMS.

usage: python3 data/make_synthetic_inputs.py [--check]"""
import hashlib
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
N = 10 * 2 ** 20  # 10 MiB

if '--check' not in sys.argv:
    (HERE / 'uniform_10MB.bin').write_bytes(np.random.default_rng(20261026).integers(0, 256, size=N, dtype=np.uint8).tobytes())
    (HERE / 'zero_10MB.bin').write_bytes(bytes(N))
    (HERE / 'ff_10MB.bin').write_bytes(b'\xff' * N)

ok = True
for line in (HERE / 'SHA256SUMS').read_text().splitlines():
    digest, name = line.split()
    p = HERE / name
    actual = hashlib.sha256(p.read_bytes()).hexdigest() if p.exists() else 'missing'
    print(f'{name:<18} {"OK" if actual == digest else "MISMATCH (" + actual + ")"}')
    ok &= actual == digest
sys.exit(0 if ok else 1)
