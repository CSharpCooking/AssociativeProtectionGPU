"""Revision 2: summary of the optimized CPU path (CPU-opt) against the CPU paths of configuration D, measured on
configuration B in independent processes (measurement/run_opt_linux.sh, Bench/OptBench.cs).

For every group and variant: the means of the individual processes, their mean, the SD between the processes and the
half-width of the 99.9% CI with the process as the unit of replication (Student's t with k-1 degrees of freedom), and,
for comparison, the 99.9% CI over the pooled iterations of all processes as BenchmarkDotNet computes it.
Ratios between variants are computed in every process and summarized over the processes (paired design).
Usage: python3 opt_summary.py [results folder] [output json]"""
import glob, json, math, os, statistics as st, sys

from tdist import t_quantile   # Student's t quantile without SciPy

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', 'results', 'opt_B')
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, 'output', 'opt_B.json')


def t999(df):
    return t_quantile(0.9995, df)


files = sorted(glob.glob(os.path.join(SRC, 'opt_p*.json')))
runs = [json.load(open(f)) for f in files]
k = len(runs)
groups = {}
for r in runs:
    for g in r['groups']:
        for s in g['results']:
            groups.setdefault(g['group'], {}).setdefault(s['Name'], []).append(s)

summary = {'processes': k, 'threads': runs[0]['threads'], 'groups': {}}
for gname, variants in groups.items():
    gs = {}
    for vname, stats in variants.items():
        pm = [s['Mean'] * 1e3 for s in stats]                    # ms, one value per process
        raw = [x * 1e3 for s in stats for x in s['Raw']]
        mean = st.mean(pm)
        sd_b = st.stdev(pm) if len(pm) > 1 else float('nan')
        hw_p = t999(len(pm) - 1) * sd_b / math.sqrt(len(pm)) if len(pm) > 1 else float('nan')
        sd_i = st.stdev(raw)
        hw_i = t999(len(raw) - 1) * sd_i / math.sqrt(len(raw))
        within_cv = [100 * s['SD'] / s['Mean'] for s in stats]
        gs[vname] = dict(process_means=pm, mean=mean, sd_between=sd_b, cv_between=100 * sd_b / mean,
                         hw_process=hw_p, hw_process_pct=100 * hw_p / mean,
                         pooled_mean=st.mean(raw), hw_pooled=hw_i, n_pooled=len(raw),
                         cv_within_range=[min(within_cv), max(within_cv)],
                         per_mb=mean / (100 if '100 MB' in gname else 10))
    summary['groups'][gname] = gs


def ratio(g, a, b):
    """time(b) / time(a), computed in every process (both variants ran in each process); mean of the process ratios with the
    half-width of its 99.9% CI over the processes (Student's t with k-1 degrees of freedom) and the range over the processes."""
    A, B = summary['groups'][g][a]['process_means'], summary['groups'][g][b]['process_means']
    r = [y / x for x, y in zip(A, B)]
    m = st.mean(r)
    h = t999(len(r) - 1) * st.stdev(r) / math.sqrt(len(r))
    return dict(value=m, lo=m - h, hi=m + h, min=min(r), max=max(r), per_process=r)


R = {}
for g, base in (('formation 10 MB', ['CPU-par', 'CPU-par-GF', 'CPU-seq-chunk', 'CPU-opt, 1 thread']),
                ('recovery 10 MB', ['CPU-par', 'CPU-par-table', 'CPU-seq-table', 'CPU-opt, 1 thread']),
                ('full cycle 10 MB', ['CPU-par', 'CPU-par-GF-table']),
                ('full cycle 100 MB', ['CPU-par', 'CPU-par-GF-table'])):
    if g not in summary['groups']:
        continue
    for b in base:
        R[f'{g}: CPU-opt vs {b}'] = ratio(g, 'CPU-opt', b)
if 'formation 10 MB' in summary['groups']:
    R['formation 10 MB: SIMD masking, memory'] = ratio('formation 10 MB', 'CPU-opt, memory', 'CPU-par logic, memory')
    R['formation 10 MB: CPU-opt 1 thread vs CPU-seq-chunk'] = ratio('formation 10 MB', 'CPU-opt, 1 thread', 'CPU-seq-chunk')
if 'recovery 10 MB' in summary['groups']:
    R['recovery 10 MB: byte-wise tables, memory'] = ratio('recovery 10 MB', 'CPU-opt, memory', 'CPU-par-table logic, memory')
    R['recovery 10 MB: CPU-opt 1 thread vs CPU-seq-table'] = ratio('recovery 10 MB', 'CPU-opt, 1 thread', 'CPU-seq-table')
summary['ratios'] = R
# file share of CPU-opt: file-based minus in-memory time, relative to the file-based time
G = summary['groups']
if 'formation 10 MB' in G and 'recovery 10 MB' in G:
    summary['file_share'] = {
        'formation': 1 - G['formation 10 MB']['CPU-opt, memory']['mean'] / G['formation 10 MB']['CPU-opt']['mean'],
        'recovery': 1 - G['recovery 10 MB']['CPU-opt, memory']['mean'] / G['recovery 10 MB']['CPU-opt']['mean'],
        'generation_share_of_memory_formation': G['formation 10 MB']['filler generation only, memory']['mean'] / G['formation 10 MB']['CPU-opt, memory']['mean'],
    }

os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(summary, open(OUT, 'w'), indent=1)
for g, gs in summary['groups'].items():
    print(f'=== {g} ({k} processes, {summary["threads"]} threads)')
    for v, s in gs.items():
        print(f'  {v:34s} {s["mean"]:9.1f} ms  +-{s["hw_process"]:7.1f} (process-level 99.9%, {s["hw_process_pct"]:4.1f}%)'
              f'  +-{s["hw_pooled"]:6.1f} (pooled, N={s["n_pooled"]})  between-process CV {s["cv_between"]:4.1f}%'
              f'  process means {", ".join("%.1f" % x for x in s["process_means"])}')
print('=== ratios (time of the second / time of the first), per process; mean, 99.9% CI over the processes, range')
for n, r in R.items():
    print(f'  {n:60s} {r["value"]:5.2f} [{r["lo"]:.2f}, {r["hi"]:.2f}]  processes {r["min"]:.2f}-{r["max"]:.2f}')
if 'file_share' in summary:
    print('file share:', {a: round(100 * b, 1) for a, b in summary['file_share'].items()})
