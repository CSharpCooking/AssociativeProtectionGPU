# Configuration D, second run (revision 2)

Same laptop as in [`../configuration_D`](../configuration_D) (ASUS ROG Strix SCAR 18 G834JYR, Core i9-14900HX, Intel UHD
Graphics through OpenCL, NVIDIA GeForce RTX 4090 Laptop GPU through CUDA; Windows 11 build 26200, .NET 8.0.33,
BenchmarkDotNet 0.15.8, ILGPU 1.5.3), measured on 10 November 2026 with the kit of
[`measurement/laptop`](../../measurement/laptop): `run_revision2.cmd ..\..\data\ru_10MB.bin ..\..\data`
(instructions: `RUN_INSTRUCTIONS_R2_RU.md`). Used in Section 7.13 of the paper (Tables 17 and 18) and in Sections 8.1,
8.4, 8.5 and 9.

Differences from the first run: every benchmark ran in three processes; the High performance plan of Windows was
active during the whole run, including the kernel-only times (`power_plan.txt`); the vendor utility was in its Turbo mode
(`vendor_mode.txt`); the state of the discrete GPU was logged every 0.5 s (`gpu_clocks_all_*.csv`). The clock log has a
gap from 16:48:09 to 22:13:20 local time, after the end of `Rev2FullCycle`: the computer was apparently in sleep mode.
`Rev2FullCycleSeq` and the kernel-only times were measured after it; no benchmark was interrupted. The Nsight Compute
step of the kit (`ncu`) was run the next day; its results are in [`ncu/`](ncu/).

| File | Content |
|---|---|
| `env.txt`, `probe_*.txt`, `probe_kernels.csv` | Environment; output of the quick check made before the run |
| `build.log` | Build of the kit with the real packages |
| `Rev2BreakEven` | Full cycle at 1, 5, 10, 20, 30, 50, 100 and 1,024 KB: CPU-seq, CPU-opt, iGPU, iGPU-opt, dGPU, dGPU-opt (3 processes × 5 warm-up + 30 measured iterations) |
| `Rev2Formation`, `Rev2Recovery` | Formation and recovery of 10 MB (3 × 3 + 15) |
| `Rev2FullCycle`, `Rev2FullCycleSeq` | Full cycle of 100 MB (3 × 2 + 10; CPU-seq 3 × 1 + 5) |
| `<Class>/results/*`, `<Class>/*.log`, `<Class>.console.log` | BenchmarkDotNet reports, logs with the time of every iteration of every process, console output |
| `kernels2.csv`, `kernels2.err` | Kernel-only times for thread group sizes 0 (automatic), 32-1,024, ten repetitions; the two formation kernels and the three recognition kernels; a new key for every device, group size and input |
| `gpu_clocks_all_*.csv`, `gpu_state_*_start.txt`, `gpu_state_*_end.txt` | `nvidia-smi` log of the power state, clock rates, power, temperature and utilization of the discrete GPU; its state before and after the run |
| `power_plan.txt`, `vendor_mode.txt` | Power plan of Windows before and during the run; mode of the vendor utility as entered by the operator |
| `ncu/` | Kernel-level counters of the discrete GPU (NVIDIA Nsight Compute), 11 November 2026; see [`ncu/README.md`](ncu/README.md) |

Modes: `CPU-opt` is the optimized CPU path (`measurement/AssocMeasure/New/CpuOptStego.cs`); `-opt` are the V2 GPU classes
with device buffers reused between chunks, the formation kernel with coalesced stores and the table-driven kernel; `-ovl`
adds double buffering of the file input and output; `-table` is the original encoder with the table-driven decoder.
Analysis: `python3 analysis/config_d_r2.py` (process-level statistics, ratios, brackets of the break-even volume, kernel
and clock summaries); the files are as they were delivered, and times in the logs are the local time of the laptop.
