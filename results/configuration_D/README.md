# Configuration D: measurements of the revision on a second laptop with a GPU

Laptop ASUS ROG Strix SCAR 18 (G834JYR): Intel Core i9-14900HX (8 performance and 16 efficient cores, 32 threads),
31.6 GB of memory visible to the operating system; Intel UHD Graphics (Raptor Lake-S, OpenCL, driver
31.0.101.5186) and NVIDIA GeForce RTX 4090 Laptop GPU (16 GB, CUDA, driver 610.88); Windows 11 Home 25H2
(build 26200.9711), .NET 8.0.33 (SDK 8.0.427), BenchmarkDotNet 0.15.8, ILGPU 1.5.3. On mains power, with the power
scheme "Silent" of the vendor active; BenchmarkDotNet switched the power plan of Windows to "High performance" for
every benchmark and restored "Silent" afterwards (lines `Setup power plan` and `Successfully reverted power plan` of
the logs). The kernel-only times (`kernels.csv`) were measured outside BenchmarkDotNet, under "Silent".
Used in Sections 7.8–7.11 of the paper and in Tables S5 and S6.

The measurements were made with the kit in [`measurement/laptop`](../../measurement/laptop), built standalone
(`standalone/AssocRevision.csproj`, see `build.log`) and run with `run_revision.cmd ..\..\data\ru_10MB.bin ..\..\data`:
the text input of the full-cycle, formation and recovery benchmarks is the Russian text `data/ru_10MB.bin`, read as
UTF-8 and repeated or truncated to the required size; the recognition benchmarks and the kernel-only times use
`uniform`, `ff`, `zero` and `ru` of [`data/`](../../data). The files are as they were delivered; times in the logs
and file names are the local time of the laptop.

## Files

| File | Content |
|---|---|
| `env.txt` | Runtime, packages, devices with their backends, `dotnet --info`, `nvidia-smi`, operating system, processor, memory, computer model, video drivers, mains power, power scheme |
| `build.log` | Build of the kit |
| `<Class>/results/<Class>-report.csv`, `-report-github.md`, `-report.html` | BenchmarkDotNet summary of each class (CSV: `;` as separator, `,` as thousands separator, units in the values) |
| `<Class>/<Class>-<date>-<time>.log` | BenchmarkDotNet log: the time of every warm-up and measured iteration of every process (`WorkloadWarmup`, `WorkloadActual`; `WorkloadResult` = after the subtraction of the overhead and the removal of upper outliers), garbage collections, power plan |
| `<Class>.console.log` | Console output of the class |
| `kernels.csv`, `kernels.err` | Output of `rev-kernels` (the error stream was empty) |

The classes and their jobs (processes × warm-up + measured iterations) are described in
[`measurement/laptop/README.md`](../../measurement/laptop/README.md): `RevBreakEven` 3 × (5 + 30), `RevFormation`,
`RevRecovery` and `RevRecognition` 1 × (3 + 15), `RevFullCycle` and `RevChunkSize` 1 × (2 + 10). `RevFormation` has
two logs: the first is the trial run of this class made before the complete run, 10 minutes earlier; the reports
belong to the second.

`kernels.csv` has the header `device,input,step,rep,ms`, but its times were written with the decimal comma of the
Russian locale, so that every line has six fields: `iGPU,uniform,search kernel,0,202,727` is 202.727 ms. Steps: the
formation kernel and the search, table-driven and branch-free recognition kernels over the whole 10 MB input in one
launch, with the data in device memory; the copies of the input (10 MB) and of the protected representation (300 MB)
to and from the device; the writing and reading of the protected representation to and from a file in one call of
300 MB. Every launch or copy was timed with a `Stopwatch` up to the end of `Synchronize`; one warm-up repetition
(not printed) and ten measured ones; one key per device and input; every recognition was compared with the input
byte by byte. Later versions of the kit write the times with a decimal point.

[`analysis/config_d.py`](../../analysis/config_d.py) reads these files, checks the logs against the reports and
computes the tables of the paper.
