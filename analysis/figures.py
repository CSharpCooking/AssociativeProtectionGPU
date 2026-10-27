# -*- coding: utf-8 -*-
"""Figures 2-5 of the paper (PNG, 300 dpi), written to analysis/output/.
Run config_a.py, summarize.py (B and C) and config_d.py first.

usage: python3 analysis/figures.py"""
import json, math
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D

from common import OUT, RESULTS, ROOT, out_path

D = json.load(open(OUT / 'config_A.json'))
out_path('fig2.png')

BLUE, ORANGE = '#2a78d6', '#eb6834'          # validated categorical slots 1-2 (light)
INK, INK2, GRID = '#0b0b0b', '#52514e', '#e4e3df'
plt.rcParams.update({'font.family': 'DejaVu Sans', 'font.size': 10, 'axes.edgecolor': INK2, 'axes.labelcolor': INK,
                     'xtick.color': INK2, 'ytick.color': INK2, 'axes.linewidth': 0.8})


def style(ax):
    for s in ('top', 'right'): ax.spines[s].set_visible(False)
    ax.grid(axis='y', color=GRID, lw=0.8); ax.set_axisbelow(True)


# ---------------------------------------------------------------- Figure 2: speedup vs volume
rows = D['table4']
lbl = ['1 KB', '10 KB', '100 KB', '1 MB', '3.45 MB', '10 MB', '100 MB']
x = list(range(len(rows)))
fig, ax = plt.subplots(figsize=(7.2, 4.0), dpi=300)
style(ax)
for key, col, mk, name, dx in (('s_igpu', BLUE, 's', 'Intel UHD Graphics (OpenCL)', -0.06), ('s_dgpu', ORANGE, 'o', 'NVIDIA GeForce RTX 4050 Laptop GPU (CUDA)', 0.06)):
    s = [r[key][0] for r in rows]; lo = [r[key][0] - r[key][1] for r in rows]; hi = [r[key][2] - r[key][0] for r in rows]
    xs = [v + dx for v in x]
    ax.errorbar(xs, s, yerr=[lo, hi], color=col, lw=2, marker=mk, ms=6, capsize=3, elinewidth=1.2, label=name,
                markeredgecolor='white', markeredgewidth=0.8, zorder=3)
ax.axhline(1.0, color=INK2, lw=1, ls='--', zorder=1)
ax.text(-0.35, 1.12, 'break-even, S = 1', color=INK2, fontsize=8.5, va='bottom')
ax.set_xticks(x); ax.set_xticklabels(lbl)
ax.set_xlim(-0.5, len(x) - 0.5); ax.set_ylim(0, 8.8)
ax.set_xlabel('Input volume'); ax.set_ylabel('Speedup S over CPU-seq (full cycle)')
ax.legend(loc='upper left', frameon=False, fontsize=8.5)
fig.tight_layout(); fig.savefig(f'{OUT}/fig2.png'); plt.close(fig)

# ---------------------------------------------------------------- Figure 3: stage speedups at 100 MB
t6 = D['table6']
ops = [('full', 'Full cycle'), ('formation', 'Block formation'), ('recovery', 'Recovery')]
fig, ax = plt.subplots(figsize=(7.2, 3.8), dpi=300)
for s_ in ('top', 'right'): ax.spines[s_].set_visible(False)
ax.grid(axis='x', color=GRID, lw=0.8); ax.set_axisbelow(True)
h = 0.34
for i, (op, name) in enumerate(ops):
    y0 = len(ops) - 1 - i
    for key, col, off, dev in (('s_dgpu', ORANGE, +h / 2 + 0.01, 'dGPU'), ('s_igpu', BLUE, -h / 2 - 0.01, 'iGPU')):
        s, lo, hi = t6[op][key]
        ax.barh(y0 + off, s, height=h, color=col, zorder=2)
        ax.errorbar(s, y0 + off, xerr=[[s - lo], [hi - s]], color=INK, capsize=3, lw=1, zorder=3)
        ax.text(hi + 0.15, y0 + off, f'{s:.2f}  [{lo:.2f}, {hi:.2f}]', va='center', fontsize=8.5, color=INK)
ax.set_yticks(range(len(ops))); ax.set_yticklabels([n for _, n in ops][::-1])
ax.set_xlim(0, 13.2); ax.set_xlabel('Speedup S over CPU-seq, 100 MB input')
handles = [Line2D([0], [0], color=ORANGE, lw=8), Line2D([0], [0], color=BLUE, lw=8)]
ax.legend(handles, ['NVIDIA GeForce RTX 4050 Laptop GPU (CUDA)', 'Intel UHD Graphics (OpenCL)'], loc='lower left', bbox_to_anchor=(0.0, 1.0), ncol=2, frameon=False, fontsize=8.5)
fig.tight_layout(); fig.savefig(f'{OUT}/fig3.png'); plt.close(fig)

# ---------------------------------------------------------------- Figure 4: recognition on configurations B and C (paired design)
AQUA = '#1baf7a'                              # validated categorical slot 3 (all-pairs safe with slots 1-2)
order = ['zero', 'ff', 'uniform', 'zip', 'en', 'ru']
names = {'zero': 'All-\nzero', 'ff': 'All-\n255', 'uniform': 'Uniform', 'zip': 'Com-\npressed', 'en': 'English\ntext', 'ru': 'Russian\ntext'}
SUM = {c: json.load(open(OUT / f'summary_{c}.json')) for c in 'BC'}
RAW = {c: json.load(open(RESULTS / f'configuration_{c}' / 'dist_10MB.json')) for c in 'BC'}
BY = {}
for cfg, rows in RAW.items():
    BY[cfg] = {}
    for r in rows: BY[cfg].setdefault(r['input'].split('_')[0], []).append(r)
fig, axs = plt.subplots(2, 2, figsize=(7.8, 6.9), dpi=300)
(a1, a2), (a3, a4) = axs
for ax in (a1, a2, a3, a4): style(ax)
import random
random.seed(1)
for ax, cfg, ymax, tag in ((a1, 'B', 140, '(a) configuration B'), (a2, 'C', 260, '(b) configuration C')):
    by = BY[cfg]
    for i, d in enumerate(order):
        rs = by[d]
        for key, col, dx, mk in (('searchMsPerMB', BLUE, -0.17, 'o'), ('tableMsPerMB', ORANGE, 0.17, 's')):
            ys = [r[key] for r in rs]
            xs = [i + dx + random.uniform(-0.06, 0.06) for _ in ys]
            ax.scatter(xs, ys, s=11, color=col, marker=mk, edgecolors='white', linewidths=0.4, zorder=3)
            m = sum(ys) / len(ys)
            ax.plot([i + dx - 0.13, i + dx + 0.13], [m, m], color=INK, lw=1.4, zorder=4)
    ax.set_xticks(range(len(order))); ax.set_xticklabels([names[d] for d in order], fontsize=8.5)
    ax.set_ylabel('Recognition time, ms/MB'); ax.set_ylim(0, ymax)
    ax.set_title(tag, fontsize=9.5, color=INK2, loc='left')
fig.legend([Line2D([0], [0], marker='o', color='w', markerfacecolor=BLUE, ms=6), Line2D([0], [0], marker='s', color='w', markerfacecolor=ORANGE, ms=6),
            Line2D([0], [0], color=INK, lw=1.4)],
           ['Sequential search', 'Table-driven', 'Mean over 16 keys'], loc='upper center', bbox_to_anchor=(0.5, 1.0), frameon=False, fontsize=9, ncol=3)
# (c) relative search time versus comparisons per byte
cmpd = SUM['B']['cmp']
xu = cmpd['uniform'][0]
xs_line = [2.0, 16.0]
a3.plot(xs_line, [v / xu for v in xs_line], color=INK2, lw=1, ls='--', zorder=2)
for cfg, col, mk, dx in (('B', BLUE, 'o', -0.18), ('C', AQUA, '^', 0.18)):
    dist = SUM[cfg]['dist']
    for d in order:
        x = cmpd[d][0] + dx
        m, e = (1.0, 0.0) if d == 'uniform' else dist[d]['search_vs_uniform']
        a3.errorbar([x], [m], yerr=[[e], [e]], fmt=mk, color=col, ms=5, mec='white', mew=0.5, elinewidth=1, capsize=0, zorder=3)
lab = {'zero': ('All-zero', 3.55, 0.16, 'left'), 'en': ('English', 10.2, 0.63, 'center'), 'uniform': ('Uniform,\ncompressed', 11.4, 1.10, 'center'),
       'ru': ('Russian', 13.45, 0.68, 'center'), 'ff': ('All-255', 15.0, 0.62, 'center')}
for d, (t, xx, yy, ha) in lab.items():
    a3.text(xx, yy, t, fontsize=8, color=INK2, ha=ha, va='bottom')
a3.set_xlim(1.5, 16.5); a3.set_ylim(0, 1.45)
a3.set_xlabel('Etalon comparisons per byte (mean)'); a3.set_ylabel('Search time relative to uniform data')
a3.set_title('(c) dependence on the data', fontsize=9.5, color=INK2, loc='left')
a3.legend([Line2D([0], [0], marker='o', color='w', markerfacecolor=BLUE, ms=6), Line2D([0], [0], marker='^', color='w', markerfacecolor=AQUA, ms=6),
           Line2D([0], [0], color=INK2, lw=1, ls='--')],
          ['Configuration B', 'Configuration C', 'Proportional to comparisons'], loc='lower right', frameon=False, fontsize=8.5)
# (d) key dependence: search time on uniform data relative to its mean over the keys
RR = {}
for cfg, col, mk, ls in (('B', BLUE, 'o', '--'), ('C', AQUA, '^', ':')):
    u = BY[cfg]['uniform']
    xs = [r['bytesPerTriedEtalon'] for r in u]; ys0 = [r['searchMsPerMB'] for r in u]
    my0 = sum(ys0) / len(ys0); ys = [v / my0 for v in ys0]
    a4.scatter(xs, ys, s=16, color=col, marker=mk, edgecolors='white', linewidths=0.4, zorder=3)
    mx, my = sum(xs) / len(xs), sum(ys) / len(ys)
    b = sum((p - mx) * (q - my) for p, q in zip(xs, ys)) / sum((p - mx) ** 2 for p in xs); a0 = my - b * mx
    xl = [min(xs) - 0.1, max(xs) + 0.1]
    a4.plot(xl, [a0 + b * v for v in xl], color=col, lw=1, ls=ls, zorder=2)
    rr = sum((p - mx) * (q - my) for p, q in zip(xs, ys)) / math.sqrt(sum((p - mx) ** 2 for p in xs) * sum((q - my) ** 2 for q in ys))
    RR[cfg] = rr
a4.legend([Line2D([0], [0], marker='o', color=BLUE, ls='--', lw=1, markerfacecolor=BLUE, mec='white', ms=6),
           Line2D([0], [0], marker='^', color=AQUA, ls=':', lw=1, markerfacecolor=AQUA, mec='white', ms=6)],
          [f'Configuration B, r = {RR["B"]:.2f}', f'Configuration C, r = {RR["C"]:.2f}'], loc='upper left', frameon=False, fontsize=8.5)
a4.set_xlim(3.8, 8.6)
a4.set_xlabel('Bytes inspected per tried etalon'); a4.set_ylabel('Search time / mean over keys')
a4.set_title('(d) dependence on the key, uniform data', fontsize=9.5, color=INK2, loc='left')
fig.tight_layout(h_pad=1.6, rect=(0, 0, 1, 0.965)); fig.savefig(f'{OUT}/fig4.png'); plt.close(fig)
# ---------------------------------------------------------------- Figure 5: break-even grid on configuration D
DD = json.load(open(OUT / 'config_D.json'))
g = DD['grid']
kb = [r['kb'] for r in g]
fig, (b1, b2) = plt.subplots(1, 2, figsize=(7.8, 3.6), dpi=300)
for ax in (b1, b2): style(ax)
for key, col, mk, name in (('cpu', AQUA, 'D', 'CPU-seq'), ('igpu', BLUE, 's', 'iGPU (OpenCL)'), ('dgpu', ORANGE, 'o', 'dGPU (CUDA)')):
    m = [r[key][0] for r in g]; e = [r[key][1] for r in g]
    b1.errorbar(kb, m, yerr=e, color=col, lw=1.8, marker=mk, ms=4.5, capsize=2, elinewidth=1, label=name,
                markeredgecolor='white', markeredgewidth=0.6, zorder=3)
b1.set_xlim(0, 104); b1.set_ylim(0, 28); b1.set_xticks(kb); b1.tick_params(axis='x', labelsize=8.5)
b1.set_xlabel('Input volume, KB'); b1.set_ylabel('Full-cycle time, ms')
b1.legend(loc='upper left', frameon=False, fontsize=8.5)
b1.set_title('(a) time', fontsize=9.5, color=INK2, loc='left')
for key, col, mk, name, dx in (('s_igpu', BLUE, 's', 'iGPU (OpenCL)', -0.8), ('s_dgpu', ORANGE, 'o', 'dGPU (CUDA)', 0.8)):
    s_ = [r[key][0] for r in g]; lo = [r[key][0] - r[key][1] for r in g]; hi = [r[key][2] - r[key][0] for r in g]
    b2.errorbar([v + dx for v in kb], s_, yerr=[lo, hi], color=col, lw=1.8, marker=mk, ms=4.5, capsize=2, elinewidth=1, label=name,
                markeredgecolor='white', markeredgewidth=0.6, zorder=3)
b2.axhline(1.0, color=INK2, lw=1, ls='--', zorder=1)
b2.text(103, 1.06, 'break-even, S = 1', color=INK2, fontsize=8, va='bottom', ha='right')
b2.set_xlim(0, 104); b2.set_ylim(0, 3.3); b2.set_xticks(kb); b2.tick_params(axis='x', labelsize=8.5)
b2.set_xlabel('Input volume, KB'); b2.set_ylabel('Speedup S over CPU-seq')
b2.legend(loc='upper left', frameon=False, fontsize=8.5)
b2.set_title('(b) speedup', fontsize=9.5, color=INK2, loc='left')
fig.tight_layout(w_pad=2.0); fig.savefig(f'{OUT}/fig5.png'); plt.close(fig)
print('figures written to', OUT.relative_to(ROOT))
