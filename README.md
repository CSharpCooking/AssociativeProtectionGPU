# GPU Offloading of the Associative Data Protection Transform in .NET: materials

[Русская версия](README_ru.md)

Materials accompanying the paper *GPU Offloading of the Associative Data Protection Transform in .NET: A Performance
Evaluation* by Ruslan Gibadullin and Dmitry Georgiev (under review): the benchmark results of configuration A, the
code of the library and of its benchmarks, the measurement program of configurations B and C with its raw results and
inputs, the reports of the NIST SP 800-22 tests, and the scripts that recompute the tables and figures of the paper.

> **The paper evaluates performance, not security.** Nothing in this repository establishes the security of the
> transform. The filler function of the GPU kernels is an unkeyed hash of public indices and a 32-bit seed per chunk;
> passing the NIST SP 800-22 tests does not make it a cryptographic generator.

## Contents

| Path | Content | Paper |
|---|---|---|
| [`library/`](library/) | Listings of the CpuGpuStego library and of the BenchmarkDotNet benchmarks of configuration A, with the correspondence between the class names of the code and of the paper | Sections 4–5, 6.2, 6.4 |
| [`results/configuration_A/`](results/configuration_A/) | BenchmarkDotNet summary reports of configuration A (images) and their values (JSON, CSV) | Tables 4–6, Figures 2–3, Sections 7.2–7.4, 7.8, Table S1 |
| [`measurement/AssocMeasure/`](measurement/AssocMeasure/) | .NET 8 console program of the CPU-side measurements on configurations B and C | Sections 5.2–5.3, 6.3–6.6 |
| [`measurement/run_linux.sh`](measurement/run_linux.sh), [`run_windows.cmd`](measurement/run_windows.cmd) | Scripts that run the complete set of these measurements | |
| [`measurement/laptop/`](measurement/laptop/) | Code for further GPU measurements (configuration A or another laptop with a GPU), with a standalone build; not used in the paper | Section 8.7 |
| [`data/`](data/) | The six 10 MB inputs of configurations B and C, their checksums and origin | Section 6.3 |
| [`results/configuration_B/`](results/configuration_B/), [`results/configuration_C/`](results/configuration_C/) | Raw per-iteration results, logs, key checks, NIST SP 800-22 reports | Tables 7–9, Figure 4, Sections 7.1, 7.5–7.7, Tables S2–S4 |
| [`results/Supplementary_Tables_S1-S4.xlsx`](results/Supplementary_Tables_S1-S4.xlsx) | Tables S1–S4 to which the paper refers: complete statistics of configuration A, measurements of B and C with raw iterations, paired measurements, NIST results | Section 6.7 |
| [`analysis/`](analysis/) | Python scripts that recompute Tables 4–9 and Figures 2–4 from `results/` | |

## Configurations

| | A | B | C |
|---|---|---|---|
| Use | CPU and GPU benchmarks | CPU-side analysis, key checks, filler tests | CPU-side analysis, test of the system generator |
| Computer | Laptop Acer Nitro V 15 | Virtual machine in a cloud environment | Laptop HP OMEN 15-dc1xxx |
| CPU | Intel Core i5-13420H (4P + 4E cores, 12 threads) | 2 vCPUs of an Intel Xeon processor (AVX-512, AES-NI) | Intel Core i5-9300H (4 cores, 8 threads) |
| Memory | 16 GB DDR5-5200 | 8 GB | 16 GB DDR4-2666 |
| GPUs | Intel UHD Graphics (OpenCL), NVIDIA GeForce RTX 4050 Laptop GPU (CUDA) | none | not used |
| Software | Windows 11 25H2, .NET 7.0.16, BenchmarkDotNet 0.15.8, ILGPU 1.5.3 | Ubuntu 24.04.5, .NET 8.0.32, OpenSSL 3.0.13 | Windows 11 (build 26300), .NET 8.0.33 |

Table 3 of the paper gives the complete description. The GPU measurements of configuration A were made with the
benchmark class in `library/StegoBenchmarks.cs`; configurations B and C ran `measurement/AssocMeasure`.

## Reproducing the analysis

Python 3.10 or later with NumPy and Matplotlib (`pip install -r analysis/requirements.txt`):

```
python3 analysis/config_a.py        # Tables 4-6 and the derived values of configuration A
python3 analysis/summarize.py B     # configuration B: formation, recognition, paired design, NIST
python3 analysis/summarize.py C     # configuration C
python3 analysis/paper_values.py    # Tables 4-9 and the values quoted in the text -> analysis/output/paper_values.md
python3 analysis/figures.py         # Figures 2-4 -> analysis/output/
```

`analysis/run_all.sh` runs all five. Details: [`analysis/README.md`](analysis/README.md).

## Repeating the CPU measurements

With the .NET 8 SDK, `measurement/run_linux.sh` (Linux) or `measurement\run_windows.cmd` (Windows) builds the
program, runs the complete set of measurements of configurations B and C (about one hour), and writes the results to
`results_run/`. The program uses no NuGet packages and no GPU. Commands and options:
[`measurement/README.md`](measurement/README.md). The NIST SP 800-22 tests are run with `analysis/run_nist.sh`;
the published GPU filler sequences are regenerated from their seeds (`results/configuration_B/filler_seeds.txt`).

## What is not included

* The input texts of configuration A: UTF-8 files of 1 KB to 100 MB formed from the Russian-language novel
  *Kogda truba zovet* by O. Selyankin and the text of a 3.45 MB book. They are protected by copyright;
  [`data/README.md`](data/README.md) gives the size and SHA-256 hash of the novel file and the script that computes the
  statistics reported for it.
* A buildable project of the library: the listings come from the appendices of the master's thesis on which the paper
  is based and lack the `BitVector` class and the project files (see [`library/README.md`](library/README.md)).
* Hardware reports with serial numbers, user names and local paths: paths in the NIST reports were shortened to
  relative ones.

## License

The code, data and texts of the authors in this repository are released under the [MIT License](LICENSE).
Third-party content keeps its own terms: `data/en_10MB.bin` (license texts and copyright notices distributed with
Ubuntu) and `data/zip_10MB.bin` (portions of .NET assemblies under the MIT License of the .NET Foundation,
`data/LICENSE-dotnet.txt`); see [`data/README.md`](data/README.md).

## Citation

If you use these materials, please cite the paper (the reference will be added on publication).
