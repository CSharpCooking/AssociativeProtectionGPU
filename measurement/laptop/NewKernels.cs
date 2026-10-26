// New recognition kernels (table-driven and branch-free) and verbatim copies of the original kernels
// (EncryptKernelCopy, DecryptSearchKernelCopy) that can be loaded with an explicit group size. Written against the ILGPU API surface
// (Index1D, ArrayView<T>) so that the same source can be dropped into the GPU project; here it
// is compiled against ILGPU directly.
using ILGPU;
using ILGPU.Runtime;

public static class NewKernels
{
    // ---- verbatim copies of the thesis kernels (renamed) ----
public static void EncryptKernelCopy(
Index1D index,
ArrayView<byte> input,
ArrayView<byte> output,
ArrayView<byte> maskedEtalons,
ArrayView<byte> keys,
int containerByteLen,
int globalInputOffset,
int blockSeed)
{
byte value = input[index];
int d2 = value / 100;
int rem = value % 100;
int d1 = rem / 10;
int d0 = rem % 10;
int globalIndex = globalInputOffset + index;
int containerBase = index * 3;
ProcessDigit(output, maskedEtalons, keys, containerByteLen, containerBase, d2, 0, globalIndex, blockSeed);
ProcessDigit(output, maskedEtalons, keys, containerByteLen, containerBase, d1, 1, globalIndex, blockSeed);
ProcessDigit(output, maskedEtalons, keys, containerByteLen, containerBase, d0, 2, globalIndex, blockSeed);
}
public static void ProcessDigit(
ArrayView<byte> output,
ArrayView<byte> maskedEtalons,
ArrayView<byte> keys,
int containerByteLen,
int containerBase,
int digit,
int digitIndex,
int globalIndex,
int blockSeed)
{
int containerIdx = containerBase + digitIndex;
int outputOffset = containerIdx * containerByteLen;
int etalonOffset = digit * containerByteLen;
for (int i = 0; i < containerByteLen; i++)
{
byte keyByte = keys[etalonOffset + i];
byte maskedEtalonByte = maskedEtalons[etalonOffset + i];
byte randomByte = PseudoRandomByte(globalIndex, digitIndex, i, blockSeed);
output[outputOffset + i] = (byte)(maskedEtalonByte | (randomByte & (byte)~keyByte));
}
}
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
public static void DecryptSearchKernelCopy(
Index1D index,
ArrayView<byte> input,
ArrayView<byte> output,
ArrayView<byte> maskedEtalons,
ArrayView<byte> keys,
int containerByteLen,
int containersPerByte,
int etalonCount)
{
int inputBase = index * containersPerByte * containerByteLen;
int d2 = DecodeContainer(input, maskedEtalons, keys, containerByteLen, inputBase, 0, etalonCount);
int d1 = DecodeContainer(input, maskedEtalons, keys, containerByteLen, inputBase, 1, etalonCount);
int d0 = DecodeContainer(input, maskedEtalons, keys, containerByteLen, inputBase, 2, etalonCount);
output[index] = (byte)(d2 * 100 + d1 * 10 + d0);
}
public static int DecodeContainer(
ArrayView<byte> input,
ArrayView<byte> maskedEtalons,
ArrayView<byte> keys,
int containerByteLen,
int inputBase,
int containerIndex,
int etalonCount)
{
int containerOffset = inputBase + containerIndex * containerByteLen;
for (int etalonIdx = 0; etalonIdx < etalonCount; etalonIdx++)
{
bool matches = true;
int etalonOffset = etalonIdx * containerByteLen;
for (int i = 0; i < containerByteLen; i++)
{
byte containerByte = input[containerOffset + i];
byte keyByte = keys[etalonOffset + i];
if ((containerByte & keyByte) != maskedEtalons[etalonOffset + i])
{
matches = false;
break;
}
}
if (matches)
return etalonIdx;
}
return 0;
}

    // ---- new kernels ----
    // Table-driven recognition: every thread gathers m bits of each section and reads the digit from T.
    // All loop trip counts are uniform across threads (m is fixed per key), so there is no data-dependent control flow.
    public static void DecryptTableKernel(
        Index1D index,
        ArrayView<byte> input,
        ArrayView<byte> output,
        ArrayView<int> posByte,
        ArrayView<int> posBit,
        ArrayView<byte> table,
        int m,
        int containerByteLen)
    {
        int inputBase = index * 3 * containerByteLen;
        int x = 0;
        for (int c = 0; c < 3; c++)
        {
            int sectionBase = inputBase + c * containerByteLen;
            int u = 0;
            for (int q = 0; q < m; q++)
                u |= ((input[sectionBase + posByte[q]] >> posBit[q]) & 1) << q;
            x = x * 10 + table[u];
        }
        output[index] = (byte)x;
    }

    // Branch-free variant of the search: always compares a section with all etalons (no early exit).
    public static void DecryptBranchFreeKernel(
        Index1D index,
        ArrayView<byte> input,
        ArrayView<byte> output,
        ArrayView<byte> maskedEtalons,
        ArrayView<byte> keys,
        int containerByteLen,
        int containersPerByte,
        int etalonCount)
    {
        int inputBase = index * containersPerByte * containerByteLen;
        int d2 = DecodeContainerBranchFree(input, maskedEtalons, keys, containerByteLen, inputBase, 0, etalonCount);
        int d1 = DecodeContainerBranchFree(input, maskedEtalons, keys, containerByteLen, inputBase, 1, etalonCount);
        int d0 = DecodeContainerBranchFree(input, maskedEtalons, keys, containerByteLen, inputBase, 2, etalonCount);
        output[index] = (byte)(d2 * 100 + d1 * 10 + d0);
    }

    public static int DecodeContainerBranchFree(
        ArrayView<byte> input,
        ArrayView<byte> maskedEtalons,
        ArrayView<byte> keys,
        int containerByteLen,
        int inputBase,
        int containerIndex,
        int etalonCount)
    {
        int containerOffset = inputBase + containerIndex * containerByteLen;
        int digit = 0;
        for (int etalonIdx = 0; etalonIdx < etalonCount; etalonIdx++)
        {
            int diff = 0;
            int etalonOffset = etalonIdx * containerByteLen;
            for (int i = 0; i < containerByteLen; i++)
                diff |= (input[containerOffset + i] & keys[etalonOffset + i]) ^ maskedEtalons[etalonOffset + i];
            digit += (diff == 0 ? 1 : 0) * etalonIdx;
        }
        return digit;
    }
}

// Host-side construction of the recognition table (once per key).
public sealed class RecognitionTable
{
    public int M { get; private init; }
    public int[] PosByte { get; private init; } = Array.Empty<int>();
    public int[] PosBit { get; private init; } = Array.Empty<int>();
    public byte[] Table { get; private init; } = Array.Empty<byte>();

    public static RecognitionTable Build(byte[] flatKeys, byte[] flatMaskedEtalons, int containerByteLen, int etalonCount)
    {
        var pos = new List<(int b, int bit)>();
        for (int b = 0; b < containerByteLen; b++)
            for (int bit = 0; bit < 8; bit++)
                for (int t = 0; t < etalonCount; t++)
                    if (((flatKeys[t * containerByteLen + b] >> bit) & 1) == 1) { pos.Add((b, bit)); break; }
        int m = pos.Count;
        if (m > 16) throw new InvalidOperationException($"|P| = {m}: the table would be too large; the key is not dichotomic.");
        var table = new byte[1 << m];
        var s = new byte[containerByteLen];
        for (int u = 0; u < (1 << m); u++)
        {
            Array.Clear(s);
            for (int q = 0; q < m; q++)
                if (((u >> q) & 1) == 1) s[pos[q].b] |= (byte)(1 << pos[q].bit);
            int found = -1, hits = 0;
            for (int t = 0; t < etalonCount; t++)
            {
                bool ok = true;
                for (int b = 0; b < containerByteLen; b++)
                    if ((s[b] & flatKeys[t * containerByteLen + b]) != flatMaskedEtalons[t * containerByteLen + b]) { ok = false; break; }
                if (ok) { found = t; hits++; }
            }
            if (hits != 1) throw new InvalidOperationException($"u = {u}: {hits} matching etalons; Proposition 1 does not hold for this key.");
            table[u] = (byte)found;
        }
        return new RecognitionTable { M = m, PosByte = pos.Select(p => p.b).ToArray(), PosBit = pos.Select(p => p.bit).ToArray(), Table = table };
    }
}
