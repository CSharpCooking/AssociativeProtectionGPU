# -*- coding: utf-8 -*-
"""Prints the rows of Tables 4-15 of the paper and the derived values quoted in its text, computed from
analysis/output/config_A.json, summary_B.json, summary_C.json and config_D.json (run config_a.py, summarize.py and
config_d.py first). Writes analysis/output/paper_values.md.

usage: python3 analysis/paper_values.py"""
import json
import os
import re
from decimal import Decimal, ROUND_HALF_UP

from common import ROOT, RESULTS, OUT, out_path

D = json.load(open(OUT / 'config_A.json'))
DD = json.load(open(OUT / 'config_D.json'))
B = json.load(open(OUT / 'summary_B.json'))
C = json.load(open(OUT / 'summary_C.json'))
lines = []


def emit(s=''):
    lines.append(s)
    print(s)


def rnd(x, nd):
    # round half up on the decimal value, as in the paper (binary noise such as 10.014999999999999 must not decide)
    q = Decimal(f'{x:.12g}').quantize(Decimal(1).scaleb(-nd), rounding=ROUND_HALF_UP)
    return f'{q:,.{nd}f}'


def pm(m, e, nd):
    return f'{rnd(m, nd)} ± {rnd(e, nd)}'


def sint(s):
    return f'{s[0]:.2f} [{s[1]:.2f}, {s[2]:.2f}]'


def table(title, header, rows):
    emit(f'### {title}')
    emit()
    emit('| ' + ' | '.join(header) + ' |')
    emit('|' + '---|' * len(header))
    for r in rows:
        emit('| ' + ' | '.join(str(x) for x in r) + ' |')
    emit()


LBL = {'1KB': '1 KB', '10KB': '10 KB', '100KB': '100 KB', '1MB': '1 MB', '3.45MB': '3.45 MB', '10MB': '10 MB', '100MB': '100 MB'}

# ---------------------------------------------------------------- Table 4
rows = []
for r in D['table4']:
    c, i, d = r['cpu'], r['igpu'], r['dgpu']
    nd = 2 if c[0] < 100 else (1 if c[0] < 1000 else 0)
    ndg = 2 if d[0] < 100 else (1 if d[0] < 1000 else 0)
    rows.append([LBL[r['size']], pm(c[0], c[1], nd), pm(i[0], i[1], ndg), pm(d[0], d[1], ndg), sint(r['s_igpu']), sint(r['s_dgpu'])])
table('Table 4. Full cycle on configuration A, 512 KB chunk (ms, mean ± half-width of the 99.9% CI; speedup with conservative interval)',
      ['Volume', 'CPU-seq', 'iGPU', 'dGPU', 'S iGPU', 'S dGPU'], rows)

# ---------------------------------------------------------------- Table 5
fx, mg = D['fixed'], D['marginal']
f1 = lambda k, nd=2: pm(fx[k][0], fx[k][1], nd)
mgs = lambda k, nd=1: pm(mg[k][0], mg[k][1], nd)
alloc = {n: float(fx[f'{n} full'][2].split()[0]) for n in ('CPU', 'iGPU', 'dGPU')}
gc2 = {n: (0 if not fx[f'{n} full'][3] or fx[f'{n} full'][3][2] in ('', None) else round(float(fx[f'{n} full'][3][2]) / 1000)) for n in ('CPU', 'iGPU', 'dGPU')}
rows = [['Formation at 1 KB, ms', f1('CPU enc', 3), f1('iGPU enc'), f1('dGPU enc')],
        ['Recovery at 1 KB, ms', f1('CPU dec'), f1('iGPU dec'), f1('dGPU dec')],
        ['Full cycle at 1 KB, ms', f1('CPU full'), f1('iGPU full'), f1('dGPU full')],
        ['Managed memory allocated per full cycle at 1 KB, MB', f"{alloc['CPU']:.1f}", f"{alloc['iGPU']:.1f}", f"{alloc['dGPU']:.1f}"],
        ['Generation-2 garbage collections per full cycle at 1 KB', gc2['CPU'], gc2['iGPU'], gc2['dGPU']],
        ['Marginal time of formation, ms/MB', mgs('CPU enc'), mgs('iGPU enc'), mgs('dGPU enc')],
        ['Marginal time of recovery, ms/MB', mgs('CPU dec'), mgs('iGPU dec'), mgs('dGPU dec')],
        ['Marginal time of the full cycle, ms/MB', mgs('CPU full'), mgs('iGPU full'), mgs('dGPU full')]]
table('Table 5. Fixed and marginal components on configuration A (marginal: between 10 and 100 MB)', ['Quantity', 'CPU-seq', 'iGPU', 'dGPU'], rows)
al = D['alloc']
emit('Allocation experiment (1 KB, chunk 512 -> 1024 KB): ' + '; '.join(
    f"{g}: +{al[g]['dalloc']:.2f} MB allocated -> {al[g]['dt']:+.2f} ms [{al[g]['lo']:.2f}, {al[g]['hi']:.2f}], {al[g]['per_mb']:.4f} ms/MB, "
    f"about {al[g]['per_mb'] * al[g]['alloc512']:.2f} ms of the {al[g]['alloc512']:.2f} MB allocated at 512 KB" for g in ('iGPU', 'dGPU')))
emit(f"Crossing points with CPU-seq (OLS on 1, 10, 100 KB): iGPU {D['cross_iGPU']:.1f} KB, dGPU {D['cross_dGPU']:.1f} KB; "
     f"two-point model: {D['cross2_iGPU']:.1f} / {D['cross2_dGPU']:.1f} KB; interpolation between 10 and 100 KB: {D['cross3_iGPU']:.1f} / {D['cross3_dGPU']:.1f} KB")
emit()

# ---------------------------------------------------------------- Table 6
t6 = D['table6']
rows = []
for op, name in (('full', 'Full cycle'), ('formation', 'Formation'), ('recovery', 'Recovery')):
    r = t6[op]
    rows.append([name, pm(r['cpu'][0], r['cpu'][1], 3), pm(r['igpu'][0], r['igpu'][1], 3), pm(r['dgpu'][0], r['dgpu'][1], 3), sint(r['s_igpu']), sint(r['s_dgpu'])])
table('Table 6. Stages at 100 MB on configuration A (s)', ['Operation', 'CPU-seq', 'iGPU', 'dGPU', 'S iGPU', 'S dGPU'], rows)
emit('Formation / recovery: ' + ', '.join(f"{v} {t6['formation'][v][0] / t6['recovery'][v][0]:.2f}" for v in ('cpu', 'igpu', 'dgpu')))
emit()

# ---------------------------------------------------------------- Section 7.4: stages are separate benchmarks
ORIG = json.load(open(RESULTS / 'configuration_A' / 'bdn_statistics.json', encoding='utf-8'))
stat = lambda z, n: next(x for x in ORIG if x['series'] == '512' and x['size'] == z and x['name'] == n)
rows = []
for z in ('1KB', '10KB', '100KB', '1MB', '3.45MB', '10MB', '100MB'):
    row = [LBL[z]]
    for m in ('CPU', 'iGPU', 'dGPU'):
        f, r, c = stat(z, m + ' enc'), stat(z, m + ' dec'), stat(z, m + ' full')
        dev = (f['Mean'] + r['Mean'] - c['Mean']) * 1e3
        ci = (f['Error'] + r['Error'] + c['Error']) * 1e3
        row.append(f"{dev:+.2f} ms ({100 * dev / (c['Mean'] * 1e3):+.1f}%){'' if abs(dev) <= ci else ' *'}")
    rows.append(row)
table('Section 7.4. Sum of the formation and recovery means minus the full-cycle mean (* = beyond the sum of the three CI half-widths)',
      ['Volume', 'CPU-seq', 'iGPU', 'dGPU'], rows)


# ---------------------------------------------------------------- Section 6.4 and captions of Tables 4-6
S512 = [x for x in ORIG if x['series'] == '512']
OPS = {'full': 'full cycle', 'enc': 'formation', 'dec': 'recovery'}
cv = lambda x: 100 * x['StdDev'] / x['Mean']
rows = []
for op, name in OPS.items():
    sub = [x for x in S512 if x['name'].endswith(' ' + op)]
    rows.append([name, f"{min(x['N'] for x in sub)}-{max(x['N'] for x in sub)}", f"{rnd(min(map(cv, sub)), 1)}-{rnd(max(map(cv, sub)), 1)}%"])
table('Section 6.4. 512 KB series: measured iterations N after outlier removal and coefficient of variation SD/mean', ['Operation', 'N', 'CV'], rows)
nrange = lambda sub: f"{min(x['N'] for x in sub)}-{max(x['N'] for x in sub)}"
emit('Captions: N of Table 4 ' + nrange([x for x in S512 if x['name'].endswith(' full')])
     + '; of Table 5 ' + nrange([x for x in S512 if x['size'] in ('1KB', '10MB', '100MB')])
     + '; of Table 6 ' + nrange([x for x in S512 if x['size'] == '100MB']))
emit()

# ---------------------------------------------------------------- Table 7
def formrow(F, prefix):
    for k, v in F.items():
        if k.startswith(prefix):
            return v
    raise KeyError(prefix)


order = [('form: original stream -> file', 'Reference, stream to a file'),
         ('form: original stream -> Stream.Null', 'Reference, stream to a null stream'),
         ('form: original, no stream', 'Reference without the stream wrapper'),
         ('form: bulk CSPRNG (one call per 512 KB)', 'One generator call per chunk'),
         ('form: GPU filler function on CPU', 'Filler function of the GPU kernels'),
         ('form: masking only', 'Masking only, no filler generation'),
         ('form: bulk CSPRNG, ', 'One generator call per range, all threads'),
         ('form: GPU filler function, ', 'Filler function of the GPU kernels, all threads')]
rows = []
for k, lbl in order:
    rb, rc = formrow(B['form'], k), formrow(C['form'], k)
    rows.append([lbl, pm(rb['per_mb'], rb['err'] / 10, 1), f"{rb['rel']:.1f}", pm(rc['per_mb'], rc['err'] / 10, 1), f"{rc['rel']:.1f}"])
table('Table 7. CPU formation, English text 10 MB (ms/MB; speedup relative to the reference written to a file)',
      ['Variant', 'B, ms/MB', 'B, speedup', 'C, ms/MB', 'C, speedup'], rows)


def share(S):
    F = S['form']
    ref = F['form: original stream -> file']
    nos = formrow(F, 'form: original, no stream')
    bulk = F['form: bulk CSPRNG (one call per 512 KB)']
    num = nos['mean'] - bulk['mean']
    ne = nos['err'] + bulk['err']
    return num / ref['mean'], (num - ne) / (ref['mean'] + ref['err']), (num + ne) / (ref['mean'] - ref['err'])


for name, S in (('B', B), ('C', C)):
    s = share(S)
    emit(f"Configuration {name}: share of the per-section generator calls in the reference formation {100 * s[0]:.1f}% "
         f"[{100 * s[1]:.1f}, {100 * s[2]:.1f}]; cost per call {1000 * S['form_derived']['per_call_us']:.0f} ns")
emit()

# ---------------------------------------------------------------- Table 8
cmpd = B['cmp']
lbl = {'uniform': 'Uniform', 'en': 'English text', 'ru': 'Russian text', 'zip': 'Compressed', 'zero': 'All-zero', 'ff': 'All-255'}
rows = []
for d in ('zero', 'ff', 'uniform', 'zip', 'en', 'ru'):
    xb, xc, c = B['dist'][d], C['dist'][d], cmpd[d]
    rows.append([lbl[d], f'{c[0]:.1f}', f'{c[1]:.1f}', pm(xb['search'][0], xb['search'][1], 1), pm(xb['table'][0], xb['table'][1], 1), f"{xb['ratio'][0]:.2f}",
                 pm(xc['search'][0], xc['search'][1], 1), pm(xc['table'][0], xc['table'][1], 1), f"{xc['ratio'][0]:.2f}"])
table('Table 8. Recognition with the same 16 keys per input (ms/MB, mean over keys ± half-width of the 99.9% CI)',
      ['Input', 'Comparisons/byte', 'Warp level', 'B search', 'B table', 'B ratio', 'C search', 'C table', 'C ratio'], rows)
nz = ['ff', 'uniform', 'zip', 'en', 'ru']
rb = [B['dist'][d]['ratio'][0] for d in nz]
rc = [C['dist'][d]['ratio'][0] for d in nz]


def unp(S, d):
    r = S['rec'][d]
    sr = [v for k, v in r.items() if isinstance(v, dict) and 'no stream (search' in k][0]['mean']
    tr = [v for k, v in r.items() if isinstance(v, dict) and 'table-driven kernel logic' in k][0]['mean']
    return sr / tr


ub = [unp(B, d) for d in nz]
uc = [unp(C, d) for d in nz]
emit(f"Search / table-driven, inputs other than all-zero: paired B {min(rb):.1f}-{max(rb):.1f}, paired C {min(rc):.1f}-{max(rc):.1f}, "
     f"unpaired B {min(ub):.1f}-{max(ub):.1f}, unpaired C {min(uc):.1f}-{max(uc):.1f}, overall {min(rb + rc + ub + uc):.1f}-{max(rb + rc + ub + uc):.1f}")
for name, S in (('B', B), ('C', C)):
    kc = S['key_corr']
    emit(f"Configuration {name}: bytes inspected per tried etalon {kc['x_range'][0]:.1f}-{kc['x_range'][1]:.1f}; "
         f"search time on uniform data {kc['y_range'][0]:.0f}-{kc['y_range'][1]:.0f} ms/MB; Pearson r = {kc['r']:.2f}")
emit()

# ---------------------------------------------------------------- Table 9 (NIST SP 800-22)
nist = dict(B['nist'])
nist['csprng_win'] = C['nist']['csprng_win']
labels = [('gpu_filler_0', 'GPU filler function, seed 1'), ('gpu_filler_1', 'GPU filler function, seed 2'),
          ('gpu_filler_2', 'GPU filler function, seed 3'), ('csprng', 'System generator of configuration B'),
          ('csprng_win', 'System generator of configuration C')]
rows = []
for l, name in labels:
    tests = nist[l]
    fl = [x for x in tests if x['flag']]
    ps = [x['p'] for x in tests if x['p'] is not None]
    re_ = [x for x in tests if x['total'] != 100]
    rows.append([name, f"{len(tests) - len(fl)} of {len(tests)}", f"{min(x['passed'] for x in tests if x['total'] == 100)}/100",
                 f"{min(x['passed'] for x in re_)}/{sorted(set(x['total'] for x in re_))[0]}", f'{min(ps):.4f}',
                 '; '.join(f"{x['name']} {x['passed']}/{x['total']}" for x in fl) or '-'])
table('Table 9. NIST SP 800-22, 100 bitstreams of 10^6 bits per sequence',
      ['Sequence', 'Statistics passed', 'Lowest pass count', 'Lowest pass count, random excursions', 'Smallest uniformity P', 'Failed statistics'], rows)
FD = RESULTS / 'configuration_B' / 'nist_followup'
fu = []
for fn in sorted(os.listdir(FD)):
    if fn.endswith('_finalAnalysisReport.txt'):
        for line in open(FD / fn):
            m = re.search(r'(\d+)/100\s*\*?\s*BlockFrequency', line)
            if m:
                fu.append((fn.replace('_finalAnalysisReport.txt', ''), int(m.group(1))))
emit('Follow-up, Block Frequency on six further GPU filler sequences: ' + ', '.join(f'{a} {b}/100' for a, b in fu))

emit()

# ---------------------------------------------------------------- configuration D (Tables 10-15)
rows = [[f"{g['kb']} KB", pm(g['cpu'][0], g['cpu'][1], 2), pm(g['igpu'][0], g['igpu'][1], 2), pm(g['dgpu'][0], g['dgpu'][1], 2),
         sint(g['s_igpu']), sint(g['s_dgpu'])] for g in DD['grid']]
table('Table 10. Full cycle on configuration D, 1-100 KB, three processes per value (ms, mean ± half-width of the 99.9% CI)',
      ['Volume', 'CPU-seq', 'iGPU', 'dGPU', 'S iGPU', 'S dGPU'], rows)
for g in ('igpu', 'dgpu'):
    b = DD[f'bracket_{g}']
    emit(f"{g}: CPU-seq faster up to {b['last_cpu']} KB, GPU faster from {b['first_gpu']} KB on; unresolved {b['unresolved']}; "
         f"interpolated crossing {b['cross']:.1f} KB")
emit()
t11 = DD['t11']
rows = [[k, pm(v['mean'], v['err'], 3), v['N'], sint(v['s_seq']), sint(v['s_best'])] for k, v in t11.items()]
table('Table 11. Full cycle of 100 MB on configuration D (s); speedups over CPU-seq and relative to CPU-par-GF-table',
      ['Path', 'Time', 'N', 'S over CPU-seq', 'Relative to CPU-par-GF-table'], rows)
F12 = DD['formation']['t12']; R13 = DD['recovery']['t13']
rows = [['formation', k, pm(v['mean'], v['err'], 1), v['N'], sint(v['s_ref'])] for k, v in F12.items()] + \
       [['recovery', k, pm(v['mean'], v['err'], 1), v['N'], sint(v['s_ref'])] for k, v in R13.items()]
table('Table 12. Formation and recovery of 10 MB on configuration D (ms); speedup over CPU-seq',
      ['Stage', 'Path', 'Time', 'N', 'S over CPU-seq'], rows)
F = DD['formation']
emit(f"Per-section generator calls: {100 * F['call_share']:.1f}% of the reference formation "
     f"[{100 * F['call_share_ci'][0]:.1f}, {100 * F['call_share_ci'][1]:.1f}], {F['ns_per_call']:.0f} ns per call; "
     f"dGPU over CPU-seq-GF {sint(F['dgpu_vs_seqgf'])}; iGPU over CPU-seq-GF {sint(F['igpu_vs_seqgf'])}; dGPU over CPU-par-GF {sint(F['dgpu_vs_pargf'])}")
emit()
DATA = ('zero', 'ff', 'uniform', 'ru')
rows = []
for m in ('CPU-seq', 'CPU-seq-table', 'iGPU', 'iGPU-table', 'iGPU-branchfree', 'dGPU', 'dGPU-table', 'dGPU-branchfree'):
    rows.append(['stage', m] + [pm(DD['recognition'][f'{d}|{m}']['mean'], DD['recognition'][f'{d}|{m}']['err'], 1) for d in DATA])
for dev in ('iGPU', 'dGPU'):
    for st in ('formation kernel', 'search kernel', 'table kernel', 'branch-free kernel'):
        rows.append(['kernel', f'{dev} {st}'] + [pm(DD['kernels'][f'{dev}|{d}|{st}']['mean'], DD['kernels'][f'{dev}|{d}|{st}']['sd'], 2) for d in DATA])
table('Table 13. Recognition of the 10 MB inputs on configuration D: stage (mean ± CI) and kernel-only time (mean ± SD of 10), ms',
      ['Measurement', 'Procedure', 'All-zero', 'All-255', 'Uniform', 'Russian text'], rows)
K = DD['kernels']
def km(dev, step): return K[f'{dev}|ru|{step}']['mean']
rows = []
for dev in ('iGPU', 'dGPU'):
    for lbl, stage, kern, to_dev, to_host in (('formation', F12[dev]['mean'], 'formation kernel', 'copy input to device', 'copy protected to host'),
                                               ('recovery, search', DD['recognition'][f'ru|{dev}']['mean'], 'search kernel', 'copy protected to device', 'copy output to host'),
                                               ('recovery, table', DD['recognition'][f'ru|{dev}-table']['mean'], 'table kernel', 'copy protected to device', 'copy output to host')):
        k_, a_, b_ = km(dev, kern), km(dev, to_dev), km(dev, to_host)
        rows.append([dev, lbl, rnd(stage, 1), rnd(k_, 1), rnd(a_, 1), rnd(b_, 1), rnd(stage - k_ - a_ - b_, 1), rnd(100 * (k_ + a_ + b_) / stage, 1)])
table('Table 14. Decomposition of the stages for 10 MB of Russian text on configuration D (ms)',
      ['Device', 'Stage', 'Stage time', 'Kernel', 'Copy to device', 'Copy to host', 'Remainder', 'Kernel and copies, %'], rows)
ck = DD['chunk']
rows = []
for dev in ('iGPU', 'dGPU'):
    rows.append([dev] + [f"{pm(ck[dev][c]['mean'], ck[dev][c]['err'], 3)} ({100 * ck[dev][c]['rel']:+.1f}%)" for c in ('orig', 'v2-256', 'v2-512', 'v2-1024', 'v2-2048')])
rows.append(['allocation, MB'] + [f"{ck['dGPU'][c]['alloc']:.0f}" for c in ('orig', 'v2-256', 'v2-512', 'v2-1024', 'v2-2048')])
table(f"Table 15. Full cycle of 100 MB on configuration D by chunk size (s; relative to V2 with 512 KB); CPU-seq {pm(ck['CPU-seq']['mean'], ck['CPU-seq']['err'], 2)} s",
      ['', 'Original, 512 KB', 'V2, 256 KB', 'V2, 512 KB', 'V2, 1024 KB', 'V2, 2048 KB'], rows)

open(out_path('paper_values.md'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print('\nwritten', out_path('paper_values.md').relative_to(ROOT))
