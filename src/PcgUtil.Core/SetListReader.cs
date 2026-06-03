using System.Buffers.Binary;
using System.Text;

namespace PcgUtil.Core;

/// <summary>
/// Decodes Set Lists from the <c>SBK1</c> chunk.
///
/// Layout (reverse-engineered from observed files): a 12-byte sub-header
/// (count, record size) followed by <c>count</c> records. Each record is one Set
/// List: a 40-byte header whose first 24 bytes are the set-list name, then 128
/// slots of 542 bytes. Within a slot, the 24-byte name sits at offset 526 and a
/// 6-byte reference (Program/Combi/Song target, not yet decoded) sits at offset 8.
/// </summary>
public static class SetListReader
{
    public const int SubHeaderSize = 12;
    public const int RecordHeaderSize = 40;
    public const int SlotSize = 542;
    public const int SetListNameLength = 24;
    public const int SlotNameOffset = 526;
    public const int SlotNameLength = 24;
    public const int SlotRefOffset = 8;
    public const int SlotRefLength = 6;

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

            string name = ReadFixedString(data, record, SetListNameLength);

            var slots = new List<SetListSlot>(slotsPerList);
            for (int j = 0; j < slotsPerList; j++)
            {
                long slot = record + RecordHeaderSize + (long)j * SlotSize;
                slots.Add(new SetListSlot
                {
                    Index = j,
                    Name = ReadFixedString(data, slot + SlotNameOffset, SlotNameLength),
                    Reference = ReadBytes(data, slot + SlotRefOffset, SlotRefLength),
                });
            }

            setLists.Add(new SetList { Index = k, Name = name, Slots = slots });
        }

        return setLists;
    }

    // Reads a NUL-terminated (or full-width) fixed-length ASCII field.
    private static string ReadFixedString(byte[] data, long offset, int maxLen)
    {
        int start = (int)offset;
        int end = (int)Math.Min(offset + maxLen, data.Length);
        int len = 0;
        for (int i = start; i < end; i++)
        {
            if (data[i] == 0)
                break;
            len++;
        }
        return Encoding.ASCII.GetString(data, start, len).TrimEnd();
    }

    private static byte[] ReadBytes(byte[] data, long offset, int length)
    {
        int start = (int)offset;
        int n = (int)Math.Min(length, data.Length - start);
        if (n <= 0)
            return Array.Empty<byte>();
        var buffer = new byte[n];
        Array.Copy(data, start, buffer, 0, n);
        return buffer;
    }
}
