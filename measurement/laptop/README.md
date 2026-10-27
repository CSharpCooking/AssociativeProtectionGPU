# Revision measurements on a laptop with a GPU

Code of the measurements added in the revision on a computer with a GPU. It was run on configuration D of the paper
(ASUS ROG Strix SCAR 18: Intel Core i9-14900HX, Intel UHD Graphics, NVIDIA GeForce RTX 4090 Laptop GPU); the results
are in [`results/configuration_D`](../../results/configuration_D) and are analyzed by
[`analysis/config_d.py`](../../analysis/config_d.py) (Sections 7.8–7.11 of the paper). It can be run unchanged on
another computer with a GPU, such as the laptop of configuration A.

There are two ways to build the code:

* **Inside the benchmark project of the thesis**, which has the library and the packages: copy the six `.cs` files of
  this folder into that project (details below).
* **Standalone** (`standalone/AssocRevision.csproj`), for a computer without that project: it compiles the library
  listing of the thesis (`library/CpuGpuStego.cs`), a compatible `BitVector` (`measurement/AssocMeasure/Thesis/`) and
  the files of this folder, with ILGPU 1.5.3, ILGPU.Algorithms 1.5.3 and BenchmarkDotNet 0.15.8 from NuGet. It needs
  the .NET 8 SDK and the folder layout of this repository.

The discrete GPU is selected with `AcceleratorSelector.Create(ExecutionMode.NvidiaDiscreteGpu)`, that is, through
CUDA, and the integrated GPU with `AcceleratorSelector.Create(ExecutionMode.IntegratedGpu)`, through OpenCL. If a
device is not available, its cases end with an error and the others run.

The kit was written against stand-ins for ILGPU and BenchmarkDotNet, because the packages could not be downloaded
where it was written, and was checked on an emulation of the ILGPU runtime. On configuration D, the standalone build
compiled against the real packages without changes (`results/configuration_D/build.log`), and all classes ran to
completion.

## Files

| File | Content |
|---|---|
| `RevisionBenchmarks.cs` | Six BenchmarkDotNet classes, one per group of measurements (table below) |
| `RevKernels.cs` | `rev-kernels`: kernel-only times on both GPUs (data resident on the device), the copies to and from the device, and the file I/O of the protected representation |
| `RevEntry.cs` | Dispatcher of the commands `rev`, `rev-kernels` and `rev-env` |
| `GpuStegoV2.cs`, `NewKernels.cs` | GPU encoder and decoder with the chunk size as a parameter and the search, branch-free and table-driven recognition kernels; copies of the original kernels |
| `CpuParallelStego.cs` | CPU paths of the file scenario: one generator call per range or the GPU filler function, search or table, sequential or on all threads |
| `run_revision.cmd` | Runs everything in order and collects the results in `results_revision` next to itself |
| `standalone/` | `AssocRevision.csproj`, `Program.cs` and `GlobalUsings.cs` of the standalone build |

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

## Running

Inside the benchmark project of the thesis:

1. Copy the six `.cs` files of this folder (not `standalone/`) into the benchmark project of the thesis (the one with
   `StegoBenchmarks`).
2. In `Program.Main`, insert as the first statement
   `if (args.Length > 0 && args[0].StartsWith("rev")) { RevEntry.Run(args); return; }`
   (or `return RevEntry.Run(args);` if `Main` returns `int`).
3. Put `run_revision.cmd` into the project folder.

Standalone: nothing to prepare; `run_revision.cmd` stays in this folder and builds `standalone/AssocRevision.csproj`
when there is no `.csproj` next to it.

Then run, from a console (not from Visual Studio, so that no debugger is attached), on mains power:

```
run_revision.cmd <novel.txt> <repository>\data
```

The first argument is a UTF-8 text file, such as the novel used on configuration A; on configuration D, the Russian
text `data/ru_10MB.bin` was used, whose numbers of etalon comparisons per byte are those of the novel (Section 6.3 of
the paper). The second argument is the `data` folder of this repository. A single group is run with a third argument, e.g. `run_revision.cmd novel.txt ..\..\data formation`;
`formation` (a few minutes) is a good first test. On configuration D, the groups took 2, 6, 1, 17, 4 and 9 minutes
(break-even grid, 100 MB full cycle, formation, chunk size, recovery, recognition), plus a few minutes for the
kernels; on a slower computer, the reference CPU implementation makes the run several times longer. Every BenchmarkDotNet group writes its reports and its log,
with the times of all warm-up and measured iterations, to `results_revision\<class>`; `env.txt` records the runtime,
the packages, the devices and their drivers, and the power settings.

## Kernel profiles (optional)

With Nsight Compute installed and access to the GPU performance counters allowed, the kernel launches of
`rev-kernels` on the discrete GPU can be profiled, from the folder of the project that was built:

```
ncu --target-processes all --set full --export rev_kernels_dgpu dotnet run -c Release --no-build -- rev-kernels <repository>\data 1 dGPU
ncu --import rev_kernels_dgpu.ncu-rep --page details --csv > rev_kernels_dgpu.csv
```

Of interest are the average number of active threads per warp, the branch efficiency, the achieved occupancy and the
DRAM throughput of the search, table and branch-free kernels for `uniform` and `ff`.
