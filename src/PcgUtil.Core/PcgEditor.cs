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

    // ----- Combis -----
    //
    // A combi is referenced only by combi-type Set List slots, so reorganizing combis
    // is safe as long as those slot references are retargeted to follow the combi.

    /// <summary>
    /// Returns a copy with two combis swapped. The combi records are exchanged and the
    /// combi-type Set List slot references are retargeted (A↔B), so every set list keeps
    /// loading the same combi.
    /// </summary>
    public static byte[] SwapCombis(PcgFile pcg, int bankA, int indexA, int bankB, int indexB)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (offsetA, size) = LocateCombi(pcg, bankA, indexA);
        var (offsetB, sizeB) = LocateCombi(pcg, bankB, indexB);

        var data = (byte[])pcg.Data.Clone();
        if (offsetA != offsetB && size == sizeB)
        {
            for (int i = 0; i < size; i++)
                (data[offsetA + i], data[offsetB + i]) = (data[offsetB + i], data[offsetA + i]);

            if (pcg.FindFirst("SBK1") is not null)
                RetargetCombiReferences(data, GetLayout(pcg), bankA, indexA, bankB, indexB);
        }
        return data;
    }

    /// <summary>Returns a copy with one combi copied over another. The destination is overwritten.</summary>
    public static byte[] CopyCombi(PcgFile pcg, int srcBank, int srcIndex, int dstBank, int dstIndex)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (src, size) = LocateCombi(pcg, srcBank, srcIndex);
        var (dst, dstSize) = LocateCombi(pcg, dstBank, dstIndex);

        var data = (byte[])pcg.Data.Clone();
        if (src != dst && size == dstSize)
            Array.Copy(data, src, data, dst, size);
        return data;
    }

    /// <summary>Returns a copy with a combi's name field rewritten (24 chars, ASCII).</summary>
    public static byte[] RenameCombi(PcgFile pcg, int bank, int index, string name)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (offset, _) = LocateCombi(pcg, bank, index);

        var data = (byte[])pcg.Data.Clone();
        PcgText.WriteFixedString(data, offset, BankNameLength, name);
        return data;
    }

    private const int BankSubHeaderSize = 12;
    private const int BankNameLength = 24;

    // Combi name is at record offset 0; bank data is a 12-byte sub-header then records.
    private static (long Offset, int RecordSize) LocateCombi(PcgFile pcg, int bank, int index)
    {
        var cmb = pcg.FindFirst("CMB1")
            ?? throw new InvalidOperationException("File has no CMB1 (Combi) chunk.");
        if (bank < 0 || bank >= cmb.Children.Count)
            throw new ArgumentOutOfRangeException(nameof(bank));

        long baseOffset = cmb.Children[bank].DataOffset;
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset, 4));
        int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset + 4, 4));
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index));

        return (baseOffset + BankSubHeaderSize + (long)index * recordSize, recordSize);
    }

    private static void RetargetCombiReferences(byte[] data, SetListLayout layout, int bankA, int indexA, int bankB, int indexB)
    {
        for (int setList = 0; setList < layout.Count; setList++)
        {
            long record = layout.RecordsStart + (long)setList * layout.RecordSize;
            for (int slot = 0; slot < layout.SlotsPerList; slot++)
            {
                long refOffset = record + SetListReader.RecordHeaderSize
                    + (long)slot * SetListReader.SlotSize + SetListReader.SlotRefOffset;
                if (refOffset + 3 > data.Length)
                    continue;

                // Only retarget true Combi-type slots. Type = B0 & 0x03 (Combi=0,
                // Program=1, Song=2); Program and Song slots must be left untouched.
                if ((data[refOffset] & 0x03) != 0)
                    continue;

                int bank = data[refOffset + 1] & 0x1F;
                int index = data[refOffset + 2] & 0x7F;
                if (bank == bankA && index == indexA)
                    WriteCombiReference(data, refOffset, bankB, indexB);
                else if (bank == bankB && index == indexB)
                    WriteCombiReference(data, refOffset, bankA, indexA);
            }
        }
    }

    // Rewrites bank (B1, low 5 bits) and number (B2, low 7 bits), preserving the other bits.
    private static void WriteCombiReference(byte[] data, long refOffset, int bank, int index)
    {
        data[refOffset + 1] = (byte)((data[refOffset + 1] & 0xE0) | (bank & 0x1F));
        data[refOffset + 2] = (byte)((data[refOffset + 2] & 0x80) | (index & 0x7F));
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
