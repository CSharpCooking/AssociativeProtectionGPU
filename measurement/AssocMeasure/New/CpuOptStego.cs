// Optimized CPU path (CPU-opt, revision 2), in the file-based scenario of the CPU paths of configuration D
// (measurement/laptop/CpuParallelStego.cs: chunks of 512 KB of source data, ranges of 16 KB, Parallel.ForEach).
// Compared with those paths, it adds:
//  - SIMD masking: s = (r AND NOT k) OR (e AND k), formula (3), applied to the 30 bytes of a block with two 16-byte vector
//    operations (offsets 0 and 14; the overlap is idempotent), using per-value templates of the masks and masked etalons of
//    the three digit sections (256 values x 32 bytes each), instead of three scalar loops over ten bytes;
//  - table-driven recognition with byte-wise partial index tables: the index u of a section is the OR of one table entry for
//    each section byte that contains positions of P (at most nine bytes), instead of m single-bit extractions;
//  - buffer reuse: the chunk buffer, sized for min(input, chunk), the templates and the tables are allocated once per object,
//    outside the measured operation, and the restored data are written into an array supplied by the caller.
// The filler is produced by one RandomNumberGenerator.Fill call per range, as in CPU-par, so that the output has the same
// statistical source as the reference implementation. No ILGPU types are used.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Threading.Tasks;

public sealed class CpuOptStego
{
    public const int ChunkSize = 512 * 1024;   // source bytes per chunk, as in the GPU classes and CPU-par
    public const int Range = 16 * 1024;        // source bytes per work item, as in CPU-par
    private const int H = 30, C = 10, T = 32;  // block, section, template stride

    private readonly byte[] _maskT = new byte[256 * T];      // masks of the three sections of value v
    private readonly byte[] _etalT = new byte[256 * T];      // masked etalons of the three sections of value v
    private readonly int[] _jBytes;                           // section bytes that contain positions of P
    private readonly ushort[] _part;                          // _part[jj * 256 + b]: bits of u contributed by byte b at _jBytes[jj]
    private readonly byte[] _table;                           // L[u]
    private readonly byte[] _chunk;                           // reusable chunk buffer
    private readonly bool _parallel;

    public CpuOptStego(AssocStego st, RecognitionTable tb, int maxInputLength, bool parallel = true)
    {
        if (st.ContainerByteLength != C || st.HiddenByteLength != H) throw new ArgumentException("10-byte sections expected");
        byte[] k = st.FlattenedKeysBytes, me = st.FlattenedMaskedEtalonsBytes;
        for (int v = 0; v < 256; v++)
        {
            int[] d = { v / 100, v % 100 / 10, v % 10 };
            for (int s = 0; s < 3; s++)
                for (int j = 0; j < C; j++)
                {
                    _maskT[v * T + s * C + j] = k[d[s] * C + j];
                    _etalT[v * T + s * C + j] = me[d[s] * C + j];
                }
        }
        _jBytes = tb.PosByte.Distinct().OrderBy(x => x).ToArray();
        _part = new ushort[_jBytes.Length * 256];
        for (int jj = 0; jj < _jBytes.Length; jj++)
            for (int b = 0; b < 256; b++)
            {
                int u = 0;
                for (int q = 0; q < tb.M; q++)
                    if (tb.PosByte[q] == _jBytes[jj] && ((b >> tb.PosBit[q]) & 1) == 1) u |= 1 << q;
                _part[jj * 256 + b] = (ushort)u;
            }
        _table = (byte[])tb.Table.Clone();
        _chunk = new byte[Math.Min(ChunkSize, Math.Max(maxInputLength, 1)) * H];
        _parallel = parallel;
    }

    private void ForRanges(int n, Action<int, int> body)
    {
        if (_parallel) Parallel.ForEach(Partitioner.Create(0, n, Range), r => body(r.Item1, r.Item2));
        else for (int a = 0; a < n; a += Range) body(a, Math.Min(n, a + Range));
    }

    // ------------------------------------------------------------------ formation
    public void EncryptToFile(byte[] input, string path)
    {
        using var fs = new FileStream(path, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
        for (int start = 0; start < input.Length; start += ChunkSize)
        {
            int n = Math.Min(ChunkSize, input.Length - start);
            FormChunk(input, start, n, _chunk);
            fs.Write(_chunk, 0, n * H);
        }
    }

    /// <summary>Formation into memory (no file): the protected representation of the whole input in <paramref name="output"/>.</summary>
    public void EncryptToMemory(byte[] input, byte[] output)
    {
        for (int start = 0; start < input.Length; start += ChunkSize)
        {
            int n = Math.Min(ChunkSize, input.Length - start);
            FormChunk(input, start, n, output, start * H);
        }
    }

    private void FormChunk(byte[] input, int start, int n, byte[] dst, int dstOffset = 0)
    {
        ForRanges(n, (a, b) =>
        {
            RandomNumberGenerator.Fill(dst.AsSpan(dstOffset + a * H, (b - a) * H));
            MaskRange(input, start, a, b, dst, dstOffset);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void MaskRange(byte[] input, int start, int a, int b, byte[] dst, int dstOffset)
    {
        ref byte o = ref MemoryMarshal.GetArrayDataReference(dst);
        ref byte mk = ref MemoryMarshal.GetArrayDataReference(_maskT);
        ref byte et = ref MemoryMarshal.GetArrayDataReference(_etalT);
        for (int i = a; i < b; i++)
        {
            nuint ob = (nuint)(dstOffset + i * H);
            nuint tb = (nuint)(input[start + i] * T);
            var r0 = Vector128.LoadUnsafe(ref o, ob);
            Vector128.BitwiseOr(Vector128.AndNot(r0, Vector128.LoadUnsafe(ref mk, tb)), Vector128.LoadUnsafe(ref et, tb)).StoreUnsafe(ref o, ob);
            var r1 = Vector128.LoadUnsafe(ref o, ob + 14);
            Vector128.BitwiseOr(Vector128.AndNot(r1, Vector128.LoadUnsafe(ref mk, tb + 14)), Vector128.LoadUnsafe(ref et, tb + 14)).StoreUnsafe(ref o, ob + 14);
        }
    }

    // ------------------------------------------------------------------ recovery
    public void DecryptFromFile(string path, byte[] result, int length)
    {
        using var fs = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
        for (int start = 0; start < length; start += ChunkSize)
        {
            int n = Math.Min(ChunkSize, length - start);
            int need = n * H, got = 0;
            while (got < need) { int r = fs.Read(_chunk, got, need - got); if (r == 0) throw new EndOfStreamException("unexpected end of the protected file"); got += r; }
            int s0 = start;
            ForRanges(n, (a, b) => DecodeRange(_chunk, 0, result, s0, a, b));
        }
    }

    /// <summary>Recovery from memory (no file).</summary>
    public void DecryptFromMemory(byte[] hidden, byte[] result, int length)
    {
        ForRanges(length, (a, b) => DecodeRange(hidden, 0, result, 0, a, b));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void DecodeRange(byte[] y, int yOffset, byte[] result, int start, int a, int b)
    {
        ref byte yr = ref MemoryMarshal.GetArrayDataReference(y);
        ref ushort pr = ref MemoryMarshal.GetArrayDataReference(_part);
        ref byte tr = ref MemoryMarshal.GetArrayDataReference(_table);
        int[] jb = _jBytes; int nj = jb.Length;
        for (int i = a; i < b; i++)
        {
            int x = 0;
            nint off = yOffset + i * H;
            for (int s = 0; s < 3; s++, off += C)
            {
                int u = 0;
                for (int jj = 0; jj < nj; jj++)
                    u |= Unsafe.Add(ref pr, (jj << 8) + Unsafe.Add(ref yr, off + jb[jj]));
                x = x * 10 + Unsafe.Add(ref tr, u);
            }
            if (x > 255) throw new InvalidDataException("decoded value exceeds 255: corrupted block");
            result[start + i] = (byte)x;
        }
    }
}
