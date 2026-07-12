using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Decodes Combis and their timbres from the <c>CMB1</c> chunk.
///
/// Each combi bank is a 12-byte sub-header (count, record size) followed by fixed records.
/// Within a record (7810 bytes on this hardware) the 24-byte name is at offset 0, and the 16 timbres
/// are a 188-byte-strided array starting at offset 4802 (16 × 188 fills the record exactly).
/// Per timbre: program number @ +0, program-bank PcgId @ +1, status in bits 5–7 of +2.
/// Offsets re-derived from PCG Tools and verified against the sample (~99.7% of enabled
/// timbres resolve to a named program).
/// </summary>
public static class CombiReader
{
    public const int TimbresOffset = 4802;
    public const int TimbreStride = 188;
    public const int TimbresPerCombi = 16;
    public const int NameLength = 24;
    private const int SubHeaderSize = 12;

    public static IReadOnlyList<Combi> Read(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var banks = PcgBankIdentity.CanonicalBanks(pcg, "CMB1");

        var data = pcg.Data;
        var combis = new List<Combi>();
        for (int bank = 0; bank < banks.Count; bank++)
        {
            if (banks[bank] is not { } chunk)
                continue; // bank not carried by this file
            long baseOffset = chunk.DataOffset;
            if (baseOffset + SubHeaderSize > data.Length)
                continue;

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

                combis.Add(new Combi
                {
                    Bank = bank,
                    Index = i,
                    Name = PcgText.ReadFixedString(data, record, NameLength),
                    Timbres = ReadTimbres(data, record),
                });
            }
        }
        return combis;
    }

    private static IReadOnlyList<CombiTimbre> ReadTimbres(byte[] data, long record)
    {
        var timbres = new List<CombiTimbre>(TimbresPerCombi);
        for (int t = 0; t < TimbresPerCombi; t++)
        {
            long tOff = record + TimbresOffset + (long)t * TimbreStride;
            if (tOff + 2 >= data.Length)
                break;

            int statusBits = (data[tOff + 2] >> 5) & 0x07; // bits 5–7
            timbres.Add(new CombiTimbre
            {
                Index = t,
                ProgramNumber = data[tOff],
                ProgramBankPcgId = data[tOff + 1],
                Status = statusBits <= (int)TimbreStatus.Ex2 ? (TimbreStatus)statusBits : TimbreStatus.Off,
            });
        }
        return timbres;
    }
}
