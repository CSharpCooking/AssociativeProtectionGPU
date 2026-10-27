# Analysis scripts

Python 3.10+ with NumPy and Matplotlib. Run from the repository root; output goes to `analysis/output/`.
`analysis/run_all.sh` runs `config_a.py`, `summarize.py B`, `summarize.py C`, `config_d.py`, `paper_values.py` and
`figures.py` in this order.

| Script | Input | Output | Paper |
|---|---|---|---|
| `config_a.py` | `results/configuration_A/bdn_statistics.json` | `config_A.json`; report on stdout | Tables 4–6; fixed and marginal costs, allocation experiment, crossing points (Sections 7.2–7.4); comparison of the two chunk-size series (Section 7.11) |
| `summarize.py B`, `summarize.py C` | `results/configuration_B/` or `_C/`, `data/` | `summary_B.json`, `summary_C.json`; report on stdout | Tables 7, 8, 9, S2, S3; Sections 7.5–7.7 |
| `config_d.py` | `results/configuration_D/` (BenchmarkDotNet CSV reports and logs, `kernels.csv`) | `config_D.json`; report on stdout | Tables 10–15 and the values summarized in Tables S5 and S6; Sections 7.8–7.11 (break-even grid with process means, multithreaded CPU paths, filler effect, recognition kernels with their ratios within a repetition, decomposition of the stages, chunk size). Computes mean, SD, SE and CI from the iteration times of the logs and checks them and N against the reports |
| `paper_values.py` | the four JSON files above | `paper_values.md` | Rows of Tables 4–15 and the derived values quoted in the text (stage sums against the full cycle, generator-call share, ratios of the recognition procedures, correlations, NIST counts) |
| `figures.py` | the JSON files above and `dist_10MB.json` of configurations B and C | `fig2.png` … `fig5.png` | Figures 2–5 |
| `input_stats.py FILE…` | any files | stdout | Size, SHA-256 and the comparison counts of the search per byte (Section 6.3, Table 8) |
| `run_nist.sh` | a binary file | NIST STS reports | Table 9, Table S4 (see the header of the script) |

Conventions: times in the JSON files of `results/` are in seconds; *Error* and *Err999* are half-widths of the 99.9%
confidence interval of the mean (Student's t). A speedup *S* is given with the conservative interval
[(T₁ − e₁)/(T₂ + e₂), (T₁ + e₁)/(T₂ − e₂)] from the two confidence intervals. Values are rounded half up.
The summaries of configurations B and C compute the comparison counts directly from the files in `data/`.
