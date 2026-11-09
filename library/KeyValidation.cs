// Key validation by construction of the recognition table (Section 5.2 of the paper). Added to the library after the
// measurements of the paper (revision 2); the measured version of the listing is that of commit 23050a9.
//
// The table-driven check applies only to keys whose masks are built by the dichotomic partitioning of AssocStego.CreateKey
// (binary partitioning of the etalon set, Proposition 1 of the paper). Such keys use at most L - 1 mask positions in total
// for L etalons (one per inner node of the partition tree), and every pattern of the bits at these positions matches exactly
// one masked etalon. A key that fails either condition, or that has mask bits outside the etalon length, is rejected, even if
// a search-based recognizer could still decode the blocks formed with it; a key that violates the necessary and sufficient
// condition of unambiguous recognition always fails (Section 5.2).
using System;
using System.Collections.Generic;

/// <summary>A key that cannot be used: the recognition table cannot be built for it. Derived from InvalidOperationException,
/// which the original check of the library (AssocStego.ValidateKeysOrThrow) throws for keys with equal signatures.</summary>
public sealed class KeyValidationException : InvalidOperationException
{
    public KeyValidationException(string message) : base(message) { }
    public KeyValidationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Recognition table L of Section 5.2 (named KeyRecognitionTable to avoid a clash with RecognitionTable of the measurement code): the digit of a section is Table[u], where bit q of u is the bit of the section
/// at position q of P (byte PositionByte[q], bit PositionBit[q] of the section).</summary>
public sealed class KeyRecognitionTable
{
    public int M { get; }
    public int[] PositionByte { get; }
    public int[] PositionBit { get; }
    public byte[] Table { get; }

    private KeyRecognitionTable(int m, int[] positionByte, int[] positionBit, byte[] table)
    {
        M = m; PositionByte = positionByte; PositionBit = positionBit; Table = table;
    }

    /// <summary>Builds the table for one key or throws <see cref="KeyValidationException"/>.</summary>
    /// <param name="flatMasks">masks of the etalons, <paramref name="containerByteLength"/> bytes each, in the byte layout of BitVector</param>
    /// <param name="flatMaskedEtalons">etalons AND masks, in the same layout</param>
    /// <param name="etalonBitLength">number of significant bits of an etalon (78 for the default etalons); the others are padding</param>
    public static KeyRecognitionTable BuildOrThrow(byte[] flatMasks, byte[] flatMaskedEtalons, int containerByteLength,
                                                int etalonCount, int etalonBitLength)
    {
        ArgumentNullException.ThrowIfNull(flatMasks);
        ArgumentNullException.ThrowIfNull(flatMaskedEtalons);
        int c = containerByteLength;
        if (etalonCount < 2 || flatMasks.Length != etalonCount * c || flatMaskedEtalons.Length != etalonCount * c)
            throw new KeyValidationException("Размеры ключа не соответствуют числу эталонов и длине секции.");
        // 1. mask bits must lie within the etalon length (bit i is stored in byte c-1-(i>>3), bit i&7)
        for (int t = 0; t < etalonCount; t++)
            for (int i = etalonBitLength; i < 8 * c; i++)
                if (((flatMasks[t * c + c - 1 - (i >> 3)] >> (i & 7)) & 1) == 1)
                    throw new KeyValidationException($"Маска эталона {t} содержит бит {i} за пределами длины эталона ({etalonBitLength}).");
        // 2. P: union of the mask positions; at most L-1 positions for a key built by dichotomic partitioning
        var pos = new List<(int b, int bit)>();
        for (int b = 0; b < c; b++)
            for (int bit = 0; bit < 8; bit++)
                for (int t = 0; t < etalonCount; t++)
                    if (((flatMasks[t * c + b] >> bit) & 1) == 1) { pos.Add((b, bit)); break; }
        int m = pos.Count;
        if (m > etalonCount - 1)
            throw new KeyValidationException($"Маски ключа занимают {m} позиций, больше {etalonCount - 1}: ключ построен не дихотомическим разбиением, табличная проверка к нему неприменима.");
        // 3. every pattern u of the bits at P must match exactly one masked etalon (Proposition 1)
        var table = new byte[1 << m];
        var s = new byte[c];
        for (int u = 0; u < (1 << m); u++)
        {
            Array.Clear(s);
            for (int q = 0; q < m; q++)
                if (((u >> q) & 1) == 1) s[pos[q].b] |= (byte)(1 << pos[q].bit);
            int found = -1, hits = 0;
            for (int t = 0; t < etalonCount; t++)
            {
                bool ok = true;
                for (int b = 0; b < c; b++)
                    if ((s[b] & flatMasks[t * c + b]) != flatMaskedEtalons[t * c + b]) { ok = false; break; }
                if (ok) { found = t; hits++; }
            }
            if (hits != 1)
                throw new KeyValidationException(hits == 0
                    ? $"Набор битов {u} на позициях масок не соответствует ни одному эталону: ключ не удовлетворяет предложению 1."
                    : $"Набор битов {u} на позициях масок соответствует {hits} эталонам: распознавание с этим ключом неоднозначно.");
            table[u] = (byte)found;
        }
        var pb = new int[m]; var pbit = new int[m];
        for (int q = 0; q < m; q++) { pb[q] = pos[q].b; pbit[q] = pos[q].bit; }
        return new KeyRecognitionTable(m, pb, pbit, table);
    }

    /// <summary>Digit encoded in the section that starts at <paramref name="offset"/> (Algorithm 1, one section).</summary>
    public int Digit(ReadOnlySpan<byte> data, int offset)
    {
        int u = 0;
        for (int q = 0; q < M; q++) u |= ((data[offset + PositionByte[q]] >> PositionBit[q]) & 1) << q;
        return Table[u];
    }
}
