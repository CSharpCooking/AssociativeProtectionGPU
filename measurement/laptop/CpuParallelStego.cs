// CPU paths of the file-based scenario of the revision benchmarks (no ILGPU types; plain arrays).
// Forward: every 512 KB chunk is formed range by range (16 KB of source data per range) into a reusable buffer
//          and written to the file; the filler is generated either by one RandomNumberGenerator.Fill call
//          per range or by the filler function of the GPU kernels (PseudoRandomByte, one seed per chunk).
// Inverse: chunks are read from the file and recognized range by range by the original search
//          (AssocStego.DiscloseByteFromBuffer) or by the table-driven procedure (RecognitionTable).
// parallel = true processes the ranges with Parallel.ForEach on all hardware threads, false one after another.
// Requires AssocStego (library) and RecognitionTable (NewKernels.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Security.Cryptography;

public enum CpuRecognition { Search, Table }

public static class CpuParallelStego
{
    public const int ChunkSize = 512 * 1024;   // source bytes per chunk, as in the GPU classes
    public const int Range = 16 * 1024;        // source bytes per work item

    private static void ForRanges(int n, bool parallel, Action<Tuple<int, int>> body)
    {
        if (parallel) Parallel.ForEach(Partitioner.Create(0, n, Range), body);
        else for (int a = 0; a < n; a += Range) body(Tuple.Create(a, Math.Min(n, a + Range)));
    }

    public static void EncryptToFile(byte[] input, string path, AssocStego st, bool gpuFiller, bool parallel = true)
    {
        int c = st.ContainerByteLength, h = st.HiddenByteLength;
        byte[] e = st.FlattenedEtalonsBytes, k = st.FlattenedKeysBytes;
        byte[] chunk = new byte[Math.Min(ChunkSize, Math.Max(input.Length, 1)) * h];
        using var fs = new FileStream(path, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
        for (int start = 0; start < input.Length; start += ChunkSize)
        {
            int n = Math.Min(ChunkSize, input.Length - start);
            int seed = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
            ForRanges(n, parallel, r =>
            {
                if (!gpuFiller)
                    RandomNumberGenerator.Fill(chunk.AsSpan(r.Item1 * h, (r.Item2 - r.Item1) * h));
                for (int i = r.Item1; i < r.Item2; i++)
                {
                    int v = input[start + i];
                    int d2 = v / 100, d1 = v % 100 / 10, d0 = v % 10;
                    if (gpuFiller)
                    {
                        Fill(chunk, i * h, start + i, 0, seed, c);
                        Fill(chunk, i * h + c, start + i, 1, seed, c);
                        Fill(chunk, i * h + 2 * c, start + i, 2, seed, c);
                    }
                    Mask(chunk, i * h, d2, e, k, c);
                    Mask(chunk, i * h + c, d1, e, k, c);
                    Mask(chunk, i * h + 2 * c, d0, e, k, c);
                }
            });
            fs.Write(chunk, 0, n * h);
        }
    }

    // s = r xor ((r xor e) and k), formula (3)
    private static void Mask(byte[] o, int off, int digit, byte[] e, byte[] k, int c)
    {
        int eo = digit * c;
        for (int j = 0; j < c; j++) { byte r = o[off + j]; o[off + j] = (byte)(r ^ ((r ^ e[eo + j]) & k[eo + j])); }
    }

    private static void Fill(byte[] o, int off, int globalIndex, int section, int seed, int c)
    {
        for (int j = 0; j < c; j++) o[off + j] = PseudoRandomByte(globalIndex, section, j, seed);
    }

    // identical to GpuStegoEncryptor.PseudoRandomByte
    public static byte PseudoRandomByte(int globalIndex, int digitIndex, int byteIndex, int blockSeed)
    {
        uint x = (uint)blockSeed;
        x += (uint)(globalIndex + 1) * 747796405u;
        x ^= (uint)(digitIndex + 1) * 2891336453u;
        x += (uint)(byteIndex + 1) * 277803737u;
        x ^= x >> 16;
        x *= 2246822519u;
        x ^= x >> 13;
        x *= 3266489917u;
        x ^= x >> 16;
        return (byte)x;
    }

    public static byte[] DecryptFromFile(string path, int length, AssocStego st, CpuRecognition alg, bool parallel = true)
    {
        int c = st.ContainerByteLength, h = st.HiddenByteLength;
        var result = new byte[length];
        RecognitionTable? tb = alg == CpuRecognition.Table
            ? RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, c, st.etalons.Length) : null;
        byte[] chunk = new byte[Math.Min(ChunkSize, Math.Max(length, 1)) * h];
        using var fs = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
        for (int start = 0; start < length; start += ChunkSize)
        {
            int n = Math.Min(ChunkSize, length - start);
            int need = n * h, got = 0;
            while (got < need) { int r = fs.Read(chunk, got, need - got); if (r == 0) throw new EndOfStreamException("unexpected end of the protected file"); got += r; }
            ForRanges(n, parallel, r =>
            {
                for (int i = r.Item1; i < r.Item2; i++)
                    result[start + i] = tb is null ? st.DiscloseByteFromBuffer(chunk.AsSpan(i * h, h)) : DecodeTable(chunk, i * h, tb, c);
            });
        }
        return result;
    }

    // Algorithm 1: gather the m bits of each section, look the digit up, restore the byte
    public static byte DecodeTable(byte[] y, int off, RecognitionTable tb, int c)
    {
        int x = 0;
        for (int s = 0; s < 3; s++)
        {
            int u = 0, b = off + s * c;
            for (int q = 0; q < tb.M; q++) u |= ((y[b + tb.PosByte[q]] >> tb.PosBit[q]) & 1) << q;
            x = x * 10 + tb.Table[u];
        }
        if (x > 255) throw new InvalidDataException("decoded value exceeds 255: corrupted block");
        return (byte)x;
    }
}
