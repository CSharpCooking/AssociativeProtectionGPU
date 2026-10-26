// Verbatim from the thesis, Appendix 1
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
public class AssocStego
{
public readonly BitVector[] etalons;
public readonly int etalonLength;
private readonly BitVector zeroVector;
private readonly List<int>[] maskUnitPositions;
private readonly int containerByteLength;
private readonly byte[] flattenedEtalons;
private byte[] flattenedKeys = Array.Empty<byte>();
private byte[] flattenedMaskedEtalons = Array.Empty<byte>();
private BitVector[] key;
public IReadOnlyList<int>[] MaskUnitPositions => maskUnitPositions;
public int ContainerByteLength => containerByteLength;
public int HiddenByteLength => containerByteLength * 3;
public byte[] FlattenedEtalonsBytes => flattenedEtalons;
public byte[] FlattenedKeysBytes => key == null
? throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.")
: flattenedKeys;
public byte[] FlattenedMaskedEtalonsBytes => key == null
? throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.")
: flattenedMaskedEtalons;
public BitVector[] Key
{
get => key ?? throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.");
set
{
if (value is null)
throw new ArgumentNullException(nameof(value));
if (value.Length != etalons.Length)
throw new ArgumentException("Длина ключа должна совпадать с количеством эталонов.", nameof(value));
key = value;
RefreshKeyCache();
}
}
public AssocStego(params string[] e)
{
if (e is null || e.Length == 0)
throw new ArgumentException("Не передано ни одного эталона.", nameof(e));
etalonLength = e[0].Length;
if (e.Any(s => s is null || s.Length != etalonLength))
throw new ArgumentException("Все эталоны должны иметь одинаковую длину.");
etalons = new BitVector[e.Length];
maskUnitPositions = new List<int>[e.Length];
for (int i = 0; i < e.Length; i++)
maskUnitPositions[i] = new List<int>();
containerByteLength = (etalonLength + 7) >> 3;
zeroVector = new BitVector((etalonLength + 7) & ~7);
flattenedEtalons = new byte[e.Length * containerByteLength];
for (int i = 0; i < e.Length; i++)
{
etalons[i] = new BitVector(e[i]);
var bytes = etalons[i].ToByteArray();
Buffer.BlockCopy(bytes, 0, flattenedEtalons, i * containerByteLength, containerByteLength);
}
}
public BitVector[] CreateKey()
{
var eIndexes = Enumerable.Range(0, etalons.Length).ToList();
CreateKey(eIndexes);
var masks = new BitVector[etalons.Length];
for (int i = 0; i < etalons.Length; i++)
masks[i] = CreateVector(etalonLength, maskUnitPositions[i].ToArray());
Key = masks;
return masks;
}
public void CreateKey(List<int> eIndexes, bool firstStepPermutation = true, bool resetMaskUnitPositions = true)
{
if (resetMaskUnitPositions)
ResetMaskUnitPositions();
if (eIndexes is null)
throw new ArgumentNullException(nameof(eIndexes));
if (eIndexes.Count < 2)
return;
BitVector a_0 = new(etalonLength);
BitVector a_1 = new(etalonLength);
var c = firstStepPermutation ? CreateCryptoPermutation(eIndexes) : eIndexes;
var d_1 = new List<int> { c[0] };
a_0 = etalons[c[0]] ^ etalons[c[1]];
for (int i = 0; i < c.Count - 2; i++)
{
a_1 = etalons[c[0]] ^ etalons[c[i + 2]];
var a_3 = a_0 & a_1;
if (a_3 == zeroVector)
d_1.Add(c[i + 2]);
else
a_0 = a_3;
}
int randomOneBitIndex = GetRandomOneBitIndex(a_0);
foreach (var idx in c)
maskUnitPositions[idx].Add(randomOneBitIndex);
var d_2 = c.Except(d_1).ToList();
Parallel.Invoke(
() => CreateKey(d_1, true, false),
() => CreateKey(d_2, true, false)
);
}
public void SaveKeyToFile(string filePath, BitVector[] keyToSave)
{
if (keyToSave == null)
throw new ArgumentNullException(nameof(keyToSave));
if (keyToSave.Length != etalons.Length)
throw new ArgumentException("Длина массива ключей не совпадает с числом эталонов.");
using var fs = File.Create(filePath);
using var bw = new BinaryWriter(fs);
const int MAGIC = 0x41534B59;
bw.Write(MAGIC);
bw.Write(keyToSave.Length);
bw.Write(etalonLength);
for (int i = 0; i < keyToSave.Length; i++)
{
byte[] bytes = keyToSave[i].ToByteArray();
bw.Write(bytes.Length);
bw.Write(bytes);
}
}
public BitVector[] LoadKeyFromFile(string filePath)
{
using var fs = File.OpenRead(filePath);
using var br = new BinaryReader(fs);
const int MAGIC = 0x41534B59;
int magic = br.ReadInt32();
if (magic != MAGIC)
throw new InvalidDataException("Неверный формат файла ключа (magic).");
int keyCount = br.ReadInt32();
int bitLength = br.ReadInt32();
if (bitLength != etalonLength)
throw new InvalidDataException($"Длина битового вектора в файле ({bitLength}) не совпадает с etalonLength ({etalonLength}).");
if (keyCount != etalons.Length)
throw new InvalidDataException($"Количество ключей в файле ({keyCount}) не совпадает с количеством эталонов ({etalons.Length}).");
var loadedKey = new BitVector[keyCount];
for (int i = 0; i < keyCount; i++)
{
int len = br.ReadInt32();
byte[] bytes = br.ReadBytes(len);
if (bytes.Length != len)
throw new EndOfStreamException("Файл ключа повреждён: не хватает данных.");
loadedKey[i] = new BitVector(bytes, bitLength);
}
key = loadedKey;
RefreshKeyCache();
return loadedKey;
}
private void RefreshKeyCache()
{
if (key == null)
throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.");
flattenedKeys = new byte[etalons.Length * containerByteLength];
flattenedMaskedEtalons = new byte[etalons.Length * containerByteLength];
for (int i = 0; i < etalons.Length; i++)
{
byte[] keyBytes = key[i].ToByteArray();
int offset = i * containerByteLength;
Buffer.BlockCopy(keyBytes, 0, flattenedKeys, offset, containerByteLength);
for (int j = 0; j < containerByteLength; j++)
flattenedMaskedEtalons[offset + j] = (byte)(flattenedEtalons[offset + j] & keyBytes[j]);
}
}
public void HideByteToBuffer(byte value, Span<byte> destination)
{
if (destination.Length < HiddenByteLength)
throw new ArgumentException("Буфер назначения слишком мал.", nameof(destination));
if (key == null)
throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.");
int v = value;
int d2 = v / 100;
int rem = v % 100;
int d1 = rem / 10;
int d0 = rem % 10;
HideDigitToBuffer(d2, destination.Slice(0, containerByteLength));
HideDigitToBuffer(d1, destination.Slice(containerByteLength, containerByteLength));
HideDigitToBuffer(d0, destination.Slice(containerByteLength * 2, containerByteLength));
}
public byte DiscloseByteFromBuffer(ReadOnlySpan<byte> hidden)
{
if (hidden.Length < HiddenByteLength)
throw new ArgumentException("Неверная длина скрытого блока для 10 эталонов.", nameof(hidden));
if (key == null)
throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.");
int d2 = DecodeDigit(hidden.Slice(0, containerByteLength));
int d1 = DecodeDigit(hidden.Slice(containerByteLength, containerByteLength));
int d0 = DecodeDigit(hidden.Slice(containerByteLength * 2, containerByteLength));
if (d2 < 0 || d1 < 0 || d0 < 0)
throw new InvalidOperationException("Не удалось распознать десятичную цифру.");
int value = d2 * 100 + d1 * 10 + d0;
if (value > 255)
throw new InvalidOperationException("Ошибка: декодировано число > 255.");
return (byte)value;
}
private void HideDigitToBuffer(int digit, Span<byte> destination)
{
RandomNumberGenerator.Fill(destination);
int offset = digit * containerByteLength;
for (int i = 0; i < containerByteLength; i++)
{
byte randomByte = destination[i];
destination[i] = (byte)(randomByte ^ ((randomByte ^ flattenedEtalons[offset + i]) & flattenedKeys[offset + i]));
}
}
private int DecodeDigit(ReadOnlySpan<byte> source)
{
for (int etalonIdx = 0; etalonIdx < etalons.Length; etalonIdx++)
{
bool matches = true;
int offset = etalonIdx * containerByteLength;
for (int i = 0; i < containerByteLength; i++)
{
if ((source[i] & flattenedKeys[offset + i]) != flattenedMaskedEtalons[offset + i])
{
matches = false;
break;
}
}
if (matches)
return etalonIdx;
}
return -1;
}
public void ValidateKeysOrThrow(BitVector[] key)
{
var map = new Dictionary<string, int>(StringComparer.Ordinal);
for (int i = 0; i < etalons.Length; i++)
{
var sig = etalons[i] & key[i];
string sigStr = sig.ToString();
if (map.TryGetValue(sigStr, out int j))
throw new InvalidOperationException($"Неоднозначные ключи: эталоны {j} и {i} имеют одинаковую подпись по своим маскам.");
map[sigStr] = i;
}
}
public int DiscloseEtalon(BitVector stegoContainer)
{
for (int i = 0; i < etalons.Length; i++)
{
if ((stegoContainer & key[i]) == (etalons[i] & key[i]))
return i;
}
return -1;
}
public BitVector HideEtalon(int eIndex)
{
BitVector container = GenerateContainer(etalonLength);
return container ^ ((container ^ etalons[eIndex]) & key[eIndex]);
}
public BitVector GenerateContainer(int bitLength)
{
int byteCount = (bitLength + 7) >> 3;
byte[] randomBytes = new byte[byteCount];
RandomNumberGenerator.Fill(randomBytes);
return new BitVector(randomBytes, bitLength);
}
int GetRandomOneBitIndex(BitVector v)
{
int count = 0;
int selectedIndex = -1;
for (int i = 0; i < v.BitLength; i++)
{
if (!v.GetBit(i))
continue;
count++;
if (RandomNumberGenerator.GetInt32(count) == 0)
selectedIndex = i;
}
if (selectedIndex < 0)
throw new InvalidOperationException("Во входном векторе отсутствуют единичные биты.");
return selectedIndex;
}
List<int> CreateCryptoPermutation(List<int> list)
{
for (int i = list.Count - 1; i > 0; i--)
{
int j = RandomNumberGenerator.GetInt32(i + 1);
(list[i], list[j]) = (list[j], list[i]);
}
return list;
}
void ResetMaskUnitPositions()
{
foreach (var list in maskUnitPositions)
list.Clear();
}
BitVector CreateVector(int bitLength, params int[] nonZeroIndexes)
{
int bufferLength = (bitLength + 7) >> 3;
byte[] bitBuffer = new byte[bufferLength];
foreach (var bitIndex in nonZeroIndexes)
{
if (bitIndex >= bitLength)
throw new ArgumentOutOfRangeException(nameof(nonZeroIndexes), "Индекс бита выходит за пределы диапазона.");
int byteIndex = bufferLength - 1 - (bitIndex >> 3);
int bitOffset = bitIndex & 7;
byte mask = (byte)(1 << bitOffset);
bitBuffer[byteIndex] = (byte)(bitBuffer[byteIndex] & ~mask);
bitBuffer[byteIndex] |= mask;
}
return new BitVector(bitBuffer, bitLength);
}
}
