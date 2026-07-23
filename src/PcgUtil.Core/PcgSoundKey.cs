using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PcgUtil.Core;

/// <summary>One record's sound identity: its slot, display name, and masked content key.</summary>
public sealed record SoundKeyEntry(int Bank, int Index, string Name, string Key);

/// <summary>
/// Computes the app's canonical <em>sound key</em> for program (PRG1) and combi (CMB1)
/// records: SHA-256 over the record with the 24-byte name field zeroed (absorbing NUL- vs
/// space-padding) and the favorite bit cleared, prefixed with the bank chunk id so
/// byte-identical HD-1 (PBK1) and EXi (MBK1) records — different sounds on different
/// engines — can never collide. Two records share a key exactly when they hold the same
/// sound, regardless of name or star. Used by the Duplicates tab (same-file grouping) and
/// the Merge view ("the target already has this sound").
/// </summary>
public static class PcgSoundKey
{
    private const int SubHeaderSize = 12;
    private const int NameLength = 24;

    /// <summary>Every record's sound key for one section ("PRG1" or "CMB1"), bank order.</summary>
    public static IReadOnlyList<SoundKeyEntry> Keys(PcgFile pcg, string sectionId)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (favOffset, favBit) = FavoriteMask(sectionId);
        var banks = PcgBankIdentity.CanonicalBanks(pcg, sectionId);
        var data = pcg.Data;
        var entries = new List<SoundKeyEntry>();

        for (int b = 0; b < banks.Count; b++)
        {
            if (banks[b] is not { } chunk)
                continue; // bank not carried by this file
            long baseOffset = chunk.DataOffset;
            if (baseOffset + SubHeaderSize > data.Length)
                continue;
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
            int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));
            if (count <= 0 || recordSize <= 0)
                continue;
            long recordsStart = baseOffset + SubHeaderSize;
            byte[] masked = new byte[recordSize];

            for (int i = 0; i < count; i++)
            {
                long record = recordsStart + (long)i * recordSize;
                if (record + recordSize > data.Length)
                    break;
                entries.Add(new SoundKeyEntry(b, i,
                    PcgText.ReadFixedString(data, record, NameLength),
                    KeyOf(data, record, recordSize, chunk.Id, favOffset, favBit, masked)));
            }
        }
        return entries;
    }

    /// <summary>The section's favorite-bit location — the only per-record metadata masked
    /// besides the name (hardware-verified offsets). Unknown sections mask nothing.</summary>
    public static (int Offset, int Bit) FavoriteMask(string sectionId) => sectionId switch
    {
        "PRG1" => (ProgramReader.FavoriteOffset, ProgramReader.FavoriteBit),
        "CMB1" => (CombiReader.FavoriteOffset, CombiReader.FavoriteBit),
        _ => (-1, 0),
    };

    internal static string KeyOf(byte[] data, long record, int recordSize, string chunkId,
                                 int favOffset, int favBit, byte[] masked)
    {
        data.AsSpan((int)record, recordSize).CopyTo(masked);
        masked.AsSpan(0, Math.Min(NameLength, recordSize)).Clear();
        if (favOffset >= 0 && favOffset < recordSize)
            masked[favOffset] &= (byte)~favBit;
        return chunkId + ":" + Convert.ToHexString(SHA256.HashData(masked));
    }
}
