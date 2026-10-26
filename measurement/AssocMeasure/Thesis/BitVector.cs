// BitVector: compatible re-implementation (the original class is not included in the thesis listing).
// Layout follows AssocStego.CreateVector: bit i is stored in byte (len-1-(i>>3)), bit (i & 7).
// Equality compares the byte arrays (the original code compares a 78-bit vector with an 80-bit zero vector).
using System.Text;
public sealed class BitVector : IEquatable<BitVector>
{
    private readonly byte[] _bytes;
    public int BitLength { get; }
    public BitVector(int bitLength) { BitLength = bitLength; _bytes = new byte[(bitLength + 7) >> 3]; }
    public BitVector(byte[] bytes, int bitLength)
    {
        BitLength = bitLength;
        int n = (bitLength + 7) >> 3;
        _bytes = new byte[n];
        Buffer.BlockCopy(bytes, 0, _bytes, 0, Math.Min(n, bytes.Length));
    }
    // string: character j is bit (L-1-j), i.e. the string is written most significant bit first
    public BitVector(string s)
    {
        BitLength = s.Length;
        _bytes = new byte[(BitLength + 7) >> 3];
        for (int j = 0; j < s.Length; j++)
            if (s[j] == '1') SetBit(BitLength - 1 - j, true);
    }
    public bool GetBit(int i) => ((_bytes[_bytes.Length - 1 - (i >> 3)] >> (i & 7)) & 1) == 1;
    public void SetBit(int i, bool v)
    {
        int b = _bytes.Length - 1 - (i >> 3);
        if (v) _bytes[b] |= (byte)(1 << (i & 7)); else _bytes[b] &= (byte)~(1 << (i & 7));
    }
    public byte[] ToByteArray() => (byte[])_bytes.Clone();
    public static BitVector operator ^(BitVector a, BitVector b) => Combine(a, b, (x, y) => (byte)(x ^ y));
    public static BitVector operator &(BitVector a, BitVector b) => Combine(a, b, (x, y) => (byte)(x & y));
    private static BitVector Combine(BitVector a, BitVector b, Func<byte, byte, byte> f)
    {
        int n = Math.Max(a._bytes.Length, b._bytes.Length);
        var r = new BitVector(Math.Max(a.BitLength, b.BitLength));
        for (int i = 0; i < n; i++)
        {
            byte x = i < a._bytes.Length ? a._bytes[i] : (byte)0;
            byte y = i < b._bytes.Length ? b._bytes[i] : (byte)0;
            r._bytes[i] = f(x, y);
        }
        return r;
    }
    public bool Equals(BitVector? o) => o is not null && _bytes.AsSpan().SequenceEqual(o._bytes);
    public override bool Equals(object? obj) => obj is BitVector v && Equals(v);
    public override int GetHashCode() { var h = new HashCode(); foreach (var b in _bytes) h.Add(b); return h.ToHashCode(); }
    public static bool operator ==(BitVector? a, BitVector? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(BitVector? a, BitVector? b) => !(a == b);
    public int PopCount() { int c = 0; foreach (var b in _bytes) c += System.Numerics.BitOperations.PopCount(b); return c; }
    public override string ToString()
    {
        var sb = new StringBuilder(BitLength);
        for (int j = 0; j < BitLength; j++) sb.Append(GetBit(BitLength - 1 - j) ? '1' : '0');
        return sb.ToString();
    }
}
