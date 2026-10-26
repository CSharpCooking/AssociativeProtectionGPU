# Library and benchmarks of configuration A

| File | Content |
|---|---|
| `CpuGpuStego.cs` | The CpuGpuStego library: facade, key and etalon model, sequential CPU path, GPU encoder and decoder (ILGPU kernels), accelerator selection, file format |
| `StegoBenchmarks.cs` | The BenchmarkDotNet class with which configuration A was measured (full cycle, formation, recovery for CPU-seq, iGPU and dGPU) |

Both files are the listings of Appendices 1 and 2 of the master's thesis on which the paper is based
(D. S. Georgiev, «Parallel implementation of an associative data protection mechanism using GPU computing»,
Kazan National Research Technical University named after A. N. Tupolev – KAI, 2026; supervisor R. F. Gibadullin),
reproduced as printed; only the indentation was restored. They are not a buildable project: the listings have no
`using` directives and no project files, and the `BitVector` class used by `AssocStego` is not part of them (a
compatible implementation is in `measurement/AssocMeasure/Thesis/BitVector.cs`). Dependencies: ILGPU 1.5.3, and
BenchmarkDotNet 0.15.8 for the benchmarks. The listing uses `ArgumentException.ThrowIfNullOrWhiteSpace`, an API of
.NET 8 and later, whereas the measurements ran under .NET 7.0.16; the listing is the final version of the code and may
differ in details from the build that was measured.

## Names in the paper and in the code

| Paper | Code |
|---|---|
| `AssocProtection` | `StegoLibrary` |
| `AssocModel` | `AssocStego` |
| `CpuAssocStream` | `AssocStegoStream` |
| `AcceleratorSelector` | `AcceleratorSelector` |
| `GpuBlockEncoder` | `GpuStegoEncryptor` |
| `GpuBlockDecoder` | `GpuStegoDecryptor` |
| `DecodeSection` | `DecodeContainer` (method of `GpuStegoDecryptor`) |

In the code, «шифрование» (encryption) is the formation of the protected representation and «дешифрование»
(decryption) is its recovery; the paper does not call the transform encryption.
