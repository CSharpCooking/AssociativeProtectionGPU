# -*- coding: utf-8 -*-
"""Lockstep work of the search kernel per source byte: etalon comparisons and inspected bytes (Sections 4.3, 7.10).

The search kernel (DecodeContainer in library/CpuGpuStego.cs) compares a section with the etalons 0, 1, ..., 9 and
stops at the first match; every comparison inspects the section byte by byte and stops at the first byte that does
not match (a match inspects all ten bytes). In a warp, the threads execute in lockstep, so that

* a comparison step lasts until the slowest active thread has finished its byte loop, and
* the warp performs comparison steps until its thread with the largest digit has found its etalon.

For every section, the model counts the comparison steps of a warp, max(d) + 1, and its byte steps,
sum over k = 0..max(d) of the largest number of bytes inspected by the threads with d >= k in step k, and sums both
over the three sections (reconvergence after every section, as the three calls of DecodeContainer suggest). The
branch-free kernel always inspects 10 x 10 bytes per section, 300 byte steps per source byte, which bounds the
search from above for every input and key. Because every input was measured with its own key, the ratios of two
inputs are also given over all pairs of keys (independent keys of the two inputs), as quoted in Section 7.10.

The etalons are those of EtalonFactory, the keys are built by the dichotomic partitioning of AssocStego.CreateKey,
and the bit layout is that of BitVector (character j of the etalon string is bit 77 - j; bit p lies in byte
9 - p // 8 of the section), as used by the measurement kit of configuration D. The filler is uniformly random.
The keys and the filler are random with fixed seeds, so that the output is reproducible.

usage: python3 analysis/lockstep_model.py [KEYS]   (default 16 keys; writes analysis/output/lockstep_model.json)
"""
import hashlib
import json
import random
import sys

import numpy as np

from common import DATA, out_path

SEED_PHRASE = 'DiplomaMag.DefaultEtalons.v1'
BITS, SECTION = 78, 10          # etalon length in bits, section length in bytes
WARPS = (8, 16, 32)
N_BYTES = 1 << 16               # source bytes per input and key


def bit_string(seed):
    out, rnd = [], 0
    while len(out) < BITS:
        for v in hashlib.sha256(f'{seed}:{rnd}'.encode()).digest():
            for b in range(8):
                if len(out) < BITS:
                    out.append('1' if (v >> b) & 1 else '0')
        rnd += 1
    return ''.join(out)


def etalons():
    """EtalonFactory.CreateDeterministicEtalons(10, 78, SEED_PHRASE) as bit lists indexed by bit position"""
    res, seen = [], set()
    for i in range(10):
        nonce = 0
        while True:
            s = bit_string(f'{SEED_PHRASE}:{i}:{nonce}')
            if '0' in s and '1' in s and s not in seen:
                seen.add(s)
                res.append([1 if s[BITS - 1 - p] == '1' else 0 for p in range(BITS)])
                break
            nonce += 1
    return res


E = etalons()


def create_key(rng):
    """AssocStego.CreateKey: dichotomic partitioning with one splitting position per node"""
    masks = [[] for _ in range(10)]

    def node(idx):
        if len(idx) < 2:
            return
        c = idx[:]
        rng.shuffle(c)
        a0 = [E[c[0]][p] ^ E[c[1]][p] for p in range(BITS)]
        d1 = [c[0]]
        for t in c[2:]:
            a3 = [x & (E[c[0]][p] ^ E[t][p]) for p, x in enumerate(a0)]
            if any(a3):
                a0 = a3
            else:
                d1.append(t)
        p = rng.choice([q for q in range(BITS) if a0[q]])
        for t in c:
            masks[t].append(p)
        node(d1)
        node([t for t in c if t not in d1])

    node(list(range(10)))
    return masks


def to_bytes(bits):
    out = [0] * SECTION
    for p, v in enumerate(bits):
        if v:
            out[SECTION - 1 - p // 8] |= 1 << (p % 8)
    return out


def key_arrays(masks):
    K = np.zeros((10, SECTION), np.uint8)
    M = np.zeros((10, SECTION), np.uint8)
    for t in range(10):
        k = [0] * BITS
        for p in masks[t]:
            k[p] = 1
        kb, eb = to_bytes(k), to_bytes(E[t])
        K[t] = kb
        M[t] = [e & m for e, m in zip(eb, kb)]
    return K, M


def inspected_bytes(data, K, M, rng):
    """digits (n, 3) and bytes inspected by the comparison of each section with each etalon (n, 3, 10)"""
    d = np.stack([data // 100, (data // 10) % 10, data % 10], axis=1)
    sections = (rng.integers(0, 256, size=(len(data), 3, SECTION), dtype=np.uint8) & ~K[d]) | M[d]
    insp = np.empty((len(data), 3, 10), np.int32)
    for k in range(10):
        mismatch = (sections & K[k]) != M[k]
        insp[:, :, k] = np.where(mismatch.any(axis=2), np.argmax(mismatch, axis=2) + 1, SECTION)
    return d, insp


def lockstep(d, insp, w):
    n = len(d) // w * w
    d = d[:n].reshape(-1, w, 3)
    insp = insp[:n].reshape(-1, w, 3, 10)
    comparisons = float((d.max(axis=1) + 1).sum(axis=1).mean())
    byte_steps = sum(float(np.where(d >= k, insp[..., k], 0).max(axis=1).sum(axis=1).mean()) for k in range(10))
    return comparisons, byte_steps


def main():
    n_keys = int(sys.argv[1]) if len(sys.argv) > 1 else 16
    rng_keys, rng_data = random.Random(2), np.random.default_rng(1)
    inputs = {'uniform': rng_data.integers(0, 256, N_BYTES).astype(np.int64),
              'ff': np.full(N_BYTES, 255, np.int64), 'zero': np.zeros(N_BYTES, np.int64),
              'ru': np.frombuffer((DATA / 'ru_10MB.bin').read_bytes()[:N_BYTES], np.uint8).astype(np.int64)}
    res = {w: {name: [] for name in inputs} for w in WARPS}
    per_tried = []
    for _ in range(n_keys):
        K, M = key_arrays(create_key(rng_keys))
        for name, data in inputs.items():
            d, insp = inspected_bytes(data, K, M, rng_data)
            for w in WARPS:
                res[w][name].append(lockstep(d, insp, w))
            if name == 'uniform':
                tried = int((d + 1).sum())
                per_tried.append(sum(int(np.where(d >= k, insp[..., k], 0).sum()) for k in range(10)) / tried)
    out = {'keys': n_keys, 'bytes_per_input': N_BYTES, 'branch_free_byte_steps': 3 * 10 * SECTION,
           'bytes_per_tried_etalon_uniform': [min(per_tried), max(per_tried)], 'warps': {}}
    print(f'{n_keys} keys, {N_BYTES} bytes per input; bytes inspected per tried etalon (uniform, sequential search): '
          f'{min(per_tried):.2f} to {max(per_tried):.2f}; branch-free kernel: 300 byte steps per byte')
    for w in WARPS:
        a = {name: np.array(v) for name, v in res[w].items()}
        o = {name: {'comparisons': float(v[:, 0].mean()), 'byte_steps': float(v[:, 1].mean()),
                    'byte_steps_range': [float(v[:, 1].min()), float(v[:, 1].max())]} for name, v in a.items()}
        for other in ('ff', 'ru', 'zero'):
            rc, rb = a['uniform'][:, 0] / a[other][:, 0], a['uniform'][:, 1] / a[other][:, 1]
            # the measurements used a separate key for every input: range of the ratio over all pairs of keys
            ind = [float(a['uniform'][:, 1].min() / a[other][:, 1].max()), float(a['uniform'][:, 1].max() / a[other][:, 1].min())]
            o[f'uniform_over_{other}'] = {'comparisons': float(rc.mean()), 'byte_steps': float(rb.mean()),
                                          'byte_steps_range': [float(rb.min()), float(rb.max())],
                                          'byte_steps_range_independent_keys': ind}
        out['warps'][str(w)] = o
        print(f'--- warps of {w} threads (same key for all inputs; mean over the keys, range over the keys)')
        for name in inputs:
            print(f"{name:8s} comparison steps {o[name]['comparisons']:6.2f}   byte steps {o[name]['byte_steps']:7.2f} "
                  f"({o[name]['byte_steps_range'][0]:.1f} to {o[name]['byte_steps_range'][1]:.1f})")
        for other in ('ff', 'ru', 'zero'):
            r = o[f'uniform_over_{other}']
            print(f"  uniform/{other}: comparison steps {r['comparisons']:.2f}; byte steps {r['byte_steps']:.2f} "
                  f"({r['byte_steps_range'][0]:.2f} to {r['byte_steps_range'][1]:.2f}; "
                  f"{r['byte_steps_range_independent_keys'][0]:.3f} to {r['byte_steps_range_independent_keys'][1]:.3f} "
                  f"with independent keys of the two inputs)")
    json.dump(out, open(out_path('lockstep_model.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
