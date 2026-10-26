#!/bin/bash
# Recomputes Tables 4-9 and Figures 2-4 of the paper from results/ (output in analysis/output/).
set -e
cd "$(dirname "$0")/.."
mkdir -p analysis/output
python3 analysis/config_a.py > analysis/output/config_A.txt
python3 analysis/summarize.py B > analysis/output/summary_B.txt
python3 analysis/summarize.py C > analysis/output/summary_C.txt
python3 analysis/paper_values.py > /dev/null
python3 analysis/figures.py
echo "reports: analysis/output/config_A.txt, summary_B.txt, summary_C.txt, paper_values.md"
