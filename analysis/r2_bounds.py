"""Revision 2: quantities derived from the existing measurements of configuration D.
 1. Process-level 99.9% CIs of the break-even grid (three processes, Student's t with two degrees of freedom), the
    brackets of the break-even volume at this level and at the 95% level, and the ordering of the process means.
 2. Upper bounds of the effect of kernel tuning (coalesced accesses, thread group sizes) and of the overlap of transfers with
    computation on the stages of 10 MB, from the decomposition of Table 14 (Russian text): a change of the kernels cannot save
    more than the kernel time, and an overlap cannot hide more than the copy time.
 3. The shift of the break-even volume if the host arrays allocated for a full chunk were sized to the input, under the
    assumption that they account for the same share of the fixed cost of the GPU path as on configuration A (about 60%).
Usage: python3 r2_bounds.py   (reads output/config_D.json, writes output/r2_bounds.json)"""
import json, math, os, statistics as st

from tdist import t_quantile   # Student's t quantile without SciPy

HERE = os.path.dirname(os.path.abspath(__file__))
D = json.load(open(os.path.join(HERE, 'output', 'config_D.json')))
OUT = {}

# ------------------------------------------------------------------ 1. process-level statistics of the grid
grid = {}
for e in D['cases']['RevBreakEven']:
    grid[(int(e['params']['SizeKB']), e['params']['Mode'])] = e['launch_means']
vols = sorted({v for v, _ in grid})
modes = ['CPU-seq', 'iGPU', 'dGPU']


def ci(x, level):
    k = len(x)
    t = t_quantile(0.5 + level / 2, k - 1)
    m = st.mean(x)
    h = t * st.stdev(x) / math.sqrt(k)
    return m, h


def bracket(level):
    res = {}
    for g in ('iGPU', 'dGPU'):
        state = {}
        for v in vols:
            mc, hc = ci(grid[(v, 'CPU-seq')], level)
            mg, hg = ci(grid[(v, g)], level)
            state[v] = 'GPU' if mg + hg < mc - hc else ('CPU' if mc + hc < mg - hg else 'unresolved')
        last_cpu = max([v for v in vols if state[v] == 'CPU'], default=None)
        first_gpu = min([v for v in vols if all(state[w] == 'GPU' for w in vols if w >= v)], default=None)
        res[g] = dict(state=state, last_cpu=last_cpu, first_gpu=first_gpu)
    return res


OUT['grid_process_level'] = {
    'half_width_pct_999': {m: {v: 100 * ci(grid[(v, m)], 0.999)[1] / ci(grid[(v, m)], 0.999)[0] for v in vols} for m in modes},
    'bracket_999': bracket(0.999),
    'bracket_95': bracket(0.95),
}
hw = OUT['grid_process_level']['half_width_pct_999']
OUT['grid_process_level']['half_width_pct_999_range'] = [min(min(h.values()) for h in hw.values()), max(max(h.values()) for h in hw.values())]
OUT['grid_process_level']['half_width_pct_999_range_without_igpu_1kb'] = [
    min(min(h.values()) for h in hw.values()),
    max(max(x for v, x in h.items() if not (m == 'iGPU' and v == 1)) for m, h in hw.items())]
# ordering of the process means: one-sided permutation test, all three below all three (p = 1/20)
OUT['grid_process_level']['permutation_p_all_below'] = 1 / math.comb(6, 3)

# ------------------------------------------------------------------ 2. bounds from the decomposition (Table 14)
K = D['kernels']
def kern(dev, name):
    return K[f'{dev}|ru|{name}']['mean']
t12, t13 = D['formation']['t12'], D['recovery']['t13']
stage = {
    'iGPU': {'formation': t12['iGPU']['mean'], 'search': D['recognition']['ru|iGPU']['mean'] if 'ru|iGPU' in D['recognition'] else None,
             'table': D['recognition']['ru|iGPU-table']['mean'] if 'ru|iGPU-table' in D['recognition'] else None},
    'dGPU': {'formation': t12['dGPU']['mean'], 'search': D['recognition']['ru|dGPU']['mean'] if 'ru|dGPU' in D['recognition'] else None,
             'table': D['recognition']['ru|dGPU-table']['mean'] if 'ru|dGPU-table' in D['recognition'] else None},
}
bounds = {}
for dev in ('iGPU', 'dGPU'):
    f_k = kern(dev, 'formation kernel')
    f_c = kern(dev, 'copy input to device') + kern(dev, 'copy protected to host')
    r_c = kern(dev, 'copy protected to device') + kern(dev, 'copy output to host')
    b = {}
    for rec in ('search', 'table'):
        r_k = kern(dev, f'{rec} kernel')
        total = stage[dev]['formation'] + stage[dev][rec]
        b[rec] = dict(stage_ms=total, kernel_ms=f_k + r_k, copies_ms=f_c + r_c,
                      kernel_share=(f_k + r_k) / total, copy_share=(f_c + r_c) / total,
                      both_share=(f_k + r_k + f_c + r_c) / total, remainder_share=1 - (f_k + r_k + f_c + r_c) / total)
    bounds[dev] = b
OUT['bounds_10MB'] = bounds
# projection to the full cycle of 100 MB (Table 11), assuming the shares of 10 MB carry over
t11 = D['t11']
proj = {}
for dev, row in (('iGPU', 'iGPU-table'), ('dGPU', 'dGPU-table')):
    best = t11[row]['mean'] * (1 - bounds[dev]['table']['both_share'])
    proj[row] = dict(measured_s=t11[row]['mean'], lower_bound_s=best,
                     vs_cpu_par_gf_table=t11['CPU-par-GF-table']['mean'] / best)
OUT['projection_100MB'] = proj

# ------------------------------------------------------------------ 3. break-even volume with host arrays sized to the input
share_A = 0.60
fixed = D['fixed_1KB']
shift = {}
for g, add in (('iGPU', fixed['add_igpu']), ('dGPU', fixed['add_dgpu'])):
    red = share_A * add
    t = {v: st.mean(grid[(v, g)]) - red for v in vols}
    c = {v: st.mean(grid[(v, 'CPU-seq')]) for v in vols}
    cross = None
    for a, b2 in zip(vols, vols[1:]):
        da, db = t[a] - c[a], t[b2] - c[b2]
        if da > 0 >= db:
            cross = a + (b2 - a) * da / (da - db)
            break
    shift[g] = dict(reduction_ms=red, crossing_kb=cross, measured_crossing_kb=D[f'bracket_{g.lower()}']['cross'])
OUT['break_even_shift'] = shift

json.dump(OUT, open(os.path.join(HERE, 'output', 'r2_bounds.json'), 'w'), indent=1, default=str)
print('process-level 99.9% half-widths, % of the mean:', {m: {v: round(x, 1) for v, x in h.items()} for m, h in hw.items()})
for lvl in ('999', '95'):
    br = OUT['grid_process_level'][f'bracket_{lvl}']
    print(f'brackets at {lvl}:', {g: (b['last_cpu'], b['first_gpu'], b['state']) for g, b in br.items()})
for dev, b in bounds.items():
    for rec, x in b.items():
        print(f'{dev} {rec}: stages {x["stage_ms"]:.1f} ms; kernels {100 * x["kernel_share"]:.1f}%, copies {100 * x["copy_share"]:.1f}%, '
              f'both {100 * x["both_share"]:.1f}%, remainder {100 * x["remainder_share"]:.1f}%')
print('projection 100 MB:', {k: {a: round(b, 3) for a, b in v.items()} for k, v in proj.items()})
print('break-even shift:', shift)
