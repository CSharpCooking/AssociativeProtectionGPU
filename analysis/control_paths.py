# -*- coding: utf-8 -*-
"""Number of different control paths that the threads of a warp take in the search kernel, by input and key (Section 7.14).

The control path of a thread through the search kernel (DecodeContainer in library/CpuGpuStego.cs) is fixed by the etalons
that it tries in each of the three sections (0, 1, ..., d for a section with digit d) and by the byte at which each
comparison stops. A comparison with a non-matching etalon j inspects, under the mask of j, the bits of the matching etalon
at the positions that the two masks share and filler bits at the other positions, so that the byte at which it stops is
random unless an earlier byte already decides it. For a constant input such as all-255, whose digits are the same in all
threads, the paths of the threads therefore differ or coincide depending on the key; for uniform data they differ for
every key, because the digits differ.

For KEYS random keys (the etalons, the key construction and the bit layout of analysis/lockstep_model.py) and 8,192 source
bytes per input and key, the script counts the different paths among the 32 threads of every warp and reports, per input,
the distribution over the keys of the mean count per warp, and the share of the keys for which all threads of every warp
take the same path. Seeds are fixed, so that the output is reproducible.

usage: python3 analysis/control_paths.py [KEYS]   (default 400; writes analysis/output/control_paths.json)
"""
import json
import random
import sys

import numpy as np

import lockstep_model as L
from common import out_path

WARP, N = 32, 32 * 256


def main():
    n_keys = int(sys.argv[1]) if len(sys.argv) > 1 else 400
    rng_keys, rng = random.Random(7), np.random.default_rng(3)
    inputs = {'uniform': rng.integers(0, 256, N).astype(np.int64), 'ff': np.full(N, 255, np.int64)}
    mean_paths = {name: [] for name in inputs}
    for _ in range(n_keys):
        K, M = L.key_arrays(L.create_key(rng_keys))
        for name, data in inputs.items():
            d, insp = L.inspected_bytes(data, K, M, rng)
            tried = np.arange(10)[None, None, :] <= d[:, :, None]           # comparisons that the thread executes
            path = np.where(tried, insp, 0).reshape(N, 30)
            counts = [len({tuple(r) for r in path[w * WARP:(w + 1) * WARP]}) for w in range(N // WARP)]
            mean_paths[name].append(float(np.mean(counts)))
    res = {'keys': n_keys, 'bytes_per_key': N, 'warp': WARP}
    for name, v in mean_paths.items():
        a = np.array(v)
        res[name] = {'min': float(a.min()), 'q25': float(np.percentile(a, 25)), 'median': float(np.median(a)),
                     'q75': float(np.percentile(a, 75)), 'max': float(a.max()),
                     'share_single_path_pct': float(100 * (a == 1).mean())}
        print(f"{name:8s} paths per warp of {WARP} threads over {n_keys} keys: min {a.min():.2f}, quartiles {res[name]['q25']:.2f} / "
              f"{res[name]['median']:.2f} / {res[name]['q75']:.2f}, max {a.max():.2f}; keys with one path: {res[name]['share_single_path_pct']:.1f}%")
    assert res['uniform']['min'] > 0.9 * WARP                                  # uniform data: almost every thread has its own path
    json.dump(res, open(out_path('control_paths.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
