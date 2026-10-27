# Revision measurements on configuration A

Code for further measurements on the laptop of the GPU measurements (configuration A: Intel Core i5-13420H,
Intel UHD Graphics through OpenCL, NVIDIA GeForce RTX 4050 Laptop GPU through CUDA). These measurements are not used
in the paper. Section 8.7 of the paper lists what they would add.

The classes are meant to be added to the benchmark project of the thesis, which has the library and the packages
(ILGPU 1.5.3, BenchmarkDotNet 0.15.8). They use its classes `AssocStego`, `AssocStegoStream`, `EtalonFactory`,
`GpuStegoEncryptor`, `GpuStegoDecryptor` and `AcceleratorSelector`. They were compiled against stand-ins for ILGPU
and BenchmarkDotNet, because the packages could not be downloaded where the kit was written. On an emulation of the
ILGPU runtime, every case of every class recovered its input byte by byte without leaking device buffers. They
**were not compiled against the real packages**, so a small adjustment may be needed on the laptop.

## Files

| File | Content |
|---|---|
| `RevisionBenchmarks.cs` | Six BenchmarkDotNet classes, one per group of measurements (table below) |
| `RevKernels.cs` | `rev-kernels`: kernel-only times on both GPUs (data resident on the device), the copies to and from the device, and the file I/O of the protected representation |
| `RevEntry.cs` | Dispatcher of the commands `rev`, `rev-kernels` and `rev-env` |
| `GpuStegoV2.cs`, `NewKernels.cs` | GPU encoder and decoder with the chunk size as a parameter and the search, branch-free and table-driven recognition kernels; copies of the original kernels |
| `CpuParallelStego.cs` | CPU paths of the file scenario: one generator call per range or the GPU filler function, search or table, sequential or on all threads |
| `run_revision.cmd` | Runs everything in order and collects the results in `results_A_revision` |

## Groups

All groups use the file-based scenario of the paper and the iteration setup of the thesis benchmarks: a new key in
every iteration, written to a key file and read back, and the encoder and decoder objects created in the setup,
outside the measured interval. Jobs: invocation count and unroll factor 1, fixed numbers of warm-up and measured
iterations, so that the reported counts are the actual ones.

| Class | What | Cases | Job (processes × warm-up + measured) | Reviewer comments |
|---|---|---|---|---|
| `RevBreakEven` | Full cycle with verification at 1, 10, 20, 30, 40, 50, 60, 75, 100 KB; CPU-seq and the original GPU classes | 27 | 3 × (5 + 30) | R1 5.1; R2 4, 5; R3 5 |
| `RevFullCycle` | Full cycle of 100 MB: multithreaded CPU (chunk-wise generator or GPU filler function, search or table) and both GPUs (search or table) | 7 | 1 × (2 + 10) | R1 2.5; R2 2; R3 2 |
| `RevFormation` | Formation of 10 MB: CPU-seq, sequential with one generator call per range or with the GPU filler function, the same on all threads, both GPUs | 7 | 1 × (3 + 15) | R2 3; R3 2 |
| `RevChunkSize` | Full cycle of 100 MB with chunks of 256, 512, 1024 and 2048 KB in one build and one run; the original classes at 512 KB; CPU-seq as a control of the state of the machine | 11 | 1 × (2 + 10) | R1 2.1; R2 8; R3 3 |
| `RevRecovery` | Recovery of 10 MB: search and table on the CPU (sequential and on all threads), original and revision search kernels, table and branch-free kernels on both GPUs | 12 | 1 × (3 + 15) | R1 1.3, 1.4, 4; R3 4 |
| `RevRecognition` | Recovery of the 10 MB inputs uniform, all-255, all-zero and Russian text of `data/` by CPU-seq, the CPU table and the three GPU kernels | 32 | 1 × (3 + 15) | R1 4, 5.2; R3 4 |
| `rev-kernels` | Kernel-only times of the formation kernel and the three recognition kernels on the same four inputs, copies to and from the device, file write and read | 8 runs | 1 warm-up + 10 | R1 2.3, 4; R3 4 |

The comparison of `uniform` with `ff` separates the effect of divergence from that of the amount of work. With the
search, a byte of `uniform` costs 12.3 comparisons on average and 22.9 at the level of a group of 32 threads; a byte
of `ff` costs 15.0 at both levels. If divergence limits the search kernel, `uniform` takes longer than `ff`; if the
amount of work does, `ff` takes longer; if memory traffic does, both take about the same time.

## Integration

1. Copy the six `.cs` files into the benchmark project of the thesis (the one with `StegoBenchmarks`).
2. In `Program.Main`, insert as the first statement
   `if (args.Length > 0 && args[0].StartsWith("rev")) { RevEntry.Run(args); return; }`
   (or `return RevEntry.Run(args);` if `Main` returns `int`).
3. Put `run_revision.cmd` into the project folder and run, from a console (not from Visual Studio, so that no
   debugger is attached):
   `run_revision.cmd <novel.txt> <repository>\data`
   The first argument is the text file of the novel used in the paper; the second is the `data` folder of this
   repository. A single group is run with a third argument, e.g. `run_revision.cmd novel.txt ..\data formation`;
   `formation` (about 3 minutes) is a good first test.

The whole run takes about 1–1.5 h. Every BenchmarkDotNet group writes its reports and its log, with the times of
all warm-up and measured iterations, to `results_A_revision\<class>`.

## Kernel profiles (optional)

With Nsight Compute installed and access to the GPU performance counters allowed, the kernel launches of
`rev-kernels` on the discrete GPU can be profiled:

```
ncu --target-processes all --set full --export rev_kernels_dgpu dotnet run -c Release --no-build -- rev-kernels <repository>\data 1 dGPU
ncu --import rev_kernels_dgpu.ncu-rep --page details --csv > rev_kernels_dgpu.csv
```

Of interest are the average number of active threads per warp, the branch efficiency, the achieved occupancy and the
DRAM throughput of the search, table and branch-free kernels for `uniform` and `ff`.
