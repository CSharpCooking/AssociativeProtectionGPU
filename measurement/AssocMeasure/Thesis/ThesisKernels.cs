// Kernel bodies verbatim from GpuStegoEncryptor / GpuStegoDecryptor (thesis, Appendix 1).
// Compiled here against Shim/ILGPUShim.cs so that they can be executed sequentially on the CPU.
using ILGPU;
using ILGPU.Runtime;
public static class ThesisKernels
{
public static void EncryptKernel(
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
public static void DecryptKernel(
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
}
