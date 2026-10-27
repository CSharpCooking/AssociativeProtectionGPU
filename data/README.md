# Inputs

## Configurations B and C

Six inputs of 10 MiB (10,485,760 bytes) each, used for all measurements of configurations B and C and, `uniform`, `ff`, `zero` and `ru`, for the measurements of
configuration D (Section 6.3 of the
paper). `SHA256SUMS` holds their hashes; `python3 data/make_synthetic_inputs.py --check` verifies all six, and without
`--check` the script first regenerates the three synthetic ones. Comparisons per byte: mean number of etalon
comparisons of the sequential search, and its warp-level counterpart (`analysis/input_stats.py`).

| File | Content | Comparisons per byte | Warp level |
|---|---|---|---|
| `uniform_10MB.bin` | Uniformly distributed pseudorandom bytes: `numpy.random.default_rng(20261026).integers(0, 256, size=10485760, dtype=numpy.uint8)` | 12.3 | 22.9 |
| `zero_10MB.bin` | All bytes 0 | 3.0 | 3.0 |
| `ff_10MB.bin` | All bytes 255 | 15.0 | 15.0 |
| `en_10MB.bin` | English text in ASCII: the 17 license texts in `/usr/share/common-licenses` of Ubuntu 24.04 (`Apache-2.0` to `MPL-2.0`, in name order), followed by the 510 files `/usr/share/doc/*/copyright` of configuration B that contain only ASCII characters (in path order), joined with line feeds (3,189,371 bytes) and repeated up to 10 MiB | 10.2 | 21.5 |
| `ru_10MB.bin` | Russian text in UTF-8: the main text of the master's thesis on which the paper is based (D. S. Georgiev, KNRTU-KAI, 2026; see `library/README.md`) from the introduction to the conclusion, without headings and formula lines (233,639 bytes, 87% of which belong to non-ASCII characters; the last paragraph is the English version of the conclusion), repeated up to 10 MiB | 13.4 | 22.9 |
| `zip_10MB.bin` | The first 10 MiB of a gzip-compressed tar archive (PAX format) of the directory `shared/` of the .NET 8.0.32 installation of configuration B; this part holds assemblies of the ASP.NET Core shared framework. Close to uniformly distributed bytes from a real file | 12.3 | 22.9 |

Licenses: the synthetic inputs and `ru_10MB.bin` are covered by the MIT License of the repository (`../LICENSE`).
Third-party content keeps its own terms: `en_10MB.bin` consists of license texts and copyright notices distributed
with Ubuntu, each under the terms stated in it; `zip_10MB.bin` contains compressed portions of .NET assemblies, which
are licensed under the MIT license (`LICENSE-dotnet.txt`, Copyright (c) .NET Foundation and Contributors).

## Configuration A (not included)

The GPU measurements of configuration A (Tables 4–6) used UTF-8 text files of 1 KB, 10 KB, 100 KB, 1 MB, 10 MB and
100 MB formed from the Russian-language novel *Kogda truba zovet* by O. Selyankin by truncating or repeating it, and the
text of a book of 3.45 MB (3,613,792 bytes). These texts are protected by copyright and are not redistributed.
The novel file was 615,049 bytes of UTF-8 without a byte order mark, SHA-256
`26ae523c7c9165130f3d289a285aa7e77cd43a4a165c943667b6e63b210e6a53`. For it, `python3 analysis/input_stats.py <file>`
gives 13.4 comparisons per byte and 22.9 at warp level, the values reported in Section 6.3 of the paper.
