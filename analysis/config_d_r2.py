"""Second run on configuration D (revision 2; results/configuration_D_r2): every benchmark in three processes, the High
performance power plan for all runs, the clock rates of the discrete GPU logged, optimized CPU and GPU paths.

Reads the BenchmarkDotNet reports and logs of the classes Rev2BreakEven, Rev2Formation, Rev2Recovery, Rev2FullCycle and
Rev2FullCycleSeq, the kernel-only times kernels2.csv and the clock log, and writes output/config_D_r2.json and a text
report to the standard output. For every case: mean and half-width of the 99.9% CI over the pooled iterations (as in the
report), the means of the three processes and the half-width of the 99.9% CI with the process as the unit of replication
(Student's t with two degrees of freedom). Ratios are bounded conservatively from the intervals of the two times.
Usage: python3 analysis/config_d_r2.py"""
import csv, collections, datetime, json, math, os, re, statistics as st, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from tdist import t_quantile

DIR = os.path.join(HERE, '..', 'results', 'configuration_D_r2')
TO_MS = {'ns': 1e-6, 'μs': 1e-3, 'us': 1e-3, 'ms': 1.0, 's': 1e3}
TO_MB = {'B': 2.0 ** -20, 'KB': 2.0 ** -10, 'MB': 1.0, 'GB': 2.0 ** 10}
BENCH = re.compile(r"^// Benchmark: (\w+)\.'?[^:]*'?: (\w+)\(.*\) \[(.*)\]\s*$")
LAUNCH = re.compile(r'^// Launch: (\d+) / (\d+)')
ITER = re.compile(r'^(WorkloadResult|WorkloadActual|WorkloadWarmup)\s+\d+: 1 op, ([\d.]+) ns')


def quantity(text, table):
    t = text.strip().strip('"')
    if t in ('', '-', 'NA', '?'):
        return None
    v, u = t.split()
    return float(v.replace(',', '')) * table[u]


def read_report(cls):
    with open(os.path.join(DIR, cls, 'results', f'{cls}-report.csv'), encoding='utf-8-sig', newline='') as f:
        rows = list(csv.reader(f, delimiter=';'))
    head = rows[0]
    names = head[head.index('WarmupCount') + 1:head.index('Mean')]
    out = {}
    for r in rows[1:]:
        d = dict(zip(head, r))
        key = tuple(d[n] for n in names)
        out[key] = dict(report_mean=quantity(d['Mean'], TO_MS), report_error=quantity(d['Error'], TO_MS),
                        N=int(round(float(d['Iterations']))), alloc_MB=quantity(d['Allocated'], TO_MB))
    return names, out


def read_log(cls):
    logs = sorted(f for f in os.listdir(os.path.join(DIR, cls)) if f.endswith('.log'))
    runs, cur = {}, None
    for line in open(os.path.join(DIR, cls, logs[-1]), encoding='utf-8', errors='replace'):
        m = BENCH.match(line.rstrip('\r\n'))
        if m:
            cur = runs.setdefault(tuple(p.split('=', 1)[1] for p in m.group(3).split(', ')), [])
            continue
        if LAUNCH.match(line) and cur is not None:
            cur.append([])
            continue
        m = ITER.match(line)
        if m and cur and m.group(1) == 'WorkloadResult':
            cur[-1].append(float(m.group(2)) * 1e-6)
    return runs


T2 = t_quantile(0.9995, 2)
R, CHECK = {}, []
for cls in ('Rev2BreakEven', 'Rev2Formation', 'Rev2Recovery', 'Rev2FullCycle', 'Rev2FullCycleSeq'):
    names, rep = read_report(cls)
    log = read_log(cls)
    R[cls] = {}
    for key, rec in rep.items():
        launches = log[key]
        res = [x for l in launches for x in l]
        n = len(res)
        mean, sd = st.mean(res), st.stdev(res)
        lm = [st.mean(l) for l in launches]
        rec.update(mean=mean, sd=sd, hw=t_quantile(0.9995, n - 1) * sd / math.sqrt(n), n=n, launch_means=lm,
                   launch_n=[len(l) for l in launches],
                   hw_process=T2 * st.stdev(lm) / math.sqrt(len(lm)), launch_dev=100 * max(abs(x / mean - 1) for x in lm))
        CHECK.append((cls, key, n == rec['N'], abs(mean / rec['report_mean'] - 1), abs(rec['hw'] / rec['report_error'] - 1), len(launches)))
        R[cls][key] = rec
assert all(c[2] and c[5] == 3 for c in CHECK), [c for c in CHECK if not (c[2] and c[5] == 3)]
print(f'{len(CHECK)} cases, three processes each; N as in the reports; largest relative difference between the log and the '
      f'report: mean {max(c[3] for c in CHECK):.1e}, CI half-width {max(c[4] for c in CHECK):.1e}')


def ratio(a, b):
    """time a / time b with the conservative interval (Section 6.4)"""
    return dict(value=a['mean'] / b['mean'], lo=(a['mean'] - a['hw']) / (b['mean'] + b['hw']), hi=(a['mean'] + a['hw']) / (b['mean'] - b['hw']))


def verdict(a, b):
    """'a' if a is faster than b with non-overlapping 99.9% CIs, 'b' if slower, '=' otherwise"""
    if a['mean'] + a['hw'] < b['mean'] - b['hw']:
        return 'a'
    if b['mean'] + b['hw'] < a['mean'] - a['hw']:
        return 'b'
    return '='


OUT = {}
# ------------------------------------------------------------------ grid 1-1024 KB
G = R['Rev2BreakEven']
sizes = sorted({int(k[0]) for k in G})
modes = ['CPU-seq', 'CPU-opt', 'iGPU', 'iGPU-opt', 'dGPU', 'dGPU-opt']
grid = {s: {m: G[(str(s), m)] for m in modes} for s in sizes}
OUT['grid'] = {s: {m: {k: v[k] for k in ('mean', 'hw', 'n', 'launch_means', 'hw_process', 'launch_dev', 'alloc_MB')} for m, v in row.items()} for s, row in grid.items()}
brackets = {}
for ref in ('CPU-seq', 'CPU-opt'):
    for g in ('iGPU', 'iGPU-opt', 'dGPU', 'dGPU-opt'):
        state = {s: verdict(grid[s][g], grid[s][ref]) for s in sizes}                 # 'a': the GPU mode is faster
        per_process = {s: [x < y for x, y in zip(grid[s][g]['launch_means'], grid[s][ref]['launch_means'])] for s in sizes}
        last_ref = max([s for s in sizes if state[s] == 'b'], default=None)
        first_gpu = min([s for s in sizes if all(state[w] == 'a' for w in sizes if w >= s)], default=None)
        brackets[f'{g} vs {ref}'] = dict(state=state, per_process=per_process, last_ref_faster=last_ref, first_gpu_faster=first_gpu,
                                         ratio={s: ratio(grid[s][g], grid[s][ref]) for s in sizes})
OUT['brackets'] = brackets
OUT['fixed_1KB'] = {m: grid[1][m]['mean'] for m in modes}
OUT['grid_launch_dev_max'] = max(v['launch_dev'] for row in grid.values() for v in row.values())
OUT['grid_hw_process_pct'] = [min(100 * v['hw_process'] / v['mean'] for row in grid.values() for v in row.values()),
                              max(100 * v['hw_process'] / v['mean'] for row in grid.values() for v in row.values())]

# ------------------------------------------------------------------ stages of 10 MB and the full cycle of 100 MB
def table(cls, ref, seq=None):
    rows = {}
    for key, rec in R[cls].items():
        m = key[-1]
        rows[m] = {k: rec[k] for k in ('mean', 'hw', 'n', 'launch_means', 'hw_process', 'launch_dev', 'alloc_MB')}
        rows[m]['rel'] = ratio(rec, R[cls][(ref,)])
        if seq is not None:
            rows[m]['speedup'] = ratio(seq, rec)
        rows[m]['vs_ref'] = verdict(rec, R[cls][(ref,)])
    return rows


SEQ100 = R['Rev2FullCycleSeq'][('CPU-seq',)]
OUT['formation'] = table('Rev2Formation', 'CPU-opt', R['Rev2Formation'][('CPU-seq',)])
OUT['recovery'] = table('Rev2Recovery', 'CPU-opt', R['Rev2Recovery'][('CPU-seq',)])
OUT['fullcycle'] = table('Rev2FullCycle', 'CPU-opt', SEQ100)
OUT['fullcycle']['CPU-seq'] = {k: SEQ100[k] for k in ('mean', 'hw', 'n', 'launch_means', 'hw_process', 'launch_dev', 'alloc_MB')}
OUT['fullcycle']['CPU-seq']['rel'] = ratio(SEQ100, R['Rev2FullCycle'][('CPU-opt',)])
FC = R['Rev2FullCycle']
pairs = {}
for a, b in (('dGPU-ovl', 'CPU-opt-ovl'), ('dGPU-ovl', 'CPU-opt'), ('dGPU-opt', 'CPU-opt'), ('iGPU-ovl', 'CPU-opt-ovl'), ('iGPU-ovl', 'CPU-opt'), ('iGPU-opt', 'CPU-opt'),
             ('CPU-par-GF-table', 'CPU-opt'), ('CPU-par', 'CPU-opt'), ('dGPU', 'dGPU-opt'), ('dGPU', 'dGPU-ovl'), ('iGPU', 'iGPU-opt'), ('iGPU', 'iGPU-ovl'),
             ('dGPU', 'dGPU-table'), ('iGPU', 'iGPU-table'), ('dGPU', 'CPU-par-GF-table'), ('dGPU', 'CPU-opt'), ('iGPU', 'CPU-opt'), ('CPU-opt', 'CPU-opt-ovl')):
    pairs[f'{a} / {b}'] = dict(ratio(FC[(a,)], FC[(b,)]), verdict=verdict(FC[(a,)], FC[(b,)]))
OUT['fullcycle_pairs'] = pairs
stage_pairs = {}
for cls, tag in (('Rev2Formation', 'formation'), ('Rev2Recovery', 'recovery')):
    S = R[cls]
    for a, b in (('dGPU-ovl', 'CPU-opt-ovl'), ('dGPU-opt', 'CPU-opt'), ('iGPU-ovl', 'CPU-opt-ovl'), ('iGPU-opt', 'CPU-opt'), ('dGPU', 'dGPU-opt'), ('dGPU', 'dGPU-ovl'),
                 ('iGPU', 'iGPU-opt'), ('iGPU', 'iGPU-ovl'), ('CPU-opt', 'CPU-opt-ovl')) + ((('CPU-par', 'CPU-opt'),) if tag == 'formation' else (('CPU-par-table', 'CPU-opt'),)):
        stage_pairs[f'{tag}: {a} / {b}'] = dict(ratio(S[(a,)], S[(b,)]), verdict=verdict(S[(a,)], S[(b,)]))
OUT['stage_pairs'] = stage_pairs
allc = [v for cls in ('Rev2Formation', 'Rev2Recovery', 'Rev2FullCycle', 'Rev2FullCycleSeq') for v in R[cls].values()]
OUT['stages_launch_dev_max'] = max(v['launch_dev'] for v in allc)
OUT['stages_hw_process_pct'] = [min(100 * v['hw_process'] / v['mean'] for v in allc), max(100 * v['hw_process'] / v['mean'] for v in allc)]

# ------------------------------------------------------------------ kernel-only times: group sizes, coalesced formation kernel
K = collections.defaultdict(list)
for r in csv.DictReader(open(os.path.join(DIR, 'kernels2.csv'))):
    K[(r['device'], r['input'], int(r['group']), r['step'])].append(float(r['ms']))
med = {k: st.median(v) for k, v in K.items()}
kern = {}
for dev in ('iGPU', 'dGPU'):
    inputs = sorted({k[1] for k in K if k[0] == dev})
    groups = sorted({k[2] for k in K if k[0] == dev})
    co = {i: [b / a for a, b in zip(K[(dev, i, 0, 'coalesced formation kernel')], K[(dev, i, 0, 'formation kernel')])] for i in inputs}
    d = dict(groups=groups, coalesced_speedup={i: dict(mean=st.mean(v), min=min(v), max=max(v)) for i, v in co.items()},
             median={f'{i}|{g}|{s}': med[(dev, i, g, s)] for (dv, i, g, s) in K if dv == dev})
    # data-independent kernels: best explicit group size against the automatic one, median times
    gs = {}
    for s in ('formation kernel', 'coalesced formation kernel', 'table kernel', 'branch-free kernel'):
        rel = [med[(dev, i, g, s)] / med[(dev, i, 0, s)] for i in inputs for g in groups if g]
        gs[s] = dict(min=min(rel), max=max(rel))
    d['group_rel_to_auto'] = gs
    su = [med[(dev, 'uniform', g, 'search kernel')] / med[(dev, 'uniform', 0, 'search kernel')] for g in groups if g]
    d['search_uniform_rel_to_auto'] = dict(min=min(su), max=max(su))
    d['rep_range_pct_max'] = max(100 * (max(v) - min(v)) / st.median(v) for k, v in K.items() if k[0] == dev)
    kern[dev] = d
OUT['kernels'] = kern

# ------------------------------------------------------------------ clock log of the discrete GPU
rows = []
for f in sorted(os.listdir(DIR)):
    if not (f.startswith('gpu_clocks_all') and f.endswith('.csv')):
        continue
    for r in csv.reader(open(os.path.join(DIR, f), encoding='utf-8', errors='replace')):
        try:
            rows.append((datetime.datetime.strptime(r[0].strip(), '%Y/%m/%d %H:%M:%S.%f'), r[2].strip(), int(r[3].split()[0]), int(r[5].split()[0]),
                         int(r[7]), int(r[8].split()[0])))
        except (ValueError, IndexError):
            pass
gaps = [(a[0].isoformat(), b[0].isoformat(), (b[0] - a[0]).total_seconds()) for a, b in zip(rows, rows[1:]) if (b[0] - a[0]).total_seconds() > 5]
busy = [r for r in rows if r[5] >= 20]
cl = collections.Counter(r[2] for r in busy)
OUT['clocks'] = dict(samples=len(rows), first=rows[0][0].isoformat(), last=rows[-1][0].isoformat(), gaps=gaps, busy_samples=len(busy),
                     busy_clock_min=min(r[2] for r in busy), busy_clock_max=max(r[2] for r in busy), busy_clock_top=cl.most_common(3),
                     busy_share_max_clock=cl[max(cl)] / len(busy), busy_share_1455=cl.get(1455, 0) / len(busy),
                     pstates_busy=dict(collections.Counter(r[1] for r in busy)), idle_clock=collections.Counter(r[2] for r in rows if r[1] == 'P8').most_common(1)[0][0],
                     temp_min=min(r[4] for r in rows), temp_max=max(r[4] for r in rows))

os.makedirs(os.path.join(HERE, 'output'), exist_ok=True)
json.dump(OUT, open(os.path.join(HERE, 'output', 'config_D_r2.json'), 'w'), indent=1, default=str)

# ------------------------------------------------------------------ text report
print('\n=== full cycle, ms: mean +- 99.9% CI over pooled iterations | process-level half-width | means of the three processes')
for s in sizes:
    print(f'{s:5d} KB  ' + '  '.join(f'{m} {grid[s][m]["mean"]:.3f}+-{grid[s][m]["hw"]:.3f}' for m in modes))
for name, b in brackets.items():
    print(f'  {name:20s} reference faster up to {b["last_ref_faster"]} KB, GPU mode faster from {b["first_gpu_faster"]} KB; states {b["state"]}')
print(f'  process means deviate from the pooled mean by at most {OUT["grid_launch_dev_max"]:.1f}%; process-level half-widths {OUT["grid_hw_process_pct"][0]:.1f}-{OUT["grid_hw_process_pct"][1]:.1f}% of the mean')
for tag in ('formation', 'recovery', 'fullcycle'):
    print(f'=== {tag}: mean +- CI (ms), N, relative to CPU-opt, speedup over CPU-seq, process means')
    for m, r in sorted(OUT[tag].items(), key=lambda kv: kv[1]['mean']):
        sp = f'  S {r["speedup"]["value"]:.2f} [{r["speedup"]["lo"]:.2f}, {r["speedup"]["hi"]:.2f}]' if 'speedup' in r else ''
        print(f'  {m:17s} {r["mean"]:9.1f} +- {r["hw"]:6.1f}  N={r["n"]:2d}  rel {r["rel"]["value"]:.2f} [{r["rel"]["lo"]:.2f}, {r["rel"]["hi"]:.2f}]{sp}'
              f'  processes {", ".join("%.1f" % x for x in r["launch_means"])} (+-{r["hw_process"]:.1f})')
print('=== pairs, full cycle of 100 MB (time of the first / time of the second)')
for n, p in pairs.items():
    print(f'  {n:30s} {p["value"]:.2f} [{p["lo"]:.2f}, {p["hi"]:.2f}] {p["verdict"]}')
print('=== pairs, stages of 10 MB')
for n, p in stage_pairs.items():
    print(f'  {n:38s} {p["value"]:.2f} [{p["lo"]:.2f}, {p["hi"]:.2f}] {p["verdict"]}')
print(f'  stages: process means deviate by at most {OUT["stages_launch_dev_max"]:.1f}%; process-level half-widths {OUT["stages_hw_process_pct"][0]:.1f}-{OUT["stages_hw_process_pct"][1]:.1f}%')
for dev, d in kern.items():
    print(f'=== kernels, {dev}: coalesced formation kernel, speedup over the original (automatic group size):',
          {i: f'{v["mean"]:.2f} ({v["min"]:.2f}-{v["max"]:.2f})' for i, v in d['coalesced_speedup'].items()})
    print('   explicit group sizes relative to automatic (median times, data-independent kernels):', {s: f'{v["min"]:.2f}-{v["max"]:.2f}' for s, v in d['group_rel_to_auto'].items()},
          '; search kernel, uniform:', f'{d["search_uniform_rel_to_auto"]["min"]:.2f}-{d["search_uniform_rel_to_auto"]["max"]:.2f}', f'; largest range over repetitions {d["rep_range_pct_max"]:.0f}%')
print('=== clocks:', OUT['clocks'])
