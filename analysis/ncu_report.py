# -*- coding: utf-8 -*-
"""Metric values of the profiled kernel launches in NVIDIA Nsight Compute reports, read without the NVIDIA tools.

The kit of configuration D profiled the kernels with `ncu --set full --export` (measurement/laptop/run_revision2.cmd, group
ncu). Nsight Compute 2026.4 writes the report as <name>.ncu-repz, a Zstandard-compressed .ncu-rep. The layout of the
uncompressed file was found by inspection (it is not documented): the magic bytes 'NVR\\0', a 32-bit version (2), a
two-byte header, then chunks of a 32-bit little-endian length followed by a protobuf message. One chunk holds the table of
the metric names (field 4, repeated field 1). Every profiled launch is one chunk, in the order of the launches: fields 5 and
6 hold the name of the kernel function and the repeated field 13 the metrics, as {1: index of the name, 2: value}, the value
being a message with one field whose wire type gives the type (string, double, float or integer). The reading was checked
against the texts of the rules stored in the same reports, which quote several of the values (the number of active threads
per warp, the numbers of sectors).

The reports of configuration D are in results/configuration_D_r2/ncu/ (139 MB); this script turned them into
ncu_metrics.csv in the same folder, which analysis/ncu_counters.py reads.

usage: python3 analysis/ncu_report.py OUT.csv INPUT=REPORT [INPUT=REPORT ...]
       e.g. python3 analysis/ncu_report.py ncu_metrics.csv uniform=ncu_dGPU_uniform.ncu-repz ff=ncu_dGPU_ff.ncu-repz
A .ncu-repz file needs a Zstandard module (compression.zstd of Python 3.14, backports.zstd, zstandard or pyzstd);
alternatively decompress it first (zstd -d -o report.ncu-rep report.ncu-repz).
"""
import csv
import struct
import sys

MAGIC = b'NVR\x00'


def _varint(b, i):
    x = s = 0
    while True:
        c = b[i]; i += 1
        x |= (c & 0x7F) << s; s += 7
        if c < 0x80:
            return x, i


def _fields(b, start, end):
    """(field number, wire type, value) of a protobuf message; the value of a length-delimited field is its bounds"""
    i = start
    while i < end:
        key, i = _varint(b, i)
        f, w = key >> 3, key & 7
        if w == 0:
            v, i = _varint(b, i)
        elif w == 1:
            v = bytes(b[i:i + 8]); i += 8
        elif w == 5:
            v = bytes(b[i:i + 4]); i += 4
        elif w == 2:
            n, i = _varint(b, i); v = (i, i + n); i += n
        else:
            raise ValueError(f'wire type {w} at offset {i}')
        yield f, w, v
    if i != end:
        raise ValueError('message overruns its bounds')


def _try_fields(b, a, e):
    try:
        out = list(_fields(b, a, e))
        return out if out and all(0 < f < 2000 for f, _, _ in out) else None
    except (ValueError, IndexError):
        return None


def _load(path):
    data = open(path, 'rb').read()
    if data[:4] == MAGIC:
        return data
    for name in ('compression.zstd', 'backports.zstd', 'zstandard', 'pyzstd'):
        try:
            mod = __import__(name, fromlist=['decompress'])
        except ImportError:
            continue
        if name == 'zstandard':
            return mod.ZstdDecompressor().decompressobj().decompress(data)
        return mod.decompress(data)
    raise SystemExit(f'{path}: compressed report and no Zstandard module; run zstd -d first')


def read(path):
    """list of dicts, one per profiled launch in the order of the launches: metric name -> value; '_function' is the kernel"""
    b = _load(path)
    if b[:4] != MAGIC:
        raise SystemExit(f'{path}: not an Nsight Compute report')
    chunks, i = [], 10
    while i < len(b):
        n = struct.unpack_from('<I', b, i)[0]
        chunks.append((i + 4, i + 4 + n)); i += 4 + n
    names, results = None, []
    for a, e in chunks:
        fl = _try_fields(b, a, e)
        if fl is None:
            continue
        nums = [x[0] for x in fl]
        if names is None and nums.count(4) == 1 and nums.count(13) == 0:
            f4 = [x for x in fl if x[0] == 4][0]
            if f4[1] == 2 and f4[2][1] - f4[2][0] > 10000:
                sub = _try_fields(b, *f4[2])
                if sub and all(x[0] == 1 and x[1] == 2 for x in sub):
                    names = [bytes(b[x[2][0]:x[2][1]]).decode('utf-8', 'replace') for x in sub]
                    continue
        if nums.count(13) > 100:
            results.append(fl)
    if names is None or not results:
        raise SystemExit(f'{path}: the table of metric names or the profile results were not found')
    out = []
    for fl in results:
        m = {}
        for fn, w, v in fl:
            if fn in (5, 6) and w == 2:
                m['_function'] = bytes(b[v[0]:v[1]]).decode('utf-8', 'replace')
            if fn != 13 or w != 2:
                continue
            idx = val = None
            for f2, w2, v2 in _fields(b, v[0], v[1]):
                if f2 == 1 and w2 == 0:
                    idx = v2
                elif f2 == 2 and w2 == 2:
                    for f3, w3, v3 in _fields(b, v2[0], v2[1]):
                        if w3 == 2:
                            val = bytes(b[v3[0]:v3[1]]).decode('utf-8', 'replace')
                        elif w3 == 1:
                            val = struct.unpack('<d', v3)[0]
                        elif w3 == 5:
                            val = struct.unpack('<f', v3)[0]
                        else:
                            val = v3
            if idx is not None and val is not None:
                m[names[idx] if idx < len(names) else f'#{idx}'] = val
        out.append(m)
    return out


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    with open(sys.argv[1], 'w', newline='', encoding='utf-8') as fh:
        wr = csv.writer(fh, lineterminator='\n')
        wr.writerow(['input', 'launch', 'kernel', 'metric', 'value'])
        for arg in sys.argv[2:]:
            name, path = arg.split('=', 1)
            for k, m in enumerate(read(path)):
                kernel, rows = m.get('_function', ''), 0
                for key, val in m.items():
                    if key == '_function' or key.startswith(('group:', 'breakdown:')):
                        continue                        # lists of metric names, not values
                    wr.writerow([name, k, kernel, key, repr(val) if isinstance(val, float) else val])
                    rows += 1
                print(f'{name}: launch {k}, {kernel}, {rows} metrics', file=sys.stderr)


if __name__ == '__main__':
    main()
