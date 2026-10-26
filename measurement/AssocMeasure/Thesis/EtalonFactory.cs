// Verbatim from the thesis, Appendix 1
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
internal static class EtalonFactory
{
public const int DefaultEtalonCount = 10;
public const int DefaultEtalonBitLength = 78;
private const string DefaultSeedPhrase = "DiplomaMag.DefaultEtalons.v1";
public static string[] CreateDefaultEtalons10() =>
CreateDeterministicEtalons(DefaultEtalonCount, DefaultEtalonBitLength, DefaultSeedPhrase);
public static string[] CreateDeterministicEtalons(int count, int bitLength, string seedPhrase)
{
if (count <= 0)
throw new ArgumentOutOfRangeException(nameof(count));
if (bitLength <= 1)
throw new ArgumentOutOfRangeException(nameof(bitLength));
if (string.IsNullOrWhiteSpace(seedPhrase))
throw new ArgumentException("Не задана seed-фраза для генерации эталонов.", nameof(seedPhrase));
string[] result = new string[count];
var seen = new HashSet<string>(StringComparer.Ordinal);
for (int i = 0; i < count; i++)
{
int nonce = 0;
while (true)
{
string etalon = CreateBitString(bitLength, $"{seedPhrase}:{i}:{nonce}");
if (IsValidEtalon(etalon) && seen.Add(etalon))
{
result[i] = etalon;
break;
}
nonce++;
}
}
return result;
}
private static string CreateBitString(int bitLength, string seed)
{
var builder = new StringBuilder(bitLength);
int round = 0;
while (builder.Length < bitLength)
{
byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{round}"));
foreach (byte value in hash)
{
for (int bit = 0; bit < 8 && builder.Length < bitLength; bit++)
builder.Append(((value >> bit) & 1) == 1 ? '1' : '0');
}
round++;
}
return builder.ToString();
}
private static bool IsValidEtalon(string value)
{
bool hasZero = false;
bool hasOne = false;
foreach (char ch in value)
{
if (ch == '0')
hasZero = true;
else if (ch == '1')
hasOne = true;
else
return false;
if (hasZero && hasOne)
return true;
}
return false;
}
}
