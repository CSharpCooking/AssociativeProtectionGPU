# Configuration D: kernel-level counters of the discrete GPU (NVIDIA Nsight Compute)

Group `ncu` of the kit ([`measurement/laptop/run_revision2.cmd`](../../../measurement/laptop/run_revision2.cmd)), run on
11 November 2026, the day after the second run, on the same laptop (NVIDIA GeForce RTX 4090 Laptop GPU through CUDA,
ILGPU 1.5.3), from a console with administrator rights, under the High performance plan of Windows and the Turbo mode of
the vendor utility. Used in Section 7.14 of the paper (Table 19) and in Sections 8.2 and 8.8.

For each of the two 10 MB inputs, uniform and all-255 (`ff`), the kernel-only program of the first kit
(`rev-kernels <data> 1 dGPU <input>`, [`RevKernels.cs`](../../../measurement/laptop/RevKernels.cs)) ran under
`ncu --target-processes all --set full` (Nsight Compute 2026.4.0). The program creates a new key for the input and
launches the formation kernel and the three recognition kernels (search, table-driven, branch-free) over the whole input
twice, a warm-up launch and the launch that it times; Nsight Compute profiled all eight launches, each in 45 passes. The
thread group size is that chosen by ILGPU (768 threads). The keys were not recorded and differ from those of the timing
runs; on the all-255 input the control flow of the search kernel depends on the key (Section 7.14).

| File | Content |
|---|---|
| `ncu_metrics.csv` | All metric values of the 16 profiled launches (`input`, `launch` 0-7, `kernel`, `metric`, `value`; 1,784 metrics per launch), extracted from the reports by [`analysis/ncu_report.py`](../../../analysis/ncu_report.py) |
| `ncu_dGPU_uniform.log`, `ncu_dGPU_ff.log` | Console output of the two runs: the profiled launches and the times printed by the program (these include the profiling passes and are not kernel times) |
| `ncu_version.txt` | Version of Nsight Compute |
| `gpu_clocks_ncu_*.csv`, `gpu_state_ncu_start.txt`, `gpu_state_ncu_end.txt` | `nvidia-smi` log of the discrete GPU during the session (every 0.5 s) and its state before and after it |
| `power_plan.txt`, `vendor_mode.txt`, `build.log`, `files.txt` | Power plan before and during the session; mode of the vendor utility as entered by the operator; build log; list of the delivered files |

The two reports themselves (`ncu_dGPU_uniform.ncu-repz`, 75 MB, and `ncu_dGPU_ff.ncu-repz`, 64 MB) are not included
because of their size; they also contain the host name of the computer. Nsight Compute 2026.4 writes compressed reports
(`.ncu-repz`), so the CSV export step of the kit, which looked for `.ncu-rep`, produced nothing; the metrics were read
from the reports with `analysis/ncu_report.py`, and the reading was checked against the rule texts stored in the reports,
which quote the number of active threads per warp and the numbers of sectors.

Analysis: `python3 analysis/ncu_counters.py` (counters of Table 19, ratios between the kernels, clock rate during the
session, search times of the second run by key) and `python3 analysis/control_paths.py` (number of control paths in a
warp by key). Times in the logs are the local time of the laptop.
