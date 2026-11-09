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

## Revision 2: key validation (after the measurements)

After the measurements of the paper, the library was extended to validate keys by building the recognition table of
Section 5.2 (`KeyValidation.cs`; the changed lines of `CpuGpuStego.cs` are marked `[r2]`). The table is built whenever a
key is set (`AssocStego.Key`), generated (`CreateKey`), read from a key file (`LoadKeyFromFile`) or restored from the
header of a protected file (`StegoLibrary.DecryptFile`), and the key is rejected with `KeyValidationException` if

* a mask has bits outside the etalon length (bits 78 and 79 of the default etalons),
* the masks occupy more than L - 1 positions in total (9 for the ten etalons), which no key built by dichotomic
  partitioning does, or
* a pattern of the bits at these positions matches no etalon or more than one.

The check, like the table-driven recognition, applies only to keys built by dichotomic (binary) partitioning of the
etalon set (Proposition 1); keys built otherwise are rejected even if a search could decode their blocks. The validation
runs when the key is set, outside the operations measured in the paper. The version of the listing that was measured is
that of commit `23050a9`.

Tests: `tests/run_tests.sh [number of keys]` (requires the .NET 8 SDK) extracts the classes `EtalonFactory` and
`AssocStego` from the listing, compiles them with `KeyValidation.cs` and the `BitVector` stand-in, and checks generated
keys, keys written to a file and read back, keys with random masks or masks drawn from nine positions (compared with an
independent check of condition (5)), and key files with one flipped mask bit. Results of the run reported in the paper:
`results/key_validation/`.
