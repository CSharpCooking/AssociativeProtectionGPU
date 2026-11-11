#!/bin/bash
# Recomputes Tables 4-15, Figures 2-5 and the lockstep model of the paper from results/ (output in analysis/output/).
set -e
cd "$(dirname "$0")/.."
mkdir -p analysis/output
python3 analysis/config_a.py > analysis/output/config_A.txt
python3 analysis/summarize.py B > analysis/output/summary_B.txt
python3 analysis/summarize.py C > analysis/output/summary_C.txt
python3 analysis/config_d.py > analysis/output/config_D.txt
python3 analysis/paper_values.py > /dev/null
python3 analysis/lockstep_model.py > analysis/output/lockstep_model.txt
python3 analysis/figures.py
# revision 2: optimized CPU path on configuration B; bounds and process-level intervals of configuration D
python3 analysis/opt_summary.py > analysis/output/opt_B.txt
python3 analysis/r2_bounds.py > analysis/output/r2_bounds.txt
python3 analysis/config_d_r2.py > analysis/output/config_D_r2.txt
# kernel-level counters of the discrete GPU of configuration D (Nsight Compute) and the control paths of a warp
python3 analysis/ncu_counters.py > analysis/output/ncu_counters.txt
python3 analysis/control_paths.py > analysis/output/control_paths.txt
echo "reports: analysis/output/config_A.txt, summary_B.txt, summary_C.txt, config_D.txt, paper_values.md, lockstep_model.txt, opt_B.txt, r2_bounds.txt, config_D_r2.txt, ncu_counters.txt, control_paths.txt"
