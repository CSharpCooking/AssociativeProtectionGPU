# Configuration A: BenchmarkDotNet results of the GPU measurements

Laptop Acer Nitro V 15: Intel Core i5-13420H, 16 GB DDR5-5200, M.2 NVMe PCIe 4.0 SSD; Intel UHD Graphics
(48 execution units, up to 1.4 GHz) through OpenCL and NVIDIA GeForce RTX 4050 Laptop GPU through CUDA;
Windows 11 25H2 (build 26200.8246), .NET 7.0.16, BenchmarkDotNet 0.15.8, ILGPU 1.5.3.
Library: [`library/CpuGpuStego.cs`](../../library/CpuGpuStego.cs); benchmark class:
[`library/StegoBenchmarks.cs`](../../library/StegoBenchmarks.cs), in their final version; the measurements were made
with an earlier build under .NET 7, which may differ from it in details (Section 6.7 of the paper). Used in Tables 4–6 and Sections 7.1–7.4 and 7.11
of the paper and in Table S1.

## Files

| File | Content |
|---|---|
| `reports/bdn_chunk<512\|1024>KB_<input>.png` | The 14 BenchmarkDotNet summary tables of the campaign (chunk sizes of 512 and 1024 KB × inputs of 1 KB, 10 KB, 100 KB, 1 MB, 10 MB, 100 MB and the 3.45 MB book), as reproduced in Appendix 4 of the thesis on which the paper is based |
| `bdn_statistics.json`, `bdn_statistics.csv` | The values of these tables (126 rows = 2 chunk sizes × 7 inputs × 3 modes × 3 operations) |

## Fields

| JSON | CSV | Meaning |
|---|---|---|
| `series` | `chunk_KB` | Chunk size of the GPU modes, KB |
| `size` | `input` | Input volume (1 KB = 1024 bytes, 1 MB = 2^20 bytes); `3.45MB` is the book |
| `name` | `mode`, `operation` | `CPU` = sequential reference implementation (CPU-seq), `iGPU`, `dGPU`; `full` = formation and recovery, `enc` = formation only, `dec` = recovery only |
| `N` | `N` | Number of measured iterations. The reports do not print it; it is (StdDev/StdErr)², which agrees with the reported Error |
| `Mean`, `StdDev`, `StdErr`, `Min`, `Q1`, `Median`, `Q3`, `Max` | `*_s` | Seconds |
| `Error` | `error_s` | Half-width of the 99.9% confidence interval of the mean, as reported by BenchmarkDotNet, seconds |
| `alloc` | `allocated` | Managed memory allocated per operation, as printed |
| `gen` | `gen*_per_1000` | Garbage collections of generations 0, 1, 2 per 1000 operations, as printed; `""`/`-` = none; `null`/`n/a` = column absent from the report |

Method names in the reports (Russian) and their names here: «CPU - Шифрование и дешифрование» = `CPU full`,
«Интегрированное GPU - Шифрование и дешифрование» = `iGPU full`, «Дискретное GPU - Шифрование и дешифрование» =
`dGPU full`, «Только шифрование CPU / интегрированное GPU / дискретное GPU» = `CPU/iGPU/dGPU enc`,
«Только дешифрование …» = `… dec` (шифрование = formation, дешифрование = recovery).

The values were transcribed from the report images and every value was checked against them. The inputs were UTF-8
text files formed from a Russian-language novel and the text of a 3.45 MB book; they are protected by copyright and
are not included (see [`data/README.md`](../../data/README.md)). In the 1024 KB series, the volumes of 3.45 MB and
above were measured under different conditions and are not comparable with the 512 KB series (Section 7.11 of the paper).
