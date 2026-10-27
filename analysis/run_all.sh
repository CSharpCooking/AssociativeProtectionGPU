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
echo "reports: analysis/output/config_A.txt, summary_B.txt, summary_C.txt, config_D.txt, paper_values.md, lockstep_model.txt"
