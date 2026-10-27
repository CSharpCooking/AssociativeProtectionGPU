# -*- coding: utf-8 -*-
"""Size, SHA-256 and the comparison counts of the sequential search for input files.

For a byte with the decimal digits d2 d1 d0, the search compares d2 + d1 + d0 + 3 etalons (it stops at the first
match in the order 0, 1, ..., 9). The warp-level count is the mean over groups of 32 consecutive bytes of the
largest count in the group, summed over the three sections (reconvergence after every section) or taken for the
whole block; the same count for groups of 16 bytes (the group size of the integrated GPU of configuration D) is
also printed. Control values: uniform bytes 12.30 / 22.9 / 20.0; all-zero 3 / 3 / 3; all-255 15 / 15 / 15.

usage: python3 analysis/input_stats.py FILE [FILE ...]
"""
import hashlib
import sys

import numpy as np


def comparisons(a):
    a = a.astype(np.int32)
    d2, d1, d0 = a // 100, (a // 10) % 10, a % 10
    mean = float((d2 + d1 + d0 + 3).mean())
    n = len(a) // 32 * 32
    w = lambda d: d[:n].reshape(-1, 32).max(axis=1)
    warp_section = float((w(d2) + w(d1) + w(d0) + 3).mean())
    warp_block = float(w(d2 + d1 + d0 + 3).mean())
    return mean, warp_section, warp_block


def group16(a):
    """warp-level count with reconvergence after every section for groups of 16 consecutive bytes"""
    a = a.astype(np.int32)
    d2, d1, d0 = a // 100, (a // 10) % 10, a % 10
    n = len(a) // 16 * 16
    w = lambda d: d[:n].reshape(-1, 16).max(axis=1)
    return float((w(d2) + w(d1) + w(d0) + 3).mean())


def stats(path):
    raw = open(path, 'rb').read()
    a = np.frombuffer(raw, dtype=np.uint8)
    mean, ws, wb = comparisons(a)
    return dict(bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest(), mean=mean, warp_section=ws, warp_block=wb,
                group16_section=group16(a))


if __name__ == '__main__':
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    for p in sys.argv[1:]:
        s = stats(p)
        print(f"{p}\n  bytes: {s['bytes']}\n  SHA-256: {s['sha256']}\n  comparisons per byte, mean: {s['mean']:.3f}\n"
              f"  warp level, reconvergence after every section: {s['warp_section']:.3f}\n"
              f"  warp level, reconvergence after the block: {s['warp_block']:.3f}\n"
              f"  groups of 16, reconvergence after every section: {s['group16_section']:.3f}")
