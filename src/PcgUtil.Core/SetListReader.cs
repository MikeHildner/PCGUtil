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
                    CommentFont = raw.Length > 5 ? raw[5] & CommentFontMask : 0,
                });
            }

            setLists.Add(new SetList { Index = k, Name = name, Slots = slots });
        }

        return setLists;
    }

    // Each record is 24 (name) + 128 x 542 (slots) = 69400 bytes, but the record size is
    // 69416 — a 16-byte trailing region follows the slots. The vendor SysEx dump names it
    // (SetList object, offsets 69400+): +0 EQ bypass, +1..+9 the nine set-list EQ band
    // levels (signed, -18.0..+18.0 dB), +10 Control Surface Mode (0-8), +11 Control Surface
    // "assign from" (set list / slot), +12..+15 reserved. That settles the long-standing
    // question about byte +10: the 5 seen on every set list written on the instrument is a
    // control-surface view, not a slot-display mode, and the all-zero bytes around it are a
    // flat, un-bypassed EQ. Still nothing writes this region.
    //
    // The same dump calls slot byte +29 a per-slot "Keyboard Track" (0-15, track 1-16),
    // which cannot be the whole story here: bits 5-7 carry the transpose high bits and the
    // comment font occupies bits 2-4 (XS/S/M/L/XL as 0/4/8/12/16, hardware-confirmed),
    // leaving only bits 0-1. That dump is Object Version 0 and predates the comment font,
    // so the byte was evidently repacked in a later OS. Harmless either way: every writer
    // masks CommentFontMask and preserves bits 0-4 verbatim, so whatever the current OS
    // keeps down there survives our edits untouched. A scan of three real backups found
    // bits 0-4 zero on all 16384 slots but two — one Song slot carrying 8, and the font
    // probe's own XL.

    // Reference bytes B0 B1 B2:
    //   Type  = B0 & 0x03        (Combi=0, Program=1, Song=2)
    //   Color = (B0 >> 2) & 0x0F (probe-verified: 16 slots colored in picker order → 4j+kind)
    //   Bank  = B1 & 0x1F        (top 3 bits = transpose high bits — always mask)
    //   Index = B2 & 0x7F
    // Bytes 3–5: hold-time index 0–22 (default 6 = 5 s — NOT a font size; probe-verified
    // 30 s→18, 50 s→21), volume 0–127, then a byte shared by two fields. Transpose =
    // semitones ×32 in a signed field across byte 5's TOP 3 BITS and B1's top bits (probe:
    // +2→0x40, −1→0xE0/0xE0, +8→B1 0x20 with byte 5 zero); because the value is always a
    // multiple of 32, byte 5's low 5 bits are free — and that is where the comment FONT
    // lives (probe 2026-07-25: changing a slot's Font moved those bits alone, leaving its
    // transpose at 0; the factory demo slot has carried 8 there all along). The font
    // values aren't mapped to the instrument's list yet, so they are preserved verbatim.

    // 11-bit signed (B1 bits 5–7 high, byte 5 low), ÷32 → semitones −12..+12. Byte 5's low
    // 5 bits belong to the comment font (see CommentFontOffsetMask) and must be masked off
    // first: leaving them in makes a negative transpose truncate toward zero (−1 with font
    // 16 would read as 0), which is exactly the bug the font probe exposed 2026-07-25.
    private static int DecodeTranspose(byte b1, byte low)
    {
        int value = ((b1 >> 5) << 8) | (low & ~CommentFontMask & 0xFF);
        if (value >= 1024)
            value -= 2048;
        return value / 32;
    }

    /// <summary>Byte 5 of a slot reference carries the comment font in its low 5 bits;
    /// transpose (semitones ×32) never uses them.</summary>
    public const int CommentFontMask = 0x1F;
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
