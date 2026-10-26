# -*- coding: utf-8 -*-
"""Configuration A: Tables 4-6 of the paper and the derived quantities of Sections 7.1-7.3 and 7.8 (fixed and
marginal costs, allocation experiment, confounding of the 1024 KB series, crossing points), computed from the
BenchmarkDotNet statistics in results/configuration_A/bdn_statistics.json.
Writes analysis/output/config_A.json and prints a report.

usage: python3 analysis/config_a.py"""
import json, math

from common import ROOT, RESULTS, out_path

ORIG = json.load(open(RESULTS / 'configuration_A' / 'bdn_statistics.json', encoding='utf-8'))
SIZES = ['1KB', '10KB', '100KB', '1MB', '3.45MB', '10MB', '100MB']
KB = {'1KB': 1, '10KB': 10, '100KB': 100, '1MB': 1024, '3.45MB': 3530, '10MB': 10240, '100MB': 102400}


def get(series, size, name):
    for r in ORIG:
        if r['series'] == series and r['size'] == size and r['name'] == name:
            return r
    raise KeyError((series, size, name))


def spd(ref, gpu):
    """speedup and conservative interval from the 99.9% CI half-widths"""
    s = ref['Mean'] / gpu['Mean']
    lo = (ref['Mean'] - ref['Error']) / (gpu['Mean'] + gpu['Error'])
    hi = (ref['Mean'] + ref['Error']) / (gpu['Mean'] - gpu['Error'])
    return s, lo, hi


out = {'table4': [], 'table6': {}, 'fixed': {}, 'marginal': {}, 'confound': [], 'alloc': {}}
print('=== Table 4: full cycle, 512 KB chunk (ms)')
for z in SIZES:
    c, i, d = (get('512', z, n + ' full') for n in ('CPU', 'iGPU', 'dGPU'))
    si, sd = spd(c, i), spd(c, d)
    row = dict(size=z, cpu=(c['Mean'] * 1e3, c['Error'] * 1e3, c['N']), igpu=(i['Mean'] * 1e3, i['Error'] * 1e3, i['N']),
               dgpu=(d['Mean'] * 1e3, d['Error'] * 1e3, d['N']), s_igpu=si, s_dgpu=sd)
    out['table4'].append(row)
    print(f"{z:>7}  CPU {c['Mean']*1e3:10.3f}±{c['Error']*1e3:7.3f} (N={c['N']:3})  iGPU {i['Mean']*1e3:9.3f}±{i['Error']*1e3:6.3f} (N={i['N']:3})"
          f"  dGPU {d['Mean']*1e3:9.3f}±{d['Error']*1e3:6.3f} (N={d['N']:3})  S_i={si[0]:.2f} [{si[1]:.2f},{si[2]:.2f}]  S_d={sd[0]:.2f} [{sd[1]:.2f},{sd[2]:.2f}]")

print('\n=== Table 6: 100 MB, stages (s)')
for op, key in (('full', 'full'), ('formation', 'enc'), ('recovery', 'dec')):
    c, i, d = (get('512', '100MB', f'{n} {key}') for n in ('CPU', 'iGPU', 'dGPU'))
    si, sd = spd(c, i), spd(c, d)
    out['table6'][op] = dict(cpu=(c['Mean'], c['Error'], c['N']), igpu=(i['Mean'], i['Error'], i['N']), dgpu=(d['Mean'], d['Error'], d['N']),
                             s_igpu=si, s_dgpu=sd)
    print(f"{op:>10}  CPU {c['Mean']:.3f}±{c['Error']:.3f} (N={c['N']})  iGPU {i['Mean']:.3f}±{i['Error']:.3f} (N={i['N']})  dGPU {d['Mean']:.3f}±{d['Error']:.3f} (N={d['N']})"
          f"  S_i={si[0]:.2f} [{si[1]:.2f},{si[2]:.2f}]  S_d={sd[0]:.2f} [{sd[1]:.2f},{sd[2]:.2f}]")
t6 = out['table6']
for v in ('cpu', 'igpu', 'dgpu'):
    f, r, full = t6['formation'][v][0], t6['recovery'][v][0], t6['full'][v][0]
    print(f"  {v}: formation/recovery = {f/r:.3f}; recovery share of full = {100*r/full:.1f}%; (f+r)/full = {(f+r)/full:.3f}")
# throughput of the protected representation at 100 MB (3000 MB = 30 * 100 MB)
for v in ('cpu', 'igpu', 'dgpu'):
    print(f"  {v}: protected data written {3000/t6['formation'][v][0]/1024:.2f} GiB/s, read {3000/t6['recovery'][v][0]/1024:.2f} GiB/s")

print('\n=== fixed part at 1 KB and marginal cost per MB between 10 and 100 MB')
for n in ('CPU', 'iGPU', 'dGPU'):
    for key in ('full', 'enc', 'dec'):
        a, b, s1 = get('512', '10MB', f'{n} {key}'), get('512', '100MB', f'{n} {key}'), get('512', '1KB', f'{n} {key}')
        m = (b['Mean'] - a['Mean']) / 90 * 1e3   # ms per MB
        e = (b['Error'] + a['Error']) / 90 * 1e3
        out['marginal'][f'{n} {key}'] = (m, e)
        out['fixed'][f'{n} {key}'] = (s1['Mean'] * 1e3, s1['Error'] * 1e3, s1['alloc'], s1['gen'])
        per10 = a['Mean'] / 10 * 1e3; per100 = b['Mean'] / 100 * 1e3
        print(f"{n:>5} {key:>4}: T(1KB)={s1['Mean']*1e3:7.3f}±{s1['Error']*1e3:.3f} ms alloc={s1['alloc']:>9} gen={s1['gen']}   "
              f"marginal={m:7.2f}±{e:5.2f} ms/MB   per-MB at 10 MB={per10:7.2f}, at 100 MB={per100:7.2f} ({100*(per100/per10-1):+.1f}%)")

print('\n=== natural experiment: chunk 512 vs 1024 KB at small volumes (full cycle)')
for z in ('1KB', '10KB', '1MB'):
    for n in ('CPU', 'iGPU', 'dGPU'):
        a, b = get('512', z, f'{n} full'), get('1024', z, f'{n} full')
        print(f"{z:>5} {n:>5}: {a['Mean']*1e3:8.3f}±{a['Error']*1e3:.3f} ({a['alloc']}) -> {b['Mean']*1e3:8.3f}±{b['Error']*1e3:.3f} ({b['alloc']})  "
              f"delta={1e3*(b['Mean']-a['Mean']):+.3f} ms")
for n in ('iGPU', 'dGPU'):
    a, b = get('512', '1KB', f'{n} full'), get('1024', '1KB', f'{n} full')
    da = float(b['alloc'].split()[0]) - float(a['alloc'].split()[0])
    dt = (b['Mean'] - a['Mean']) * 1e3
    lo = ((b['Mean'] - b['Error']) - (a['Mean'] + a['Error'])) * 1e3
    hi = ((b['Mean'] + b['Error']) - (a['Mean'] - a['Error'])) * 1e3
    out['alloc'][n] = dict(dt=dt, lo=lo, hi=hi, dalloc=da, per_mb=dt / da, alloc512=float(a['alloc'].split()[0]))
    print(f"  {n}: +{da:.2f} MB allocated -> +{dt:.2f} ms [{lo:.2f}, {hi:.2f}]  => {dt/da:.4f} ms per MB; at 512 KB chunk ({a['alloc']}): ~{dt/da*float(a['alloc'].split()[0]):.2f} ms")

print('\n=== confound: CPU-only operations, 1024 vs 512 series')
for z in SIZES:
    for key in ('enc', 'dec', 'full'):
        a, b = get('512', z, f'CPU {key}'), get('1024', z, f'CPU {key}')
        out['confound'].append(dict(size=z, op=key, r=b['Mean'] / a['Mean']))
    print(f"{z:>7}: formation x{b and get('1024', z, 'CPU enc')['Mean']/get('512', z, 'CPU enc')['Mean']:.2f}  "
          f"recovery x{get('1024', z, 'CPU dec')['Mean']/get('512', z, 'CPU dec')['Mean']:.2f}  full x{get('1024', z, 'CPU full')['Mean']/get('512', z, 'CPU full')['Mean']:.2f}")

print('\n=== linear cost models on 1, 10, 100 KB (OLS, full cycle)')
def ols(xs, ys):
    n = len(xs); mx = sum(xs) / n; my = sum(ys) / n
    c = sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / sum((x - mx) ** 2 for x in xs)
    return my - c * mx, c
fits = {}
for n in ('CPU', 'iGPU', 'dGPU'):
    xs = [1, 10, 100]; ys = [get('512', z, f'{n} full')['Mean'] * 1e3 for z in ('1KB', '10KB', '100KB')]
    fits[n] = ols(xs, ys)
    print(f"  {n}: T0={fits[n][0]:.3f} ms, c={fits[n][1]:.4f} ms/KB")
for g in ('iGPU', 'dGPU'):
    x = (fits[g][0] - fits['CPU'][0]) / (fits['CPU'][1] - fits[g][1])
    out[f'cross_{g}'] = x
    print(f"  crossing CPU vs {g}: {x:.1f} KB")
# crossing from two-point models between 1 and 100 KB, and from interpolation between 10 and 100 KB
for g in ('iGPU', 'dGPU'):
    c1, c100 = get('512', '1KB', 'CPU full')['Mean'], get('512', '100KB', 'CPU full')['Mean']
    g1, g100 = get('512', '1KB', f'{g} full')['Mean'], get('512', '100KB', f'{g} full')['Mean']
    cc = (c100 - c1) / 99; gc = (g100 - g1) / 99
    x2 = 1 + (g1 - c1) / (cc - gc)
    c10, g10 = get('512', '10KB', 'CPU full')['Mean'], get('512', '10KB', f'{g} full')['Mean']
    # linear interpolation of the difference between 10 and 100 KB
    d10, d100 = c10 - g10, c100 - g100
    x3 = 10 + (0 - d10) * 90 / (d100 - d10)
    print(f"  {g}: two-point model (1,100 KB) -> {x2:.1f} KB; interpolation of T_CPU - T_GPU between 10 and 100 KB -> {x3:.1f} KB")
    out[f'cross2_{g}'] = x2; out[f'cross3_{g}'] = x3

json.dump(out, open(out_path('config_A.json'), 'w'), indent=1, default=str)
print('\nwritten', out_path('config_A.json').relative_to(ROOT))
