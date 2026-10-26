# -*- coding: utf-8 -*-
"""Configurations B and C: formation variants (Table 7), recognition variants (Table S2), the paired data/key
experiment (Table 8, Figure 4) and the NIST SP 800-22 reports (Table 9), summarized from the raw results.
Writes analysis/output/summary_<config>.json and prints a report.

usage: python3 analysis/summarize.py B|C|<results directory>"""
import json, os, re, math, statistics as stt
import sys

from common import DATA, ROOT, INPUTS, config_dir, out_path
from input_stats import stats as input_stats

R, CFG = config_dir(sys.argv[1] if len(sys.argv) > 1 else 'B')
R = str(R)
OUTP = out_path(f'summary_{CFG}.json')
# mean comparisons per byte, warp level (per section), warp level (per block), computed from the inputs in data/
CMP = {}
for d in INPUTS:
    s = input_stats(DATA / f'{d}_10MB.bin')
    CMP[d] = (round(s['mean'], 3), round(s['warp_section'], 3), round(s['warp_block'], 3))
out = {'cmp': CMP}

# t quantiles for the two-sided 99.9% CI (df -> t_{0.9995})
T9995 = {1: 636.62, 2: 31.599, 3: 12.924, 4: 8.610, 5: 6.869, 6: 5.959, 7: 5.408, 8: 5.041, 9: 4.781, 10: 4.587,
         11: 4.437, 12: 4.318, 13: 4.221, 14: 4.140, 15: 4.073, 16: 4.015, 17: 3.965, 18: 3.922, 19: 3.883, 20: 3.850}

def ci(xs):
    n = len(xs); m = sum(xs) / n; sd = stt.stdev(xs) if n > 1 else 0.0
    return m, sd, T9995[n - 1] * sd / math.sqrt(n), n

# ---------------------------------------------------------------- formation
f = json.load(open(f'{R}/form_en_10MB.json'))
mb = f['bytes'] / 2**20
print(f'=== formation, en 10 MB (configuration {CFG})')
form = {}
ref = None
for r in f['results']:
    mean_ms = r['Mean'] * 1e3; err_ms = r['Err999'] * 1e3
    if ref is None: ref = mean_ms
    form[r['Name']] = dict(mean=mean_ms, err=err_ms, sd=r['SD'] * 1e3, n=r['N'], per_mb=mean_ms / mb, rel=ref / mean_ms,
                           min=r['Min'] * 1e3, max=r['Max'] * 1e3, median=r['Median'] * 1e3)
    print(f"{r['Name']:<50} {mean_ms:9.1f} ± {err_ms:6.1f} ms  ({mean_ms/mb:7.1f} ms/MB)  x{ref/mean_ms:6.1f} vs reference")
out['form'] = form
o = form['form: original, no stream (per-container CSPRNG)']['mean']; b = form['form: bulk CSPRNG (one call per 512 KB)']['mean']
m0 = form['form: masking only (no filler)']['mean']
calls = 3 * f['bytes']
out['form_derived'] = dict(gen_share=(o - b) / form['form: original stream -> file']['mean'], per_call_us=(o - b) / calls * 1e3,
                           bulk_gen_ms=b - m0, bulk_gen_GBps=(f['bytes'] * 30 / 1e9) / ((b - m0) / 1e3))
print('share of the per-section generator calls in the reference formation (to file):', round(100 * out['form_derived']['gen_share'], 1), '%')
print('cost per generator call: %.3f us' % out['form_derived']['per_call_us'])
print('chunk-wise generation (bulk - masking): %.1f ms, %.2f GB/s of filler' % (out['form_derived']['bulk_gen_ms'], out['form_derived']['bulk_gen_GBps']))

# ---------------------------------------------------------------- recognition, unpaired (new key in every iteration)
print(f'\n=== recognition, 10 MB inputs (configuration {CFG}), ms/MB')
rec = {}
for d in INPUTS:
    p = f'{R}/rec_{d}_10MB.json'
    if not os.path.exists(p): continue
    j = json.load(open(p)); mb = j['bytes'] / 2**20
    rec[d] = {r['Name']: dict(mean=r['Mean'] * 1e3 / mb, err=r['Err999'] * 1e3 / mb, sd=r['SD'] * 1e3 / mb, n=r['N'],
                              cv=r['SD'] / r['Mean']) for r in j['results']}
    rec[d]['table construction us'] = j['tableBuild']['Mean'] * 1e6
    line = ' | '.join(f"{k.replace('rec: ','')[:28]}: {v['mean']:6.1f}±{v['err']:5.1f} (cv {100*v['cv']:4.1f}%)" for k, v in rec[d].items() if isinstance(v, dict))
    print(d, line)
out['rec'] = rec

# ---------------------------------------------------------------- paired experiment
p = f'{R}/dist_10MB.json'
if os.path.exists(p):
    rows = json.load(open(p))
    keys = sorted(set(r['key'] for r in rows))
    print(f'\n=== paired experiment: {len(keys)} keys')
    by = {}
    for r in rows:
        by.setdefault(r['input'].split('_')[0], {})[r['key']] = r
    dist = {}
    for d in INPUTS:
        if d not in by: continue
        ks = sorted(by[d])
        s = [by[d][k]['searchMsPerMB'] for k in ks]; t = [by[d][k]['tableMsPerMB'] for k in ks]
        ms, sds, es, n = ci(s); mt, sdt, et, _ = ci(t)
        ratio = [by[d][k]['searchMsPerMB'] / by[d][k]['tableMsPerMB'] for k in ks]
        mr, sdr, er, _ = ci(ratio)
        dist[d] = dict(search=(ms, es, sds, min(s), max(s)), table=(mt, et, sdt, min(t), max(t)), ratio=(mr, er), n=n)
        print(f"{d:8} search {ms:6.1f}±{es:5.1f} (range {min(s):6.1f}-{max(s):6.1f}, cv {100*sds/ms:4.1f}%)   table {mt:5.1f}±{et:4.1f} "
              f"(range {min(t):5.1f}-{max(t):5.1f})   search/table {mr:4.2f}±{er:4.2f}")
    # paired differences relative to uniform
    if 'uniform' in by:
        for d in INPUTS:
            if d not in by or d == 'uniform': continue
            ks = sorted(set(by[d]) & set(by['uniform']))
            rr = [by[d][k]['searchMsPerMB'] / by['uniform'][k]['searchMsPerMB'] for k in ks]
            m, sd, e, n = ci(rr)
            dist[d]['search_vs_uniform'] = (m, e)
            print(f"  search({d}) / search(uniform) = {m:.3f} ± {e:.3f}")
    # key descriptor vs search time (uniform)
    if 'uniform' in by:
        ks = sorted(by['uniform'])
        x = [by['uniform'][k]['bytesPerTriedEtalon'] for k in ks]; y = [by['uniform'][k]['searchMsPerMB'] for k in ks]
        mx, my = sum(x) / len(x), sum(y) / len(y)
        cov = sum((a - mx) * (b - my) for a, b in zip(x, y)); vx = sum((a - mx) ** 2 for a in x); vy = sum((b - my) ** 2 for b in y)
        rcorr = cov / math.sqrt(vx * vy)
        out['key_corr'] = dict(r=rcorr, x_range=(min(x), max(x)), y_range=(min(y), max(y)), n=len(x))
        print(f"  bytes inspected per tried etalon: {min(x):.2f}-{max(x):.2f}; Pearson r with search time (uniform) = {rcorr:.3f}")
        # same for ff
        if 'ff' in by:
            y2 = [by['ff'][k]['searchMsPerMB'] for k in ks if k in by['ff']]
            x2 = [by['uniform'][k]['bytesPerTriedEtalon'] for k in ks if k in by['ff']]
            mx2, my2 = sum(x2) / len(x2), sum(y2) / len(y2)
            r2 = sum((a - mx2) * (b - my2) for a, b in zip(x2, y2)) / math.sqrt(sum((a - mx2) ** 2 for a in x2) * sum((b - my2) ** 2 for b in y2))
            out['key_corr_ff'] = r2
            print(f"  Pearson r with search time (ff) = {r2:.3f}")
    out['dist'] = dist

# ---------------------------------------------------------------- NIST SP 800-22
N = f'{R}/nist'
if os.path.isdir(N):
    nist = {}
    for fn in sorted(os.listdir(N)):
        if not fn.endswith('_finalAnalysisReport.txt'): continue
        label = fn.replace('_finalAnalysisReport.txt', '')
        txt = open(os.path.join(N, fn)).read()
        tests = []
        for line in txt.splitlines():
            m = re.match(r'\s*((?:\d+\s+){10})(\S+)\s+\*?\s*(\d+)/(\d+)\s*\*?\s+(\w+)', line)
            if m:
                pv = m.group(2)
                tests.append(dict(name=m.group(5), p=None if pv.startswith('-') else float(pv), passed=int(m.group(3)), total=int(m.group(4)),
                                  flag='*' in line))
        nist[label] = tests
        nm = sorted(set(t['name'] for t in tests))
        flagged = [t for t in tests if t['flag']]
        ps = [t['p'] for t in tests if t['p'] is not None]
        prop = [t['passed'] / t['total'] for t in tests]
        print(f"\n=== NIST {label}: {len(tests)} p-values over {len(nm)} tests; flagged: {len(flagged)}; min uniformity P = {min(ps):.6f}; "
              f"min proportion = {min(prop):.3f}")
        for t in flagged: print('   flagged:', t)
    out['nist'] = nist

json.dump(out, open(OUTP, 'w'), indent=1)
print('\nwritten', OUTP.relative_to(ROOT))
