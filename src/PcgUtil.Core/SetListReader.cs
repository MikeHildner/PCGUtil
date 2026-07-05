using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Decodes Set Lists from the <c>SBK1</c> chunk.
///
/// Layout (reverse-engineered, confirmed against hardware): a 12-byte sub-header
/// (count, record size) followed by <c>count</c> records. Each record is one Set
/// List: a 24-byte set-list name, then 128 slots of 542 bytes (a small trailing
/// region is unused). Within a slot the 24-byte name is at offset 0, immediately
/// followed by the 6-byte reference at offset 24 (see <see cref="SetListReference"/>).
/// </summary>
public static class SetListReader
{
    public const int SubHeaderSize = 12;
    public const int RecordHeaderSize = 24;  // the set-list name precedes the slots
    public const int SlotSize = 542;
    public const int SetListNameLength = 24;
    public const int SlotNameOffset = 0;     // name is at the start of each slot
    public const int SlotNameLength = 24;
    public const int SlotRefOffset = 24;     // reference follows the name
    public const int SlotRefLength = 6;
    public const int SlotDescriptionOffset = 30;  // comment field fills the rest of the slot
    public const int SlotDescriptionLength = 512; // 24 + 6 + 512 = 542 = SlotSize

    public static IReadOnlyList<SetList> Read(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var sbk = pcg.FindFirst("SBK1");
        return sbk is null ? Array.Empty<SetList>() : Read(pcg.Data, sbk);
    }

    public static IReadOnlyList<SetList> Read(byte[] data, PcgChunk sbk)
    {
        long baseOffset = sbk.DataOffset;
        if (baseOffset + SubHeaderSize > data.Length)
            return Array.Empty<SetList>();

        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
        int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));
        long recordsStart = baseOffset + SubHeaderSize;

        int slotsPerList = (recordSize - RecordHeaderSize) / SlotSize;
        if (count <= 0 || slotsPerList <= 0)
            return Array.Empty<SetList>();

        var setLists = new List<SetList>(count);
        for (int k = 0; k < count; k++)
        {
            long record = recordsStart + (long)k * recordSize;
            if (record + recordSize > sbk.DataEnd)
                break;

            string name = PcgText.ReadFixedString(data, record, SetListNameLength);

            var slots = new List<SetListSlot>(slotsPerList);
            for (int j = 0; j < slotsPerList; j++)
            {
                long slotBase = record + RecordHeaderSize + (long)j * SlotSize;
                slots.Add(new SetListSlot
                {
                    Index = j,
                    Name = PcgText.ReadFixedString(data, slotBase + SlotNameOffset, SlotNameLength),
                    Reference = DecodeReference(ReadBytes(data, slotBase + SlotRefOffset, SlotRefLength)),
                    Description = PcgText.ReadFixedString(data, slotBase + SlotDescriptionOffset, SlotDescriptionLength),
                });
            }

            setLists.Add(new SetList { Index = k, Name = name, Slots = slots });
        }

        return setLists;
    }

    // Reference bytes B0 B1 B2 (bytes 3–5 are the slot's color/volume/transpose):
    //   Type  = B0 & 0x03  (Combi=0, Program=1, Song=2)
    //   Bank  = B1 & 0x1F
    //   Index = B2 & 0x7F
    private static SetListReference DecodeReference(byte[] raw)
    {
        byte b0 = raw.Length > 0 ? raw[0] : (byte)0;
        byte b1 = raw.Length > 1 ? raw[1] : (byte)0;
        byte b2 = raw.Length > 2 ? raw[2] : (byte)0;
        return new SetListReference
        {
            Kind = (b0 & 0x03) switch
            {
                1 => PcgItemKind.Program,
                2 => PcgItemKind.Song,
                _ => PcgItemKind.Combi,
            },
            Bank = b1 & 0x1F,
            Index = b2 & 0x7F,
            Raw = raw,
        };
    }

    private static byte[] ReadBytes(byte[] data, long offset, int length)
    {
        if (offset < 0 || offset >= data.Length)
            return Array.Empty<byte>();
        int start = (int)offset;
        int n = (int)Math.Min(length, data.Length - start);
        if (n <= 0)
            return Array.Empty<byte>();
        var buffer = new byte[n];
        Array.Copy(data, start, buffer, 0, n);
        return buffer;
    }
}
