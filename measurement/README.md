# Measurement code

## AssocMeasure: CPU-side measurements of configurations B and C

A .NET 8 console program without NuGet packages and without a GPU. It runs the library code of the thesis on the CPU,
including the bodies of the GPU kernels, and measures every variant with a `Stopwatch`-based harness
(Section 6.4 of the paper).

| Folder | Content |
|---|---|
| `AssocMeasure/Thesis/` | Library code from Appendix 1 of the thesis (`AssocStego`, `AssocStegoStream`, `EtalonFactory`, bodies of the kernels of `GpuStegoEncryptor`/`GpuStegoDecryptor` in `ThesisKernels.cs`). `BitVector` is not in the appendix; `BitVector.cs` is a compatible implementation (bit order as in `AssocStego.CreateVector`). |
| `AssocMeasure/Shim/` | Minimal stand-ins for `ILGPU.Index1D` and `ILGPU.Runtime.ArrayView<T>`, so that the kernel bodies run as ordinary C# methods |
| `AssocMeasure/New/` | Table-driven and branch-free recognition kernels, `RecognitionTable` (Section 5.2), CPU variants of formation and recognition (Section 5.3) |
| `AssocMeasure/Bench/` | Measurement harness and Student's t quantiles |

Build and run from `measurement/AssocMeasure` (the scripts `run_linux.sh` and `run_windows.cmd` do all of this and
write to `results_run/` in the repository root):

```
dotnet build -c Release
dotnet bin/Release/net8.0/AssocMeasure.dll env                          # runtime and OS
dotnet bin/Release/net8.0/AssocMeasure.dll keys 100000                  # condition (5), Proposition 1, mask statistics (Section 7.1)
dotnet bin/Release/net8.0/AssocMeasure.dll equiv 100 1000000            # agreement of the four recognition procedures (Section 6.6)
dotnet bin/Release/net8.0/AssocMeasure.dll form ../../data/en_10MB.bin 10 2 form_en_10MB.json          # Table 7
dotnet bin/Release/net8.0/AssocMeasure.dll rec ../../data/uniform_10MB.bin 10 2 rec_uniform_10MB.json  # Table S2, one input
dotnet bin/Release/net8.0/AssocMeasure.dll dist 16 2 0 dist_10MB.json ../../data/uniform_10MB.bin ../../data/en_10MB.bin ../../data/ru_10MB.bin ../../data/zip_10MB.bin ../../data/zero_10MB.bin ../../data/ff_10MB.bin   # Table 8, Figure 4
dotnet bin/Release/net8.0/AssocMeasure.dll filler fillers 12500000      # sequences for the NIST tests, new seeds
dotnet bin/Release/net8.0/AssocMeasure.dll filler fillers 12500000 -1813208602,919601178,-429519325   # the published GPU sequences
```

`form` and `rec`: iterations and warm-up iterations per variant, a new key in every iteration; the JSON holds every
iteration. `dist`: number of keys, runs per key and input (the shorter is kept), `1` to include the branch-free
procedure; the same keys are used for every input. `keys` writes `keys.json` and the other commands write temporary
files to the folder `assoc_measure` of the system temporary directory. `filler` writes three sequences of the GPU filler
function (with the seeds given or drawn from the system generator) and one sequence of the system generator
(`csprng.bin`, not reproducible). On configuration B, `form` took about 14 minutes, `rec` about 4 minutes per input and
`dist` about 5 minutes. Run on an otherwise idle machine connected to mains power.

The only change made after the measurements is the optional seed list of `filler`.

## laptop: code for further GPU measurements on configuration A

Not used in the paper (Section 8.7 lists what was not measured). The classes use the library of the thesis and ILGPU;
they were checked against an emulation of the ILGPU runtime and stubs of BenchmarkDotNet (byte-exact results for every
mode, algorithm, chunk size and volume) but **were not compiled against ILGPU or BenchmarkDotNet themselves**.

| File | Content |
|---|---|
| `GpuStegoV2.cs`, `NewKernels.cs` | GPU encoder and decoder with the search, branch-free and table-driven recognition kernels; `groupSize > 0` loads the kernels with `LoadImplicitlyGroupedStreamKernel` (group-size sweep); `reuseDeviceBuffers: true` allocates the device buffers once per operation |
| `CpuParallelStego.cs` | Multithreaded CPU paths of the file scenario: chunk-wise system generator or the GPU filler function, search or table |
| `RevisionBenchmarks.cs` | BenchmarkDotNet class: full cycle of CPU-seq, CPU-par, CPU-par with the GPU filler, iGPU and dGPU on a dense grid of 1–100 KB and at 1, 10 and 100 MB, with parameters for the recognition procedure, chunk size, group size and buffer reuse; the input is a UTF-8 text file of at least 100 MB given in `ASSOC_INPUT` |

A kernel profile can be taken by running one recovery of `data/uniform_10MB.bin` and of `data/ff_10MB.bin` under
Nsight Compute (`ncu --set full -k regex:Decrypt …`): warp execution efficiency, branch efficiency, achieved occupancy,
DRAM throughput.
