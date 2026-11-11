# -*- coding: utf-8 -*-
"""Kernel-level counters of the discrete GPU of configuration D (Section 7.14, Table 19 of the paper).

Input: results/configuration_D_r2/ncu/ncu_metrics.csv, the metrics that analysis/ncu_report.py extracted from the Nsight
Compute reports of the kit (ncu --set full; one run per input, uniform and all-255 (ff), 10 MB, a new key per input). In
each run the four kernels (formation, search, table-driven, branch-free) were launched twice, a warm-up launch and the
launch that the kernel-only program times; both were profiled (launches 0-3 and 4-7). The clock log of the session
(gpu_clocks_ncu_*.csv) and the kernel-only times of the recognition kernels in the second run (../kernels2.csv), in which
every thread group size had its own key, are summarized as well.

Output: analysis/output/ncu_counters.json and a text report on the standard output.
"""
import csv
import json
import statistics
from collections import Counter, defaultdict

from common import RESULTS, out_path

DIR = RESULTS / 'configuration_D_r2' / 'ncu'
KERNELS = {'Kernel_EncryptKernelCopy': 'formation', 'Kernel_DecryptSearchKernelCopy': 'search',
           'Kernel_DecryptTableKernel': 'table', 'Kernel_DecryptBranchFreeKernel': 'branch-free'}
WARP = 32


def load():
    runs = defaultdict(lambda: defaultdict(dict))
    kernel = {}
    for r in csv.DictReader(open(DIR / 'ncu_metrics.csv', encoding='utf-8')):
        key = (r['input'], int(r['launch']))
        kernel[key] = KERNELS[r['kernel']]
        try:
            runs[r['input']][int(r['launch'])][r['metric']] = float(r['value'])
        except ValueError:
            runs[r['input']][int(r['launch'])][r['metric']] = r['value']
    return runs, kernel


def counters(m):
    threads = m['launch__thread_count']
    c = {
        'duration_ms': m['gpu__time_duration.sum'] / 1e6,
        'sm_clock_mhz': m['sm__cycles_elapsed.avg.per_second'] / 1e6,
        'sm_cycles_active': m['sm__cycles_active.sum'],
        'block_size': m['launch__block_size'], 'grid_size': m['launch__grid_size'], 'threads': threads,
        'registers_per_thread': m['launch__registers_per_thread'],
        'occupancy_theoretical_pct': m['sm__maximum_warps_per_active_cycle_pct'],
        'occupancy_achieved_pct': m['sm__warps_active.avg.pct_of_peak_sustained_active'],
        'warps_active_per_sm': m['sm__warps_active.avg.per_cycle_active'],
        'warp_inst': m['smsp__inst_executed.sum'],
        'thread_inst': m['thread_inst_executed'],
        'threads_per_warp_inst': m['smsp__thread_inst_executed_per_inst_executed.ratio'],
        'threads_per_warp_inst_pred_on': m['smsp__thread_inst_executed_pred_on_per_inst_executed.ratio'],
        'branch_inst': m['smsp__inst_executed_op_branch.sum'],
        'divergent_branches': m['smsp__sass_branch_targets_threads_divergent.sum'],
        'branch_uniform_pct': m['smsp__sass_average_branch_targets_threads_uniform.pct'],
        'ld_requests': m['l1tex__t_requests_pipe_lsu_mem_global_op_ld.sum'],
        'ld_sectors': m['l1tex__t_sectors_pipe_lsu_mem_global_op_ld.sum'],
        'ld_bytes_per_sector': m['smsp__sass_average_data_bytes_per_sector_mem_global_op_ld.ratio'],
        'st_requests': m['l1tex__t_requests_pipe_lsu_mem_global_op_st.sum'],
        'st_sectors': m['l1tex__t_sectors_pipe_lsu_mem_global_op_st.sum'],
        'st_bytes_per_sector': m['smsp__sass_average_data_bytes_per_sector_mem_global_op_st.ratio'],
        'l2_sectors': m['memory_l2_theoretical_sectors_global'],
        'l2_sectors_ideal': m['memory_l2_theoretical_sectors_global_ideal'],
        'l1_hit_pct': m['l1tex__t_sector_hit_rate.pct'], 'l2_hit_pct': m['lts__t_sector_hit_rate.pct'],
        'dram_pct_of_peak': m['gpu__dram_throughput.avg.pct_of_peak_sustained_elapsed'],
        'dram_read_bytes': m['dram__bytes_read.sum'], 'dram_write_bytes': m['dram__bytes_write.sum'],
        'dram_read_gb_s': m['dram__bytes_read.sum.per_second'] / 1e9,
        'inst_per_sm_cycle': m['sm__inst_executed.avg.per_cycle_active'],
        'issue_slots_busy_pct': m['smsp__issue_active.avg.pct_of_peak_sustained_active'],
        'sm_count': m['launch__sm_count'],
    }
    c['thread_inst_per_warp_inst'] = c['thread_inst'] / c['warp_inst']
    c['ld_sectors_per_request'] = c['ld_sectors'] / c['ld_requests']
    c['st_sectors_per_request'] = c['st_sectors'] / c['st_requests']
    c['l2_sectors_to_ideal'] = c['l2_sectors'] / c['l2_sectors_ideal']
    c['divergent_per_warp'] = c['divergent_branches'] / (threads / WARP)
    c['warp_inst_per_thread'] = c['warp_inst'] / threads
    c['thread_inst_per_thread'] = c['thread_inst'] / threads
    return c


def clock_log():
    files = sorted(DIR.glob('gpu_clocks_ncu_*.csv'))
    busy = Counter()
    for f in files:
        for r in list(csv.reader(open(f, encoding='utf-8', errors='replace')))[1:]:
            if len(r) >= 9 and int(r[8].split()[0]) >= 20:     # utilized by at least 20%, as in config_d_r2.py
                busy[int(r[4].split()[0])] += 1
    n = sum(busy.values())
    mhz, cnt = busy.most_common(1)[0]
    return {'busy_samples': n, 'clock_mhz': mhz, 'share_pct': 100 * cnt / n}


def kernels2(device):
    """recognition kernels of one device in the second run: mean of the ten repetitions for every group size (a new key each)"""
    t = defaultdict(list)
    for r in csv.DictReader(open(RESULTS / 'configuration_D_r2' / 'kernels2.csv', encoding='utf-8')):
        if r['device'] == device:
            t[(r['input'], r['step'], int(r['group']))].append(float(r['ms']))
    out = {}
    for inp in ('ff', 'uniform', 'ru', 'zero'):
        out[inp] = {}
        for step in ('search kernel', 'branch-free kernel', 'table kernel'):
            means = {g: statistics.mean(v) for (i, s, g), v in t.items() if i == inp and s == step}
            out[inp][step] = {'by_group': means, 'min': min(means.values()), 'max': max(means.values()), 'keys': len(means)}
        s, b = out[inp]['search kernel']['by_group'], out[inp]['branch-free kernel']['by_group']
        ratio = {g: s[g] / b[g] for g in s}
        out[inp]['search_to_branch_free'] = {'by_group': ratio, 'min': min(ratio.values()), 'max': max(ratio.values())}
    return out


def main():
    runs, kernel = load()
    res = {'inputs': {}, 'clock_log': clock_log(), 'kernels2_dgpu': kernels2('dGPU'), 'kernels2_igpu': kernels2('iGPU')}
    for inp, launches in runs.items():
        assert len(launches) == 8, (inp, len(launches))
        per = defaultdict(list)
        for k in sorted(launches):
            per[kernel[(inp, k)]].append(counters(launches[k]))
        assert all(len(v) == 2 for v in per.values())
        res['inputs'][inp] = per
        for name, (a, b) in per.items():
            # the instruction and request counts are those of the algorithm and must not depend on the launch
            for key in ('warp_inst', 'thread_inst', 'ld_requests', 'st_requests', 'divergent_branches', 'threads_per_warp_inst'):
                assert abs(a[key] - b[key]) <= 1e-3 * max(abs(a[key]), 1), (inp, name, key, a[key], b[key])
            assert a['occupancy_theoretical_pct'] == b['occupancy_theoretical_pct'] == 100
            if name != 'search':
                assert a['threads_per_warp_inst'] == WARP and a['divergent_branches'] == 0, (inp, name)
    R = res['inputs']
    ratios = {}
    for inp in R:
        s, bf, tb = R[inp]['search'], R[inp]['branch-free'], R[inp]['table']
        ratios[inp] = {
            'search_to_branch_free_time': [s[k]['duration_ms'] / bf[k]['duration_ms'] for k in (0, 1)],
            'search_to_branch_free_cycles': [s[k]['sm_cycles_active'] / bf[k]['sm_cycles_active'] for k in (0, 1)],
            'search_to_branch_free_warp_inst': s[1]['warp_inst'] / bf[1]['warp_inst'],
            'search_to_branch_free_thread_inst': s[1]['thread_inst'] / bf[1]['thread_inst'],
            'branch_free_to_search_thread_inst': bf[1]['thread_inst'] / s[1]['thread_inst'],
            'search_to_table_warp_inst': s[1]['warp_inst'] / tb[1]['warp_inst'],
            'search_to_table_time': [s[k]['duration_ms'] / tb[k]['duration_ms'] for k in (0, 1)],
        }
    u, f = R['uniform']['search'][1], R['ff']['search'][1]
    ratios['uniform_to_ff'] = {'warp_inst': u['warp_inst'] / f['warp_inst'], 'thread_inst': u['thread_inst'] / f['thread_inst'],
                               'cycles': u['sm_cycles_active'] / f['sm_cycles_active'],
                               'time': [R['uniform']['search'][k]['duration_ms'] / R['ff']['search'][k]['duration_ms'] for k in (0, 1)]}
    res['ratios'] = ratios
    # largest relative difference between the two launches of a kernel, by counter
    spread = defaultdict(float)
    for inp in R:
        for name, (a, b) in R[inp].items():
            for key in a:
                if a[key] or b[key]:
                    spread[key] = max(spread[key], abs(a[key] - b[key]) / max(abs(a[key]), abs(b[key])))
    res['launch_spread'] = dict(spread)
    json.dump(res, open(out_path('ncu_counters.json'), 'w'), indent=1)

    cols = [('uniform', 'formation'), ('uniform', 'search'), ('ff', 'search'), ('uniform', 'table'), ('uniform', 'branch-free'),
            ('ff', 'formation'), ('ff', 'table'), ('ff', 'branch-free')]
    print('counters of the second (timed) launch; columns:', ', '.join(f'{k} ({i})' for i, k in cols))
    for key in R['uniform']['search'][1]:
        print(f'{key:32s}', ' '.join(f'{R[i][k][1][key]:14.5g}' for i, k in cols))
    print('\nduration of the two launches, ms')
    for i, k in cols:
        print(f'  {k:12s} {i:8s}', ' '.join(f"{c['duration_ms']:.2f} (SM clock {c['sm_clock_mhz']:.0f} MHz)" for c in R[i][k]))
    print('\nratios'); print(json.dumps(ratios, indent=1))
    print('\nlargest relative difference between the two launches (counters above 1%):',
          {k: round(v, 3) for k, v in spread.items() if v > 0.01})
    print('\nclock log of the profiling session:', res['clock_log'])
    for dev in ('dgpu', 'igpu'):
        print(f'\nsecond run, {dev}, mean kernel time by group size (a new key for every size), ms, and ratio search / branch-free')
        for inp, d in res[f'kernels2_{dev}'].items():
            for step, v in d.items():
                print(f"  {inp:8s} {step:22s} {v['min']:.2f}-{v['max']:.2f}  ", ' '.join(f'{g}:{t:.2f}' for g, t in sorted(v['by_group'].items())))


if __name__ == '__main__':
    main()
