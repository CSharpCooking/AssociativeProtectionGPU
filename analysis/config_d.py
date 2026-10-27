# -*- coding: utf-8 -*-
"""Configuration D: the revision measurements on a second laptop with a GPU (measurement/laptop), computed from the
BenchmarkDotNet reports and logs and from the kernel times in results/configuration_D.
Tables 10-14 and Figure 5 of the paper, Sections 7.8-7.11, Table S5 (statistics) and Table S6 (kernel times).
Writes analysis/output/config_D.json and prints a report.

The statistics of every case are taken from the CSV report (mean, half-width of the 99.9% confidence interval,
SD, SE, quartiles, extremes, number of iterations after the removal of upper outliers, allocation). The log of
every class gives the time of every measured iteration of every process; the script recomputes the mean and the
number of iterations from it as a check of the report and gives the mean of every process (the break-even grid
ran three processes per case). kernels.csv was written with the decimal comma of the Russian locale, so that a
line has six fields: device, input, step, repetition, integer part, fraction.

usage: python3 analysis/config_d.py"""
import csv
import json
import math
import re
from pathlib import Path

from common import ROOT, RESULTS, out_path

DIR = RESULTS / 'configuration_D'
CLASSES = ['RevBreakEven', 'RevFullCycle', 'RevFormation', 'RevChunkSize', 'RevRecovery', 'RevRecognition']
TO_MS = {'ns': 1e-6, 'μs': 1e-3, 'us': 1e-3, 'ms': 1.0, 's': 1e3}
TO_MB = {'B': 2.0 ** -20, 'KB': 2.0 ** -10, 'MB': 1.0, 'GB': 2.0 ** 10}


def quantity(text, table):
    """'1,033.6 μs' -> 1.0336 (ms); '33.07 MB' -> 33.07 (MB); '-' or '' -> None"""
    t = text.strip().strip('"')
    if t in ('', '-', 'NA', '?'):
        return None
    v, u = t.split()
    return float(v.replace(',', '')) * table[u]


def read_report(cls):
    """rows of the CSV report: params, statistics in ms, N, allocation in MB, collections per 1000 operations"""
    path = DIR / cls / 'results' / f'{cls}-report.csv'
    with open(path, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.reader(f, delimiter=';'))
    head, body = rows[0], rows[1:]
    i0, i1 = head.index('WarmupCount') + 1, head.index('Mean')
    names = head[i0:i1]
    out = []
    for r in body:
        d = dict(zip(head, r))
        rec = {'class': cls, 'job': d['Job'], 'params': {n: d[n] for n in names}}
        for k in ('Mean', 'Error', 'StdDev', 'StdErr', 'Median', 'Min', 'Q1', 'Q3', 'Max'):
            rec[k] = quantity(d[k], TO_MS)
        rec['N'] = int(round(float(d['Iterations'])))
        rec['alloc_MB'] = quantity(d['Allocated'], TO_MB)
        # BenchmarkDotNet omits the columns of the generations without collections in a class
        rec['gc_per_1000'] = [None if d.get(g, '-') in ('-', '') else float(d[g]) for g in ('Gen0', 'Gen1', 'Gen2')]
        out.append(rec)
    return out


BENCH = re.compile(r"^// Benchmark: (\w+)\.'?[^:]*'?: (\w+)\(.*\) \[(.*)\]\s*$")
LAUNCH = re.compile(r'^// Launch: (\d+) / (\d+)')
ITER = re.compile(r'^(WorkloadResult|WorkloadActual|WorkloadWarmup)\s+\d+: 1 op, ([\d.]+) ns')


def read_log(path):
    """{params tuple: [launch: {'result': [...ms], 'actual': [...], 'warmup': [...]}]} in the order of the log"""
    runs, cur = {}, None
    for line in open(path, encoding='utf-8', errors='replace'):
        line = line.rstrip('\r\n')
        m = BENCH.match(line)
        if m:
            params = tuple(p.split('=', 1)[1] for p in m.group(3).split(', '))
            cur = runs.setdefault(params, [])
            continue
        m = LAUNCH.match(line)
        if m and cur is not None:
            cur.append({'result': [], 'actual': [], 'warmup': []})
            continue
        m = ITER.match(line)
        if m and cur:
            key = {'WorkloadResult': 'result', 'WorkloadActual': 'actual', 'WorkloadWarmup': 'warmup'}[m.group(1)]
            cur[-1][key].append(float(m.group(2)) * 1e-6)
    return runs


def mean(xs):
    return sum(xs) / len(xs)


def sd(xs):
    m = mean(xs)
    return math.sqrt(sum((x - m) ** 2 for x in xs) / (len(xs) - 1))


# ---------------------------------------------------------------- reports and logs
R = {}          # class -> {params tuple: record}
CHECK = []
for cls in CLASSES:
    logs = sorted((DIR / cls).glob(f'{cls}-*.log'))
    main_log = read_log(logs[-1])            # the report belongs to the last run of the class
    R[cls] = {}
    for rec in read_report(cls):
        key = tuple(rec['params'].values())
        launches = main_log[key]
        res = [x for l in launches for x in l['result']]
        rec['launch_means'] = [mean(l['result']) for l in launches]
        rec['launch_N'] = [len(l['result']) for l in launches]
        rec['warmup_per_launch'] = [len(l['warmup']) for l in launches]
        rec['measured_per_launch'] = [len(l['actual']) for l in launches]
        rec['log_mean'], rec['log_N'] = mean(res), len(res)
        rec['log_sd'] = sd(res)
        rel = abs(rec['log_mean'] / rec['Mean'] - 1)
        CHECK.append((cls, key, rel, rec['log_N'] == rec['N']))
        R[cls][key] = rec
    if len(logs) > 1:                       # RevFormation: the first log is a separate trial run of the class
        R[cls + '_trial'] = {k: {'Mean': mean([x for l in v for x in l['result']]), 'N': sum(len(l['result']) for l in v)}
                             for k, v in read_log(logs[0]).items()}

worst = max(CHECK, key=lambda c: c[2])
assert all(c[3] for c in CHECK), [c for c in CHECK if not c[3]]
assert worst[2] < 5e-4, worst
print(f'logs agree with the reports: {len(CHECK)} cases, N identical, largest relative difference of the means {worst[2]:.1e}')


def get(cls, *key):
    return R[cls][tuple(str(k) for k in key)]


def ci(r):
    return r['Mean'] - r['Error'], r['Mean'] + r['Error']


def spd(ref, x):
    """speedup of x over ref and conservative interval from the 99.9% CIs"""
    return (ref['Mean'] / x['Mean'], (ref['Mean'] - ref['Error']) / (x['Mean'] + x['Error']),
            (ref['Mean'] + ref['Error']) / (x['Mean'] - x['Error']))


def verdict(a, b):
    """'a' if a is faster than b with disjoint CIs, 'b' if b is, 'overlap' otherwise"""
    if ci(a)[1] < ci(b)[0]:
        return 'a'
    if ci(b)[1] < ci(a)[0]:
        return 'b'
    return 'overlap'


def stats(r):
    keep = ('Mean', 'Error', 'StdDev', 'StdErr', 'Median', 'Min', 'Q1', 'Q3', 'Max', 'N', 'alloc_MB', 'gc_per_1000',
            'launch_means', 'launch_N', 'warmup_per_launch', 'measured_per_launch')
    return {k: r[k] for k in keep}


out = {'cases': {cls: [{'params': r['params'], **stats(r)} for r in R[cls].values()] for cls in CLASSES}}

# ---------------------------------------------------------------- 1. break-even grid
SIZES = [1, 10, 20, 30, 40, 50, 60, 75, 100]
print('\n=== Table 10: full cycle at 1-100 KB, 3 processes x 30 iterations (ms, mean ± 99.9% CI half-width)')
grid = []
for z in SIZES:
    c, i, d = (get('RevBreakEven', z, m) for m in ('CPU-seq', 'iGPU', 'dGPU'))
    row = {'kb': z}
    for name, r in (('cpu', c), ('igpu', i), ('dgpu', d)):
        row[name] = (r['Mean'], r['Error'], r['N'])
        row[name + '_cv'] = 100 * r['StdDev'] / r['Mean']
        row[name + '_launch_dev'] = max(abs(m / r['Mean'] - 1) for m in r['launch_means']) * 100
        row[name + '_alloc'] = r['alloc_MB']
    row['s_igpu'], row['s_dgpu'] = spd(c, i), spd(c, d)
    row['v_igpu'], row['v_dgpu'] = verdict(c, i), verdict(c, d)
    # per process: is the GPU faster than CPU-seq in every one of the three processes of the same index?
    row['proc_igpu'] = [ci_ < cc for ci_, cc in zip(i['launch_means'], c['launch_means'])]
    row['proc_dgpu'] = [cd < cc for cd, cc in zip(d['launch_means'], c['launch_means'])]
    grid.append(row)
    print(f"{z:>4} KB  CPU {c['Mean']:7.3f}±{c['Error']:.3f} (N={c['N']})  iGPU {i['Mean']:7.3f}±{i['Error']:.3f} (N={i['N']})"
          f"  dGPU {d['Mean']:7.3f}±{d['Error']:.3f} (N={d['N']})  S_i={row['s_igpu'][0]:.2f} [{row['s_igpu'][1]:.2f},{row['s_igpu'][2]:.2f}] {row['v_igpu']:>7}"
          f"  S_d={row['s_dgpu'][0]:.2f} [{row['s_dgpu'][1]:.2f},{row['s_dgpu'][2]:.2f}] {row['v_dgpu']:>7}"
          f"  alloc {c['alloc_MB']:.2f}/{i['alloc_MB']:.2f}/{d['alloc_MB']:.2f} MB")
out['grid'] = grid
out['grid_N_range'] = [min(min(r[m][2] for m in ('cpu', 'igpu', 'dgpu')) for r in grid), max(max(r[m][2] for m in ('cpu', 'igpu', 'dgpu')) for r in grid)]
out['grid_cv_range'] = {m: [min(r[m + '_cv'] for r in grid), max(r[m + '_cv'] for r in grid)] for m in ('cpu', 'igpu', 'dgpu')}
out['grid_launch_dev_max'] = {m: max(r[m + '_launch_dev'] for r in grid) for m in ('cpu', 'igpu', 'dgpu')}
print('N range', out['grid_N_range'], ' CV range %', {k: [round(v, 1) for v in w] for k, w in out['grid_cv_range'].items()},
      ' largest deviation of a process mean from the pooled mean %', {k: round(v, 1) for k, v in out['grid_launch_dev_max'].items()})
for g in ('igpu', 'dgpu'):
    last_cpu = max(r['kb'] for r in grid if r['v_' + g] == 'a')
    first_gpu = min(r['kb'] for r in grid if r['v_' + g] == 'b')
    unresolved = [r['kb'] for r in grid if r['v_' + g] == 'overlap']
    above = all(r['v_' + g] == 'b' for r in grid if r['kb'] >= first_gpu)
    # crossing of the mean times by linear interpolation between the neighbouring grid volumes
    cross = None
    for a, b in zip(grid, grid[1:]):
        da, db = a['cpu'][0] - a[g][0], b['cpu'][0] - b[g][0]
        if da < 0 <= db:
            cross = a['kb'] + (b['kb'] - a['kb']) * (-da) / (db - da)
    procs = {r['kb']: r['proc_' + g] for r in grid if last_cpu <= r['kb'] <= first_gpu}
    out[f'bracket_{g}'] = dict(last_cpu=last_cpu, first_gpu=first_gpu, unresolved=unresolved, gpu_faster_above=above, cross=cross, processes=procs)
    print(f'  {g}: CPU-seq faster up to {last_cpu} KB, GPU faster from {first_gpu} KB on (at every larger volume: {above}); '
          f'unresolved {unresolved}; means cross at {cross:.1f} KB; GPU faster per process {procs}')
# fixed part at 1 KB
c1, i1, d1 = (get('RevBreakEven', 1, m) for m in ('CPU-seq', 'iGPU', 'dGPU'))
out['fixed_1KB'] = {'cpu': c1['Mean'], 'igpu': i1['Mean'], 'dgpu': d1['Mean'], 'add_igpu': i1['Mean'] - c1['Mean'], 'add_dgpu': d1['Mean'] - c1['Mean']}
print(f"  1 KB: GPU modes add {i1['Mean']-c1['Mean']:.2f} (iGPU) and {d1['Mean']-c1['Mean']:.2f} ms (dGPU) to CPU-seq; "
      f"GPU times at 1-100 KB: iGPU {min(r['igpu'][0] for r in grid):.2f}-{max(r['igpu'][0] for r in grid):.2f}, dGPU {min(r['dgpu'][0] for r in grid):.2f}-{max(r['dgpu'][0] for r in grid):.2f} ms")
mono = {g: [a['kb'] for a, b in zip(grid, grid[1:]) if b[g][0] < a[g][0]] for g in ('cpu', 'igpu', 'dgpu')}
out['grid_decreases_after'] = mono
print('  volumes after which the mean time decreases:', mono)

# ---------------------------------------------------------------- 2. 100 MB: multithreaded CPU and GPUs
print('\n=== Table 11: full cycle of 100 MB (s); CPU-seq and the original GPU classes from RevChunkSize')
fc = {m: get('RevFullCycle', m) for m in ('CPU-par', 'CPU-par-GF', 'CPU-par-GF-table', 'iGPU', 'iGPU-table', 'dGPU', 'dGPU-table')}
cs = {m: get('RevChunkSize', m) for m in ('CPU-seq', 'iGPU-orig', 'dGPU-orig')}
ref = cs['CPU-seq']
best_cpu = fc['CPU-par-GF-table']
t11 = {}
for name, r in [('CPU-seq', ref)] + list(fc.items()) + [('iGPU (chunk-size class)', cs['iGPU-orig']), ('dGPU (chunk-size class)', cs['dGPU-orig'])]:
    t11[name] = dict(mean=r['Mean'] / 1e3, err=r['Error'] / 1e3, N=r['N'], cv=100 * r['StdDev'] / r['Mean'],
                     s_seq=spd(ref, r), s_best=spd(best_cpu, r))
    print(f"{name:>24}: {r['Mean']/1e3:6.3f}±{r['Error']/1e3:.3f} s (N={r['N']}, CV {100*r['StdDev']/r['Mean']:.1f}%)  "
          f"vs CPU-seq {t11[name]['s_seq'][0]:5.2f} [{t11[name]['s_seq'][1]:.2f},{t11[name]['s_seq'][2]:.2f}]  "
          f"vs CPU-par-GF-table {t11[name]['s_best'][0]:.2f} [{t11[name]['s_best'][1]:.2f},{t11[name]['s_best'][2]:.2f}]")
out['t11'] = t11
for a, b in (('dGPU', 'CPU-par-GF-table'), ('dGPU', 'CPU-par'), ('dGPU-table', 'CPU-par-GF-table'), ('iGPU-table', 'CPU-par-GF-table'),
             ('dGPU', 'dGPU-table'), ('iGPU', 'iGPU-table')):
    print(f'  {a} vs {b}: {verdict(fc[a], fc[b])}')
rep = {g: (cs[g + '-orig']['Mean'] / fc[g]['Mean'] - 1) * 100 for g in ('iGPU', 'dGPU')}
out['repeat_100MB'] = rep
print(f"  original GPU classes measured in two classes (separate processes): difference {rep['iGPU']:+.1f}% (iGPU), {rep['dGPU']:+.1f}% (dGPU)")

# ---------------------------------------------------------------- 3. formation of 10 MB
print('\n=== Table 12: formation of 10 MB (ms)')
fm = {m: get('RevFormation', m) for m in ('CPU-seq', 'CPU-seq-chunk', 'CPU-seq-GF', 'CPU-par', 'CPU-par-GF', 'iGPU', 'dGPU')}
t12 = {}
for name, r in fm.items():
    t12[name] = dict(mean=r['Mean'], err=r['Error'], N=r['N'], per_mb=r['Mean'] / 10, s_ref=spd(fm['CPU-seq'], r))
    print(f"{name:>14}: {r['Mean']:8.1f}±{r['Error']:6.2f} ms (N={r['N']}) = {r['Mean']/10:7.2f} ms/MB   speedup over the reference {t12[name]['s_ref'][0]:6.2f} [{t12[name]['s_ref'][1]:.2f},{t12[name]['s_ref'][2]:.2f}]")
share = (fm['CPU-seq']['Mean'] - fm['CPU-seq-chunk']['Mean']) / fm['CPU-seq']['Mean']
lo = ((fm['CPU-seq']['Mean'] - fm['CPU-seq']['Error']) - (fm['CPU-seq-chunk']['Mean'] + fm['CPU-seq-chunk']['Error'])) / (fm['CPU-seq']['Mean'] - fm['CPU-seq']['Error'])
hi = ((fm['CPU-seq']['Mean'] + fm['CPU-seq']['Error']) - (fm['CPU-seq-chunk']['Mean'] - fm['CPU-seq-chunk']['Error'])) / (fm['CPU-seq']['Mean'] + fm['CPU-seq']['Error'])
calls = 3 * 10 * 2 ** 20
per_call = (fm['CPU-seq']['Mean'] - fm['CPU-seq-chunk']['Mean']) * 1e6 / calls
out['formation'] = dict(t12=t12, call_share=share, call_share_ci=(lo, hi), ns_per_call=per_call,
                        gf_vs_chunk=fm['CPU-seq-GF']['Mean'] / fm['CPU-seq-chunk']['Mean'],
                        dgpu_vs_seqgf=spd(fm['CPU-seq-GF'], fm['dGPU']), igpu_vs_seqgf=spd(fm['CPU-seq-GF'], fm['iGPU']),
                        dgpu_vs_pargf=spd(fm['CPU-par-GF'], fm['dGPU']), dgpu_vs_par=spd(fm['CPU-par'], fm['dGPU']),
                        par_vs_seqchunk=fm['CPU-seq-chunk']['Mean'] / fm['CPU-par']['Mean'],
                        pargf_vs_seqgf=fm['CPU-seq-GF']['Mean'] / fm['CPU-par-GF']['Mean'])
F = out['formation']
print(f"  per-section generator calls: {100*share:.1f}% of the reference formation (conservative {100*lo:.1f}-{100*hi:.1f}%), {per_call:.0f} ns per call;"
      f" GPU filler function / chunk-wise generator on the CPU = {F['gf_vs_chunk']:.2f}")
print(f"  dGPU vs CPU-seq-GF {F['dgpu_vs_seqgf'][0]:.2f} [{F['dgpu_vs_seqgf'][1]:.2f},{F['dgpu_vs_seqgf'][2]:.2f}];"
      f" iGPU vs CPU-seq-GF {F['igpu_vs_seqgf'][0]:.2f} [{F['igpu_vs_seqgf'][1]:.2f},{F['igpu_vs_seqgf'][2]:.2f}];"
      f" dGPU vs CPU-par-GF {F['dgpu_vs_pargf'][0]:.2f} [{F['dgpu_vs_pargf'][1]:.2f},{F['dgpu_vs_pargf'][2]:.2f}];"
      f" all threads: {F['par_vs_seqchunk']:.2f}x (chunk-wise), {F['pargf_vs_seqgf']:.2f}x (GPU filler function)")
trial = R['RevFormation_trial']
dev = {k[0]: (trial[k]['Mean'] / get('RevFormation', *k)['Mean'] - 1) * 100 for k in trial}
out['formation_trial_dev'] = dev
print('  trial run of the class 10 min earlier, difference of the means %:', {k: round(v, 1) for k, v in dev.items()})

# ---------------------------------------------------------------- 4. recovery of 10 MB
print('\n=== Table 13: recovery of 10 MB (ms); the protected file of the GPU modes is formed by the GPU encoder')
rv_modes = ('CPU-seq', 'CPU-seq-table', 'CPU-par', 'CPU-par-table', 'iGPU', 'iGPU-v2', 'iGPU-table', 'iGPU-branchfree',
            'dGPU', 'dGPU-v2', 'dGPU-table', 'dGPU-branchfree')
rv = {m: get('RevRecovery', m) for m in rv_modes}
t13 = {}
for name, r in rv.items():
    t13[name] = dict(mean=r['Mean'], err=r['Error'], N=r['N'], cv=100 * r['StdDev'] / r['Mean'], s_ref=spd(rv['CPU-seq'], r))
    print(f"{name:>16}: {r['Mean']:7.2f}±{r['Error']:6.2f} ms (N={r['N']}, CV {t13[name]['cv']:4.1f}%)  speedup over CPU-seq {t13[name]['s_ref'][0]:5.2f}")
out['recovery'] = dict(t13=t13,
                       table_cpu_seq=spd(rv['CPU-seq'], rv['CPU-seq-table']), table_cpu_par=spd(rv['CPU-par'], rv['CPU-par-table']),
                       table_dgpu=spd(rv['dGPU'], rv['dGPU-table']), table_igpu=spd(rv['iGPU'], rv['iGPU-table']),
                       bf_dgpu=spd(rv['dGPU'], rv['dGPU-branchfree']), bf_igpu=spd(rv['iGPU'], rv['iGPU-branchfree']),
                       v2_dgpu=rv['dGPU-v2']['Mean'] / rv['dGPU']['Mean'] - 1, v2_igpu=rv['iGPU-v2']['Mean'] / rv['iGPU']['Mean'] - 1,
                       dgpu_table_vs_cpu_par_table=spd(rv['CPU-par-table'], rv['dGPU-table']))
for k in ('table_cpu_seq', 'table_cpu_par', 'table_dgpu', 'table_igpu', 'bf_dgpu', 'bf_igpu', 'dgpu_table_vs_cpu_par_table'):
    s = out['recovery'][k]
    print(f'  {k}: {s[0]:.2f} [{s[1]:.2f}, {s[2]:.2f}]')
# stage ratios on D at 10 MB
out['stage_ratio_10MB'] = {m: fm[m]['Mean'] / rv[m]['Mean'] for m in ('CPU-seq', 'iGPU', 'dGPU')}
print('  formation / recovery at 10 MB:', {k: round(v, 2) for k, v in out['stage_ratio_10MB'].items()})

# ---------------------------------------------------------------- 5. recognition of the four inputs
print('\n=== Table 14 (left): recovery of the 10 MB inputs of data/ (ms)')
DATA = ('uniform', 'ff', 'zero', 'ru')
rg_modes = ('CPU-seq', 'CPU-seq-table', 'iGPU', 'iGPU-table', 'iGPU-branchfree', 'dGPU', 'dGPU-table', 'dGPU-branchfree')
rg = {(d, m): get('RevRecognition', d, m) for d in DATA for m in rg_modes}
out['recognition'] = {f'{d}|{m}': dict(mean=r['Mean'], err=r['Error'], N=r['N']) for (d, m), r in rg.items()}
for m in rg_modes:
    print(f'{m:>16}: ' + '  '.join(f"{d} {rg[(d, m)]['Mean']:7.2f}±{rg[(d, m)]['Error']:5.2f}" for d in DATA)
          + f"   uniform/ff {rg[('uniform', m)]['Mean']/rg[('ff', m)]['Mean']:.2f}")

# ---------------------------------------------------------------- 6. kernel-only times
print('\n=== Table 14 (right): kernel-only times, 10 MB in one launch, data resident on the device (ms, mean ± SD of 10)')
K = {}
with open(DIR / 'kernels.csv', encoding='utf-8') as f:
    next(f)
    for line in f:
        p = line.strip().split(',')
        if len(p) == 6:
            dev_, inp, step, rep_, a, b = p
            ms = float(a + '.' + b)
        else:
            dev_, inp, step, rep_, ms = p[0], p[1], p[2], p[3], float(p[4])
        K.setdefault((dev_, inp, step), []).append(ms)
assert all(len(v) == 10 for v in K.values())
KS = {k: dict(mean=mean(v), sd=sd(v), min=min(v), max=max(v)) for k, v in K.items()}
out['kernels'] = {'|'.join(k): v for k, v in KS.items()}
steps = ['formation kernel', 'search kernel', 'table kernel', 'branch-free kernel', 'copy input to device', 'copy protected to host',
         'copy protected to device', 'copy output to host', 'write protected file', 'read protected file']
for dev_ in ('dGPU', 'iGPU'):
    for st in steps:
        print(f'{dev_} {st:>25}: ' + '  '.join(f"{d} {KS[(dev_, d, st)]['mean']:8.2f}±{KS[(dev_, d, st)]['sd']:5.2f}" for d in DATA))
kd = {}
for dev_ in ('dGPU', 'iGPU'):
    km = lambda d, s: KS[(dev_, d, s)]['mean']
    pooled = lambda s: mean([x for d in DATA for x in K[(dev_, d, s)]])
    kd[dev_] = dict(
        search_uniform_over_ff=km('uniform', 'search kernel') / km('ff', 'search kernel'),
        search_over_table_uniform=km('uniform', 'search kernel') / km('uniform', 'table kernel'),
        search_over_table={d: km(d, 'search kernel') / km(d, 'table kernel') for d in DATA},
        table_range=[min(km(d, 'table kernel') for d in DATA), max(km(d, 'table kernel') for d in DATA)],
        bf_range=[min(km(d, 'branch-free kernel') for d in DATA), max(km(d, 'branch-free kernel') for d in DATA)],
        formation_range=[min(km(d, 'formation kernel') for d in DATA), max(km(d, 'formation kernel') for d in DATA)],
        h2d_300MB=pooled('copy protected to device'), d2h_300MB=pooled('copy protected to host'),
        h2d_GBps=300 * 2 ** 20 / pooled('copy protected to device') / 1e6, d2h_GBps=300 * 2 ** 20 / pooled('copy protected to host') / 1e6,
        write_300MB=pooled('write protected file'), read_300MB=pooled('read protected file'),
        write_range=[min(km(d, 'write protected file') for d in DATA), max(km(d, 'write protected file') for d in DATA)],
        read_range=[min(km(d, 'read protected file') for d in DATA), max(km(d, 'read protected file') for d in DATA)],
        formation_kernel_share=mean([km(d, 'formation kernel') for d in DATA]) / fm[dev_]['Mean'],
        search_share_uniform=km('uniform', 'search kernel') / rg[('uniform', dev_)]['Mean'],
        search_share_ru=km('ru', 'search kernel') / rg[('ru', dev_)]['Mean'],
        table_share=mean([km(d, 'table kernel') for d in DATA]) / mean([rg[(d, dev_ + '-table')]['Mean'] for d in DATA]),
        # recovery time minus kernel time, per input and kernel: the part of the stage outside the kernel
        outside={f'{d}|{s}': rg[(d, dev_ + suf)]['Mean'] - km(d, s) for d in DATA
                 for s, suf in (('search kernel', ''), ('table kernel', '-table'), ('branch-free kernel', '-branchfree'))},
        # differences between kernels against differences between the recovery times of the same inputs
        delta_search_table={d: (km(d, 'search kernel') - km(d, 'table kernel'), rg[(d, dev_)]['Mean'] - rg[(d, dev_ + '-table')]['Mean']) for d in DATA},
    )
    k = kd[dev_]
    print(f"  {dev_}: search uniform/ff {k['search_uniform_over_ff']:.2f}; search/table on uniform {k['search_over_table_uniform']:.1f}; "
          f"table {k['table_range'][0]:.2f}-{k['table_range'][1]:.2f}; branch-free {k['bf_range'][0]:.1f}-{k['bf_range'][1]:.1f}; "
          f"formation kernel {k['formation_range'][0]:.1f}-{k['formation_range'][1]:.1f} ms = {100*k['formation_kernel_share']:.1f}% of the formation stage")
    print(f"        300 MB to the device {k['h2d_300MB']:.1f} ms ({k['h2d_GBps']:.1f} GB/s), to the host {k['d2h_300MB']:.1f} ms ({k['d2h_GBps']:.1f} GB/s); "
          f"file write {k['write_range'][0]:.0f}-{k['write_range'][1]:.0f} ms, read {k['read_range'][0]:.0f}-{k['read_range'][1]:.0f} ms")
    print(f"        search kernel share of the recovery: uniform {100*k['search_share_uniform']:.1f}%, Russian text {100*k['search_share_ru']:.1f}%; "
          f"table kernel share {100*k['table_share']:.1f}%")
    print('        kernel difference search - table vs recovery difference (ms):',
          {d: (round(a, 1), round(b, 1)) for d, (a, b) in k['delta_search_table'].items()})
    print('        recovery minus kernel (ms):', {kk: round(v, 1) for kk, v in k['outside'].items()})
out['kernel_derived'] = kd

# ---------------------------------------------------------------- 7. chunk size
print('\n=== chunk size: full cycle of 100 MB (s), one run, CPU-seq as control')
ck = {}
for g in ('iGPU', 'dGPU'):
    base = get('RevChunkSize', f'{g}-v2-512')
    row = {}
    for case in ('orig', 'v2-256', 'v2-512', 'v2-1024', 'v2-2048'):
        r = get('RevChunkSize', f'{g}-{case}')
        row[case] = dict(mean=r['Mean'] / 1e3, err=r['Error'] / 1e3, N=r['N'], rel=r['Mean'] / base['Mean'] - 1, v=verdict(r, base),
                         alloc=r['alloc_MB'])
    ck[g] = row
    print(f'{g}: ' + '  '.join(f"{k} {v['mean']:.3f}±{v['err']:.3f} ({100*v['rel']:+.1f}%, {v['v']}, {v['alloc']:.0f} MB)" for k, v in row.items()))
ck['CPU-seq'] = dict(mean=ref['Mean'] / 1e3, err=ref['Error'] / 1e3, N=ref['N'], cv=100 * ref['StdDev'] / ref['Mean'])
out['chunk'] = ck

json.dump(out, open(out_path('config_D.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False, default=str)
print('\nwritten', out_path('config_D.json').relative_to(ROOT))
