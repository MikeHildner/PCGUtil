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
        return Finalized(pcg, data);
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
        return Finalized(pcg, data);
    }

    /// <summary>
    /// Returns a copy of <paramref name="destination"/> with one Set List slot overwritten by a
    /// slot from another file. The 542-byte block carries the name and reference verbatim; the
    /// reference will resolve against whatever lives in the destination's banks.
    /// </summary>
    public static byte[] CopySetListSlotAcross(PcgFile source, int srcSetList, int srcSlot,
                                               PcgFile destination, int dstSetList, int dstSlot)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var srcLayout = GetLayout(source);
        var dstLayout = GetLayout(destination);
        ValidateSlot(srcLayout, srcSetList, srcSlot, nameof(srcSlot));
        ValidateSlot(dstLayout, dstSetList, dstSlot, nameof(dstSlot));
        RequireSameRecordSize("Set List", srcLayout.RecordSize, dstLayout.RecordSize);

        var data = (byte[])destination.Data.Clone();
        Array.Copy(source.Data, SlotOffset(srcLayout, srcSetList, srcSlot),
                   data, SlotOffset(dstLayout, dstSetList, dstSlot), SetListReader.SlotSize);
        return Finalized(destination, data);
    }

    /// <summary>Returns a copy with a slot's name field rewritten (24 chars, ASCII).</summary>
    public static byte[] RenameSetListSlot(PcgFile pcg, int setListIndex, int slot, string name)
    {
        var layout = GetLayout(pcg);
        ValidateSlot(layout, setListIndex, slot, nameof(slot));

        var data = (byte[])pcg.Data.Clone();
        long offset = SlotOffset(layout, setListIndex, slot) + SetListReader.SlotNameOffset;
        PcgText.WriteFixedString(data, offset, SetListReader.SlotNameLength, name);
        return Finalized(pcg, data);
    }

    /// <summary>Returns a copy with a slot's description (comment) field rewritten
    /// (up to 512 ASCII chars, line breaks allowed).</summary>
    public static byte[] SetSetListSlotDescription(PcgFile pcg, int setListIndex, int slot, string description)
    {
        var layout = GetLayout(pcg);
        ValidateSlot(layout, setListIndex, slot, nameof(slot));

        var data = (byte[])pcg.Data.Clone();
        long offset = SlotOffset(layout, setListIndex, slot) + SetListReader.SlotDescriptionOffset;
        PcgText.WriteFixedString(data, offset, SetListReader.SlotDescriptionLength, description ?? string.Empty);
        return Finalized(pcg, data);
    }

    /// <summary>
    /// Returns a copy with a slot re-pointed at a different Program or Combi — the slot's name,
    /// description, and other settings stay put; only the reference bytes change. For programs,
    /// <paramref name="bank"/> is the bank <em>list index</em> (mapped to the hardware PcgId here);
    /// for combis it's the direct bank index.
    /// </summary>
    public static byte[] RepointSetListSlot(PcgFile pcg, int setListIndex, int slot,
                                            PcgItemKind kind, int bank, int index)
    {
        var layout = GetLayout(pcg);
        ValidateSlot(layout, setListIndex, slot, nameof(slot));

        int bankByte;
        if (kind == PcgItemKind.Program)
        {
            var (_, _, count) = LocateBank(pcg, "PRG1", bank); // validates the bank list index
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index));
            bankByte = PcgCatalog.ProgramBankPcgIdForIndex(bank);
        }
        else if (kind == PcgItemKind.Combi)
        {
            var (_, _, count) = LocateBank(pcg, "CMB1", bank);
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index));
            bankByte = bank;
        }
        else
        {
            throw new ArgumentException("Slots can only be re-pointed at a Program or Combi.", nameof(kind));
        }

        var data = (byte[])pcg.Data.Clone();
        long refOffset = SlotOffset(layout, setListIndex, slot) + SetListReader.SlotRefOffset;
        data[refOffset] = (byte)((data[refOffset] & ~0x03) | (int)kind); // type in B0's low 2 bits
        WriteSlotReference(data, refOffset, bankByte, index);
        return Finalized(pcg, data);
    }

    /// <summary>Returns a copy with a set list's name field rewritten (24 chars, ASCII).</summary>
    public static byte[] RenameSetList(PcgFile pcg, int setListIndex, string name)
    {
        var layout = GetLayout(pcg);
        ValidateSetList(layout, setListIndex);

        var data = (byte[])pcg.Data.Clone();
        PcgText.WriteFixedString(data, RecordOffset(layout, setListIndex), SetListReader.SetListNameLength, name);
        return Finalized(pcg, data);
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
        return Finalized(pcg, data);
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
        return Finalized(pcg, data);
    }

    /// <summary>
    /// Returns a copy of <paramref name="destination"/> with one combi overwritten by a combi
    /// from another file. The whole record travels — name, timbres, parameters — but the timbre
    /// program references resolve against the destination's banks, so the combi plays whatever
    /// lives at those slots there.
    /// </summary>
    public static byte[] CopyCombiAcross(PcgFile source, int srcBank, int srcIndex,
                                         PcgFile destination, int dstBank, int dstIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var (srcOffset, srcSize) = LocateCombi(source, srcBank, srcIndex);
        var (dstOffset, dstSize) = LocateCombi(destination, dstBank, dstIndex);
        RequireSameRecordSize("Combi", srcSize, dstSize);

        var data = (byte[])destination.Data.Clone();
        Array.Copy(source.Data, srcOffset, data, dstOffset, dstSize);
        return Finalized(destination, data);
    }

    /// <summary>
    /// Applies a <see cref="DeepCopyPlan"/>: copies the plan's combi <em>and</em> the programs
    /// its timbres reference (planned copies into free slots; planned reuses point at programs
    /// the destination already holds), then rewrites the copied combi's timbre bytes to the new
    /// addresses so the copy keeps its sound. All writes land in one buffer with one checksum
    /// recompute. Timbres the plan skipped, and all KARMA settings, travel byte-for-byte.
    /// </summary>
    public static byte[] CopyCombiDeepAcross(PcgFile source, PcgFile destination,
                                             int dstBank, int dstIndex, DeepCopyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Error is not null)
            throw new InvalidOperationException(plan.Error);

        var (srcCombiOffset, srcCombiSize) = LocateCombi(source, plan.SourceBank, plan.SourceIndex);
        var (dstCombiOffset, dstCombiSize) = LocateCombi(destination, dstBank, dstIndex);
        RequireSameRecordSize("Combi", srcCombiSize, dstCombiSize);

        // Locate and re-validate every program mapping against the *current* destination —
        // a stale plan (slots filled or records changed since planning) must fail loudly
        // rather than overwrite something real.
        var writes = new List<(long SrcOffset, long DstOffset, int Size)>();
        var claimed = new HashSet<(int Bank, int Index)>();
        foreach (var m in plan.Programs)
        {
            var (srcOffset, srcSize) = LocateProgram(source, m.SourceBank, m.SourceIndex);
            var (dstOffset, dstSize) = LocateProgram(destination, m.DestinationBank, m.DestinationIndex);
            RequireSameRecordSize("Program", srcSize, dstSize);

            if (m.Action == DeepCopyProgramAction.Copy)
            {
                var occupant = PcgText.ReadFixedString(destination.Data, dstOffset, BankNameLength);
                if (!PcgOrganizer.IsProgramPlaceholder(occupant) || !claimed.Add((m.DestinationBank, m.DestinationIndex)))
                    throw new InvalidOperationException(
                        "The plan no longer matches the destination file — recompute it and try again.");
                writes.Add((srcOffset, dstOffset, srcSize));
            }
            else if (!source.Data.AsSpan((int)srcOffset, srcSize)
                         .SequenceEqual(destination.Data.AsSpan((int)dstOffset, dstSize)))
            {
                throw new InvalidOperationException(
                    "The plan no longer matches the destination file — recompute it and try again.");
            }
        }

        var data = (byte[])destination.Data.Clone();
        foreach (var (srcOffset, dstOffset, size) in writes)
            Array.Copy(source.Data, srcOffset, data, dstOffset, size);
        Array.Copy(source.Data, srcCombiOffset, data, dstCombiOffset, dstCombiSize);

        // Re-point the copied combi's timbres at where their programs landed. Writes go by
        // the plan's timbre indices, never by byte matching, so skipped timbres that happen
        // to share an address are left alone.
        foreach (var m in plan.Programs)
        {
            byte pcgId = (byte)PcgCatalog.ProgramBankPcgIdForIndex(m.DestinationBank);
            foreach (var t in m.Timbres)
            {
                long tOff = dstCombiOffset + CombiReader.TimbresOffset + (long)t * CombiReader.TimbreStride;
                if (CombiReader.TimbresOffset + (t + 1) * CombiReader.TimbreStride > dstCombiSize)
                    throw new InvalidOperationException($"Timbre {t + 1} lies outside the combi record.");
                data[tOff] = (byte)m.DestinationIndex;
                data[tOff + 1] = pcgId;
            }
        }

        return Finalized(destination, data);
    }

    /// <summary>Returns a copy with a combi's name field rewritten (24 chars, ASCII).</summary>
    public static byte[] RenameCombi(PcgFile pcg, int bank, int index, string name)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (offset, _) = LocateCombi(pcg, bank, index);

        var data = (byte[])pcg.Data.Clone();
        PcgText.WriteFixedString(data, offset, BankNameLength, name);
        return Finalized(pcg, data);
    }

    /// <summary>
    /// Returns a copy with one combi bank's records rearranged in a single pass: the record at
    /// position <c>i</c> afterwards is the one that was at <c>newOrder[i]</c> (<paramref name="newOrder"/>
    /// must be a permutation of 0..count-1). Combi-type Set List slot references into the bank are
    /// retargeted to follow their records, so every set list keeps loading the same combi.
    /// </summary>
    public static byte[] ReorderCombis(PcgFile pcg, int bank, IReadOnlyList<int> newOrder)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (recordsStart, recordSize, count) = LocateBank(pcg, "CMB1", bank);
        var newIndexOfOld = InverseOf(newOrder, count);

        var data = (byte[])pcg.Data.Clone();
        for (int i = 0; i < count; i++)
            if (newOrder[i] != i)
                Array.Copy(pcg.Data, recordsStart + (long)newOrder[i] * recordSize,
                           data, recordsStart + (long)i * recordSize, recordSize);

        if (pcg.FindFirst("SBK1") is not null)
            RetargetCombiReferences(data, GetLayout(pcg), bank, newIndexOfOld);
        return Finalized(pcg, data);
    }

    // Retargets combi-type slot references after a whole-bank permutation. newIndexOfOld maps each
    // record's old index to its new one; references into other banks are untouched.
    private static void RetargetCombiReferences(byte[] data, SetListLayout layout, int bank, int[] newIndexOfOld)
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
                if ((data[refOffset] & 0x03) != 0) // combi-type slots only
                    continue;
                if ((data[refOffset + 1] & 0x1F) != bank)
                    continue;

                int index = data[refOffset + 2] & 0x7F;
                if (index < newIndexOfOld.Length && newIndexOfOld[index] != index)
                    data[refOffset + 2] = (byte)((data[refOffset + 2] & 0x80) | (newIndexOfOld[index] & 0x7F));
            }
        }
    }

    // Validates that newOrder is a permutation of 0..count-1 and returns its inverse
    // (old record index → new record index).
    private static int[] InverseOf(IReadOnlyList<int> newOrder, int count)
    {
        ArgumentNullException.ThrowIfNull(newOrder);
        if (newOrder.Count != count)
            throw new ArgumentException($"Expected {count} entries, got {newOrder.Count}.", nameof(newOrder));

        var inverse = new int[count];
        Array.Fill(inverse, -1);
        for (int i = 0; i < count; i++)
        {
            int old = newOrder[i];
            if (old < 0 || old >= count || inverse[old] != -1)
                throw new ArgumentException("newOrder must be a permutation of 0..count-1.", nameof(newOrder));
            inverse[old] = i;
        }
        return inverse;
    }

    private const int BankSubHeaderSize = 12;
    private const int BankNameLength = 24;

    // Combi name is at record offset 0; bank data is a 12-byte sub-header then records.
    private static (long Offset, int RecordSize) LocateCombi(PcgFile pcg, int bank, int index)
    {
        var (recordsStart, recordSize, count) = LocateBank(pcg, "CMB1", bank);
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (recordsStart + (long)index * recordSize, recordSize);
    }

    // Locates one bank's fixed-size record table (12-byte sub-header: count, record size).
    // Banks are addressed by canonical list index; partial files may not carry every bank.
    // Internal so PcgDeepCopy can address records without re-parsing sub-headers.
    internal static (long RecordsStart, int RecordSize, int Count) LocateBank(PcgFile pcg, string sectionId, int bank)
    {
        if (pcg.FindFirst(sectionId) is null)
            throw new InvalidOperationException($"File has no {sectionId} chunk.");
        var banks = PcgBankIdentity.CanonicalBanks(pcg, sectionId);
        if (bank < 0 || bank >= banks.Count)
            throw new ArgumentOutOfRangeException(nameof(bank));
        var chunk = banks[bank]
            ?? throw new InvalidOperationException($"This file does not contain {sectionId} bank {bank}.");

        long baseOffset = chunk.DataOffset;
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset, 4));
        int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset + 4, 4));
        long recordsStart = baseOffset + BankSubHeaderSize;
        if (count < 0 || recordSize <= 0 || recordsStart + (long)count * recordSize > pcg.Data.Length)
            throw new InvalidOperationException($"{sectionId} bank {bank} record table is out of bounds.");
        return (recordsStart, recordSize, count);
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
                    WriteSlotReference(data, refOffset, bankB, indexB);
                else if (bank == bankB && index == indexB)
                    WriteSlotReference(data, refOffset, bankA, indexA);
            }
        }
    }

    // Rewrites bank (B1, low 5 bits) and number (B2, low 7 bits), preserving the other bits.
    private static void WriteSlotReference(byte[] data, long refOffset, int bank, int index)
    {
        data[refOffset + 1] = (byte)((data[refOffset + 1] & 0xE0) | (bank & 0x1F));
        data[refOffset + 2] = (byte)((data[refOffset + 2] & 0x80) | (index & 0x7F));
    }

    // Every edit ends here: recompute per-chunk checksums so the hardware accepts the file.
    private static byte[] Finalized(PcgFile pcg, byte[] data)
    {
        PcgChecksum.Recompute(pcg, data);
        return data;
    }

    // ----- Programs -----
    //
    // A program is referenced by combi timbres (number @ timbre+0, bank PcgId @ timbre+1) and by
    // program-type Set List slots (bank PcgId @ slot+25, number @ slot+26). Reorganizing programs
    // is safe only if both reference graphs are retargeted to follow the moved records.

    /// <summary>
    /// Returns a copy with two programs swapped. The records are exchanged and every combi timbre
    /// and program-type Set List slot that referenced them is retargeted (A↔B), so the file keeps
    /// loading the same program everywhere.
    /// </summary>
    public static byte[] SwapPrograms(PcgFile pcg, int bankA, int indexA, int bankB, int indexB)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (offsetA, size) = LocateProgram(pcg, bankA, indexA);
        var (offsetB, sizeB) = LocateProgram(pcg, bankB, indexB);

        var data = (byte[])pcg.Data.Clone();
        if (offsetA != offsetB && size == sizeB)
        {
            for (int i = 0; i < size; i++)
                (data[offsetA + i], data[offsetB + i]) = (data[offsetB + i], data[offsetA + i]);

            RetargetProgramReferences(pcg, data, bankA, indexA, bankB, indexB);
        }
        return Finalized(pcg, data);
    }

    /// <summary>Returns a copy with one program copied over another. The destination is overwritten.</summary>
    public static byte[] CopyProgram(PcgFile pcg, int srcBank, int srcIndex, int dstBank, int dstIndex)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (src, size) = LocateProgram(pcg, srcBank, srcIndex);
        var (dst, dstSize) = LocateProgram(pcg, dstBank, dstIndex);

        var data = (byte[])pcg.Data.Clone();
        if (src != dst && size == dstSize)
            Array.Copy(data, src, data, dst, size);
        return Finalized(pcg, data);
    }

    /// <summary>
    /// Returns a copy of <paramref name="destination"/> with one program overwritten by a
    /// program from another file (whole record: name + parameters).
    /// </summary>
    public static byte[] CopyProgramAcross(PcgFile source, int srcBank, int srcIndex,
                                           PcgFile destination, int dstBank, int dstIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var (srcOffset, srcSize) = LocateProgram(source, srcBank, srcIndex);
        var (dstOffset, dstSize) = LocateProgram(destination, dstBank, dstIndex);
        RequireSameRecordSize("Program", srcSize, dstSize);

        var data = (byte[])destination.Data.Clone();
        Array.Copy(source.Data, srcOffset, data, dstOffset, dstSize);
        return Finalized(destination, data);
    }

    // Cross-file copies land raw bytes at computed offsets, so mismatched record layouts
    // (different models, or different OS versions that resized records) must be refused.
    private static void RequireSameRecordSize(string what, int srcSize, int dstSize)
    {
        if (srcSize != dstSize)
            throw new InvalidOperationException(
                $"{what} records differ in size ({srcSize} vs {dstSize} bytes) — " +
                "the files don't appear to be from the same model.");
    }

    /// <summary>Returns a copy with a program's name field rewritten (24 chars, ASCII).</summary>
    public static byte[] RenameProgram(PcgFile pcg, int bank, int index, string name)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (offset, _) = LocateProgram(pcg, bank, index);

        var data = (byte[])pcg.Data.Clone();
        PcgText.WriteFixedString(data, offset, BankNameLength, name);
        return Finalized(pcg, data);
    }

    /// <summary>
    /// Returns a copy with one program bank's records rearranged in a single pass: the record at
    /// position <c>i</c> afterwards is the one that was at <c>newOrder[i]</c> (<paramref name="newOrder"/>
    /// must be a permutation of 0..count-1). Every combi timbre and program-type Set List slot that
    /// references the bank is retargeted to follow its record, so the file keeps loading the same
    /// program everywhere.
    /// </summary>
    public static byte[] ReorderPrograms(PcgFile pcg, int bank, IReadOnlyList<int> newOrder)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (recordsStart, recordSize, count) = LocateBank(pcg, "PRG1", bank);
        var newIndexOfOld = InverseOf(newOrder, count);

        var data = (byte[])pcg.Data.Clone();
        for (int i = 0; i < count; i++)
            if (newOrder[i] != i)
                Array.Copy(pcg.Data, recordsStart + (long)newOrder[i] * recordSize,
                           data, recordsStart + (long)i * recordSize, recordSize);

        RetargetProgramReferences(pcg, data, bank, newIndexOfOld);
        return Finalized(pcg, data);
    }

    // Retargets both program reference graphs after a whole-bank permutation. newIndexOfOld maps
    // each record's old index to its new one; references into other banks are untouched, and the
    // bank byte never changes because the permutation stays within one bank.
    private static void RetargetProgramReferences(PcgFile pcg, byte[] data, int bank, int[] newIndexOfOld)
    {
        // Combi timbres (every CMB1 record). Each timbre: number @ +0, bank PcgId @ +1.
        var cmb = pcg.FindFirst("CMB1");
        if (cmb is not null)
        {
            foreach (var bankChunk in cmb.Children)
            {
                long baseOffset = bankChunk.DataOffset;
                if (baseOffset + BankSubHeaderSize > data.Length)
                    continue;
                int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
                int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));
                long recordsStart = baseOffset + BankSubHeaderSize;

                for (int i = 0; i < count; i++)
                {
                    long record = recordsStart + (long)i * recordSize;
                    if (record + recordSize > data.Length)
                        break;
                    for (int t = 0; t < CombiReader.TimbresPerCombi; t++)
                    {
                        long tOff = record + CombiReader.TimbresOffset + (long)t * CombiReader.TimbreStride;
                        if (tOff + 1 >= data.Length)
                            break;
                        if (PcgCatalog.ProgramBankIndexForPcgId(data[tOff + 1]) != bank)
                            continue;
                        int number = data[tOff];
                        if (number < newIndexOfOld.Length && newIndexOfOld[number] != number)
                            data[tOff] = (byte)newIndexOfOld[number];
                    }
                }
            }
        }

        // Program-type Set List slots (SBK1): bank PcgId in low 5 bits of +25, number in low 7 of +26.
        if (pcg.FindFirst("SBK1") is not null)
        {
            var layout = GetLayout(pcg);
            for (int setList = 0; setList < layout.Count; setList++)
            {
                long record = layout.RecordsStart + (long)setList * layout.RecordSize;
                for (int slot = 0; slot < layout.SlotsPerList; slot++)
                {
                    long refOffset = record + SetListReader.RecordHeaderSize
                        + (long)slot * SetListReader.SlotSize + SetListReader.SlotRefOffset;
                    if (refOffset + 2 >= data.Length)
                        continue;
                    if ((data[refOffset] & 0x03) != 1) // program-type slots only (Program == 1)
                        continue;
                    if (PcgCatalog.ProgramBankIndexForPcgId(data[refOffset + 1] & 0x1F) != bank)
                        continue;

                    int number = data[refOffset + 2] & 0x7F;
                    if (number < newIndexOfOld.Length && newIndexOfOld[number] != number)
                        data[refOffset + 2] = (byte)((data[refOffset + 2] & 0x80) | (newIndexOfOld[number] & 0x7F));
                }
            }
        }
    }

    // Program name is at record offset 0; bank data is a 12-byte sub-header then fixed records.
    private static (long Offset, int RecordSize) LocateProgram(PcgFile pcg, int bank, int index)
    {
        var (recordsStart, recordSize, count) = LocateBank(pcg, "PRG1", bank);
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (recordsStart + (long)index * recordSize, recordSize);
    }

    // Retargets both reference graphs after the programs at (bankA,indexA)/(bankB,indexB) swap.
    // Banks here are list indices; references store a PcgId, so we map both ways.
    private static void RetargetProgramReferences(PcgFile pcg, byte[] data, int bankA, int indexA, int bankB, int indexB)
    {
        int pcgIdA = PcgCatalog.ProgramBankPcgIdForIndex(bankA);
        int pcgIdB = PcgCatalog.ProgramBankPcgIdForIndex(bankB);

        // Combi timbres (every CMB1 record). Each timbre: number @ +0, bank PcgId @ +1.
        var cmb = pcg.FindFirst("CMB1");
        if (cmb is not null)
        {
            foreach (var bankChunk in cmb.Children)
            {
                long baseOffset = bankChunk.DataOffset;
                if (baseOffset + BankSubHeaderSize > data.Length)
                    continue;
                int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
                int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));
                long recordsStart = baseOffset + BankSubHeaderSize;

                for (int i = 0; i < count; i++)
                {
                    long record = recordsStart + (long)i * recordSize;
                    if (record + recordSize > data.Length)
                        break;
                    for (int t = 0; t < CombiReader.TimbresPerCombi; t++)
                    {
                        long tOff = record + CombiReader.TimbresOffset + (long)t * CombiReader.TimbreStride;
                        if (tOff + 1 >= data.Length)
                            break;
                        int mapped = PcgCatalog.ProgramBankIndexForPcgId(data[tOff + 1]);
                        int number = data[tOff];
                        if (mapped == bankA && number == indexA)
                        {
                            data[tOff] = (byte)indexB;
                            data[tOff + 1] = (byte)pcgIdB;
                        }
                        else if (mapped == bankB && number == indexB)
                        {
                            data[tOff] = (byte)indexA;
                            data[tOff + 1] = (byte)pcgIdA;
                        }
                    }
                }
            }
        }

        // Program-type Set List slots (SBK1): bank PcgId in low 5 bits of +25, number in low 7 of +26.
        if (pcg.FindFirst("SBK1") is not null)
        {
            var layout = GetLayout(pcg);
            for (int setList = 0; setList < layout.Count; setList++)
            {
                long record = layout.RecordsStart + (long)setList * layout.RecordSize;
                for (int slot = 0; slot < layout.SlotsPerList; slot++)
                {
                    long refOffset = record + SetListReader.RecordHeaderSize
                        + (long)slot * SetListReader.SlotSize + SetListReader.SlotRefOffset;
                    if (refOffset + 2 >= data.Length)
                        continue;
                    if ((data[refOffset] & 0x03) != 1) // program-type slots only (Program == 1)
                        continue;

                    int mapped = PcgCatalog.ProgramBankIndexForPcgId(data[refOffset + 1] & 0x1F);
                    int number = data[refOffset + 2] & 0x7F;
                    if (mapped == bankA && number == indexA)
                        WriteSlotReference(data, refOffset, pcgIdB, indexB);
                    else if (mapped == bankB && number == indexB)
                        WriteSlotReference(data, refOffset, pcgIdA, indexA);
                }
            }
        }
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
