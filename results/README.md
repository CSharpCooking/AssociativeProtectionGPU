# Results

* [`configuration_A/`](configuration_A/): BenchmarkDotNet results of the GPU measurements (own README).
* `configuration_B/`: virtual machine, 2 vCPUs of an Intel Xeon processor, 8 GB, Ubuntu 24.04.5, .NET 8.0.32,
  OpenSSL 3.0.13.
* `configuration_C/`: laptop HP OMEN 15-dc1xxx, Intel Core i5-9300H, 16 GB DDR4-2666, Windows 11 (build 26300),
  High performance power plan, .NET 8.0.33; produced with `measurement/run_windows.cmd`.
* `Supplementary_Tables_S1-S4.xlsx`: Tables S1–S4 to which the paper refers (Section 6.7).

All results of configurations B and C were obtained with `measurement/AssocMeasure` on the inputs in `data/`. Times
are in seconds unless the field name says otherwise.

| File | Content | Paper |
|---|---|---|
| `form_en_10MB.json` | Formation variants on `en_10MB.bin`. Per variant: `Name`, `N` (measured iterations), `Mean`, `SD`, `SE`, `Err999` (half-width of the 99.9% confidence interval), `Median`, `Min`, `Max` and `Raw` (every iteration), for the whole 10 MiB; a new key in every iteration, 2 warm-up iterations | Table 7, Section 7.5, Table S2 |
| `rec_<input>_10MB.json` | Recognition variants on each input, same fields; `tableBuild`: construction of the recognition table (30 builds for one key) | Table S2, Section 7.6 |
| `dist_10MB.json` | Paired design: for each of 16 keys and each input, `m` = \|P\|, `bytesPerTriedEtalon` (key descriptor, counted on the first 64 KB of the uniform input), `searchMsPerMB` and `tableMsPerMB` (shorter of two runs); `branchFreeMsPerMB` = −1: not measured in this design | Table 8, Figure 4, Table S3 |
| `*.log` | Console output of these runs (configuration B: of the paired design only) | |
| `configuration_B/keys_100000.json` | Checks of 100,000 generated keys: mean mask weight, histograms of the mask weights (0–10) and of \|P\| (0–16), violations of condition (5) and of Proposition 1, repeated positions | Section 7.1 |
| `configuration_B/equivalence.txt` | Agreement of the four recognition procedures (100 keys × 10⁶ bytes) | Sections 6.6, 7.1 |
| `configuration_B/filler_seeds.txt` | Seeds of the three GPU filler sequences tested | Section 6.5 |
| `*/nist/` | NIST SP 800-22 reports: `<label>_finalAnalysisReport.txt`, the console log and the statistics of every test (`<label>_detail/`). Labels: `gpu_filler_0..2` (GPU filler function), `csprng` (OpenSSL, configuration B), `csprng_win` (BCryptGenRandom, configuration C) | Table 9, Table S4 |
| `configuration_B/nist_followup/` | Block Frequency test on six further GPU filler sequences; `seeds.txt` | Section 7.7 |
| `configuration_C/env.txt` | Runtime, SDK, processor, computer model, memory and power plan | Table 3 |

The input files of the GPU filler sequences are regenerated with
`AssocMeasure filler <dir> 12500000 <seeds>` (see `measurement/README.md`); those of the system generators are not
reproducible and are not included. Edits made for publication: in line 4 of every NIST report, the path of the input
file was shortened to a relative path; in `configuration_C/env.txt`, the line printed by `powercfg` was converted from
code page 866 to UTF-8; in the JSON files of configuration C, the absolute input paths were replaced by `../data/<file>`.
