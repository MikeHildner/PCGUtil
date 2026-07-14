using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Decodes Combis and their timbres from the <c>CMB1</c> chunk.
///
/// Each combi bank is a 12-byte sub-header (count, record size) followed by fixed records.
/// Within a record (7810 bytes on this hardware): 24-byte name @ 0, tempo ×100 (LE16) @ 1304,
/// category/sub-category @ 4790 (bits 0–4 / 5–7), favorite @ 4791 bit 0, and the 16 timbres as
/// a 188-byte-strided array starting at 4802 (16 × 188 fills the record exactly).
/// Per timbre: program number @ +0, bank PcgId @ +1, MIDI channel / status @ +2 (bits 0–4 /
/// 5–7), volume @ +5, transpose @ +7 (signed), detune @ +8 (LE16, cents), mute @ +34 bit 7,
/// key zone top/bottom @ +37/+38, velocity zone top/bottom @ +40/+41.
/// Offsets re-derived from PCG Tools and verified against real files — program resolution at
/// ~99.7%, and key/velocity zones match a vendor pack's prose zone descriptions exactly
/// (e.g. "F#3 to B3 velocity has tight bell" decodes to keys 54–59, velocity 89–127).
/// </summary>
public static class CombiReader
{
    public const int TimbresOffset = 4802;
    public const int TimbreStride = 188;
    public const int TimbresPerCombi = 16;
    public const int NameLength = 24;
    private const int SubHeaderSize = 12;
    private const int TempoOffset = 1304;
    private const int CategoryOffset = 4790;

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
                    Category = recordSize > CategoryOffset ? data[record + CategoryOffset] & 0x1F : 0,
                    SubCategory = recordSize > CategoryOffset ? (data[record + CategoryOffset] >> 5) & 0x07 : 0,
                    Favorite = recordSize > CategoryOffset + 1 && (data[record + CategoryOffset + 1] & 0x01) != 0,
                    Tempo = recordSize >= TempoOffset + 2
                        ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)record + TempoOffset, 2)) / 100m
                        : 0m,
                    Timbres = ReadTimbres(data, record, recordSize),
                });
            }
        }
        return combis;
    }

    private static IReadOnlyList<CombiTimbre> ReadTimbres(byte[] data, long record, int recordSize)
    {
        var timbres = new List<CombiTimbre>(TimbresPerCombi);
        for (int t = 0; t < TimbresPerCombi; t++)
        {
            long tOff = record + TimbresOffset + (long)t * TimbreStride;
            if (TimbresOffset + (t + 1) * TimbreStride > recordSize || tOff + TimbreStride > data.Length)
                break;

            int statusBits = (data[tOff + 2] >> 5) & 0x07; // bits 5–7
            timbres.Add(new CombiTimbre
            {
                Index = t,
                ProgramNumber = data[tOff],
                ProgramBankPcgId = data[tOff + 1],
                Status = statusBits <= (int)TimbreStatus.Ex2 ? (TimbreStatus)statusBits : TimbreStatus.Off,
                MidiChannel = data[tOff + 2] & 0x1F,
                Volume = data[tOff + 5],
                Transpose = (sbyte)data[tOff + 7],
                Detune = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan((int)tOff + 8, 2)),
                Mute = (data[tOff + 34] & 0x80) != 0,
                TopKey = data[tOff + 37],
                BottomKey = data[tOff + 38],
                TopVelocity = data[tOff + 40],
                BottomVelocity = data[tOff + 41],
            });
        }
        return timbres;
    }
}
