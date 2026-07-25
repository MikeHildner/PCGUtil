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
                var raw = ReadBytes(data, slotBase + SlotRefOffset, SlotRefLength);
                slots.Add(new SetListSlot
                {
                    Index = j,
                    Name = PcgText.ReadFixedString(data, slotBase + SlotNameOffset, SlotNameLength),
                    Reference = DecodeReference(raw),
                    Description = PcgText.ReadFixedString(data, slotBase + SlotDescriptionOffset, SlotDescriptionLength),
                    Color = raw.Length > 0 ? (raw[0] >> 2) & 0x0F : 0,
                    HoldTimeIndex = raw.Length > 3 ? raw[3] : SetListHoldTimes.DefaultIndex,
                    Volume = raw.Length > 4 ? raw[4] : 127,
                    Transpose = raw.Length > 5 ? DecodeTranspose(raw[1], raw[5]) : 0,
                });
            }

            setLists.Add(new SetList { Index = k, Name = name, Slots = slots });
        }

        return setLists;
    }

    // Each record is 24 (name) + 128 x 542 (slots) = 69400 bytes, but the record size is
    // 69416 — a 16-byte trailing region follows the slots. Only its byte +10 is ever
    // non-zero on real files, holding 5 on exactly the set lists that have been written on
    // the instrument and 0 on the rest (probe 2026-07-25: editing set list 000 flipped its
    // byte from 0 to 5, nothing else in the region moved). The Edit page's **Font** button
    // is the leading suspect — a per-set-list comment font size sitting at its default —
    // but that is untested, so nothing here decodes or writes the region.
    //
    // NOT here: any per-slot "keyboard track". The Edit page's dropdown beside the slot
    // number is the category/program picker for what the slot loads (verified by probe:
    // choosing new programs for two slots moved only their reference bytes).

    // Reference bytes B0 B1 B2:
    //   Type  = B0 & 0x03        (Combi=0, Program=1, Song=2)
    //   Color = (B0 >> 2) & 0x0F (probe-verified: 16 slots colored in picker order → 4j+kind)
    //   Bank  = B1 & 0x1F        (top 3 bits = transpose high bits — always mask)
    //   Index = B2 & 0x7F
    // Bytes 3–5: hold-time index 0–22 (default 6 = 5 s — NOT a font size; probe-verified
    // 30 s→18, 50 s→21), volume 0–127, transpose low byte. Transpose = semitones ×32 in an
    // 11-bit signed field across byte 5 and B1's top bits (probe: +2→0x40, −1→0xE0/0xE0,
    // +8→B1 0x20 with byte 5 zero).

    // 11-bit signed (B1 bits 5–7 high, byte 5 low), ÷32 → semitones −12..+12.
    private static int DecodeTranspose(byte b1, byte low)
    {
        int value = ((b1 >> 5) << 8) | low;
        if (value >= 1024)
            value -= 2048;
        return value / 32;
    }
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
