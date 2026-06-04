using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Minimal, surgical edits to a PCG byte image. Each edit works on a copy of the
/// bytes and only touches the affected fixed-size record blocks (plus, where needed,
/// the reference bytes that point at them), leaving every other byte — including
/// fields we have not decoded — exactly as it was.
/// </summary>
public static class PcgEditor
{
    // ----- Set List slots -----

    /// <summary>
    /// Returns a copy with two Set List slots swapped. The whole 542-byte slot block
    /// moves, so the name, reference, and comment travel together.
    /// </summary>
    public static byte[] SwapSetListSlots(PcgFile pcg, int setListIndex, int slotA, int slotB)
    {
        var layout = GetLayout(pcg);
        ValidateSlot(layout, setListIndex, slotA, nameof(slotA));
        ValidateSlot(layout, setListIndex, slotB, nameof(slotB));

        var data = (byte[])pcg.Data.Clone();
        if (slotA != slotB)
        {
            long a = SlotOffset(layout, setListIndex, slotA);
            long b = SlotOffset(layout, setListIndex, slotB);
            for (int i = 0; i < SetListReader.SlotSize; i++)
                (data[a + i], data[b + i]) = (data[b + i], data[a + i]);
        }
        return data;
    }

    /// <summary>
    /// Returns a copy with a Set List slot copied over another (within or across set
    /// lists). The destination slot is overwritten.
    /// </summary>
    public static byte[] CopySetListSlot(PcgFile pcg, int srcSetList, int srcSlot, int dstSetList, int dstSlot)
    {
        var layout = GetLayout(pcg);
        ValidateSlot(layout, srcSetList, srcSlot, nameof(srcSlot));
        ValidateSlot(layout, dstSetList, dstSlot, nameof(dstSlot));

        var data = (byte[])pcg.Data.Clone();
        long src = SlotOffset(layout, srcSetList, srcSlot);
        long dst = SlotOffset(layout, dstSetList, dstSlot);
        if (src != dst)
            Array.Copy(data, src, data, dst, SetListReader.SlotSize);
        return data;
    }

    /// <summary>Returns a copy with a slot's name field rewritten (24 chars, ASCII).</summary>
    public static byte[] RenameSetListSlot(PcgFile pcg, int setListIndex, int slot, string name)
    {
        var layout = GetLayout(pcg);
        ValidateSlot(layout, setListIndex, slot, nameof(slot));

        var data = (byte[])pcg.Data.Clone();
        long offset = SlotOffset(layout, setListIndex, slot) + SetListReader.SlotNameOffset;
        PcgText.WriteFixedString(data, offset, SetListReader.SlotNameLength, name);
        return data;
    }

    /// <summary>Returns a copy with a set list's name field rewritten (24 chars, ASCII).</summary>
    public static byte[] RenameSetList(PcgFile pcg, int setListIndex, string name)
    {
        var layout = GetLayout(pcg);
        ValidateSetList(layout, setListIndex);

        var data = (byte[])pcg.Data.Clone();
        PcgText.WriteFixedString(data, RecordOffset(layout, setListIndex), SetListReader.SetListNameLength, name);
        return data;
    }

    // ----- Set List layout helpers -----

    private readonly record struct SetListLayout(long RecordsStart, int RecordSize, int Count, int SlotsPerList);

    private static SetListLayout GetLayout(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var sbk = pcg.FindFirst("SBK1")
            ?? throw new InvalidOperationException("File has no SBK1 (Set List) chunk.");

        long baseOffset = sbk.DataOffset;
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset, 4));
        int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset + 4, 4));
        int slotsPerList = (recordSize - SetListReader.RecordHeaderSize) / SetListReader.SlotSize;
        return new SetListLayout(baseOffset + SetListReader.SubHeaderSize, recordSize, count, slotsPerList);
    }

    private static long RecordOffset(SetListLayout layout, int setListIndex) =>
        layout.RecordsStart + (long)setListIndex * layout.RecordSize;

    private static long SlotOffset(SetListLayout layout, int setListIndex, int slot) =>
        RecordOffset(layout, setListIndex) + SetListReader.RecordHeaderSize + (long)slot * SetListReader.SlotSize;

    private static void ValidateSetList(SetListLayout layout, int setListIndex)
    {
        if (setListIndex < 0 || setListIndex >= layout.Count)
            throw new ArgumentOutOfRangeException(nameof(setListIndex));
    }

    private static void ValidateSlot(SetListLayout layout, int setListIndex, int slot, string paramName)
    {
        ValidateSetList(layout, setListIndex);
        if (slot < 0 || slot >= layout.SlotsPerList)
            throw new ArgumentOutOfRangeException(paramName);
    }
}
