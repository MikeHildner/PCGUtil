using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Decodes Program metadata from the <c>PRG1</c> chunk's bank leaves.
///
/// Each bank is a 12-byte sub-header (count, record size, identity) followed by fixed
/// records (4960 bytes on this hardware). Within a record: 24-byte name @ 0, favorite
/// @ 2558 bit 5, then category/sub-category @ 2568 (bits 0–4 / 5–7, the same packed
/// idiom combis use at 4790). EXi records additionally carry the instrument engine id
/// @ 2857 (meaningless in HD-1 records — engine-specific region).
/// Category and engine located by correlating the published voice name list against
/// the factory banks (768/768 and 640/640 exact); the favorite bit found by diffing
/// two hardware exports around starring one program — a single-byte 0x00→0x20 flip
/// at 2558 (the combi-idiom guess of 2569 bit 0 was wrong and is corrected here).
/// </summary>
public static class ProgramReader
{
    public const int NameLength = 24;
    private const int SubHeaderSize = 12;
    public const int FavoriteOffset = 2558;
    public const int FavoriteBit = 0x20;
    private const int CategoryOffset = 2568;
    private const int ExiEngineOffset = 2857;

    public static IReadOnlyList<ProgramInfo> Read(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var banks = PcgBankIdentity.CanonicalBanks(pcg, "PRG1");

        var data = pcg.Data;
        var programs = new List<ProgramInfo>();
        for (int bank = 0; bank < banks.Count; bank++)
        {
            if (banks[bank] is not { } chunk)
                continue; // bank not carried by this file
            long baseOffset = chunk.DataOffset;
            if (baseOffset + SubHeaderSize > data.Length)
                continue;

            bool isExi = PcgBankIdentity.TypeFromChunkId(chunk.Id) == ProgramBankType.Exi;
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
            int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));
            if (count <= 0 || recordSize <= 0)
                continue;
            long recordsStart = baseOffset + SubHeaderSize;

            for (int i = 0; i < count; i++)
            {
                long record = recordsStart + (long)i * recordSize;
                if (record + recordSize > data.Length)
                    break;

                programs.Add(new ProgramInfo
                {
                    Bank = bank,
                    Index = i,
                    Name = PcgText.ReadFixedString(data, record, NameLength),
                    Category = recordSize > CategoryOffset ? data[record + CategoryOffset] & 0x1F : 0,
                    SubCategory = recordSize > CategoryOffset ? (data[record + CategoryOffset] >> 5) & 0x07 : 0,
                    Favorite = recordSize > FavoriteOffset && (data[record + FavoriteOffset] & FavoriteBit) != 0,
                    ExiEngine = isExi && recordSize > ExiEngineOffset ? data[record + ExiEngineOffset] : null,
                });
            }
        }
        return programs;
    }
}
