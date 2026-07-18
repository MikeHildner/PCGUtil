using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Reads user KARMA GE names from a companion <c>.KGE</c> file.
///
/// A .KGE uses the same chunk idiom as a .PCG but with a 32-byte file header:
/// <c>KGE1</c> contains <c>GENM</c> (names) and <c>GEDT</c> (data); GENM holds one
/// <c>GENB</c> leaf per saved GE bank. A GENB's data is count (BE32), record size
/// (BE32, = 32), a zero word, label length (BE32, = 24), the 24-byte bank label
/// ("Bank U-A" … "Bank U-L"), then count × 32-byte GE names. User GEs address as
/// flat ids 2048 + bank×128 + number (see <see cref="Combi.KarmaGeLabel"/>).
/// Verified against a vendor pack's .KGE (its combis' GE selects resolve to the
/// names listed in the pack's own content listing).
/// </summary>
public static class KgeReader
{
    private const int FileHeaderSize = 32;
    private const int ChunkHeaderSize = 12;
    private const int GeNameLength = 32;
    private const int NamesOffset = 40; // count(4) recSize(4) zero(4) labelLen(4) label(24)
    public const int UserBankCount = 12; // USER-A..L

    /// <summary>User GE names by bank (USER-A..L = 0..11); a bank the file doesn't carry
    /// is an empty list. Returns null when the bytes aren't a KGE file.</summary>
    public static IReadOnlyList<IReadOnlyList<string>>? Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < FileHeaderSize + ChunkHeaderSize)
            return null;
        if (data[0] != 'K' || data[1] != 'O' || data[2] != 'R' || data[3] != 'G')
            return null;

        var banks = new IReadOnlyList<string>[UserBankCount];
        for (int i = 0; i < banks.Length; i++)
            banks[i] = Array.Empty<string>();

        long p = FileHeaderSize;
        if (ReadId(data, p) != "KGE1")
            return null;
        long kgeEnd = p + ChunkHeaderSize + BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)p + 4, 4));

        long q = p + ChunkHeaderSize;
        while (q + ChunkHeaderSize <= kgeEnd && q + ChunkHeaderSize <= data.Length)
        {
            var id = ReadId(data, q);
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)q + 4, 4));
            if (id == "GENM")
                ReadBankNames(data, q + ChunkHeaderSize, q + ChunkHeaderSize + size, banks);
            q += ChunkHeaderSize + size;
        }
        return banks;
    }

    private static void ReadBankNames(byte[] data, long start, long end, IReadOnlyList<string>[] banks)
    {
        long r = start;
        while (r + ChunkHeaderSize <= end && r + ChunkHeaderSize <= data.Length)
        {
            var id = ReadId(data, r);
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)r + 4, 4));
            if (id == "GENB" && size >= NamesOffset)
            {
                long d = r + ChunkHeaderSize;
                int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)d, 4));
                string label = PcgText.ReadFixedString(data, d + 16, 24);
                int bank = BankIndexFromLabel(label);
                if (bank >= 0 && count > 0)
                {
                    var names = new string[count];
                    for (int i = 0; i < count; i++)
                        names[i] = PcgText.ReadFixedString(data, d + NamesOffset + (long)i * GeNameLength, GeNameLength);
                    banks[bank] = names;
                }
            }
            r += ChunkHeaderSize + size;
        }
    }

    // "Bank U-A" → 0 … "Bank U-L" → 11.
    private static int BankIndexFromLabel(string label)
    {
        var trimmed = label.Trim();
        if (trimmed.Length < 1)
            return -1;
        char letter = char.ToUpperInvariant(trimmed[^1]);
        return letter is >= 'A' and <= 'L' ? letter - 'A' : -1;
    }

    /// <summary>Name for a flat user GE id (2048 + bank×128 + number), or null when the
    /// id is a preset or the bank/number isn't carried by this KGE.</summary>
    public static string? UserGeName(IReadOnlyList<IReadOnlyList<string>> banks, int geId)
    {
        if (geId < Combi.KarmaUserGeBase)
            return null;
        int bank = (geId - Combi.KarmaUserGeBase) / 128;
        int number = (geId - Combi.KarmaUserGeBase) % 128;
        if (bank < 0 || bank >= banks.Count || number >= banks[bank].Count)
            return null;
        var name = banks[bank][number];
        return name.Length == 0 ? null : name;
    }

    private static string ReadId(byte[] data, long offset)
    {
        if (offset + 4 > data.Length)
            return string.Empty;
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            byte c = data[offset + i];
            if (c is < 0x30 or > 0x5A)
                return string.Empty;
            chars[i] = (char)c;
        }
        return new string(chars);
    }
}
