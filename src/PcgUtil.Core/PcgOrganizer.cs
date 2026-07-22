namespace PcgUtil.Core;

/// <summary>
/// Reference-safe, bank-level reorganizations built on <see cref="PcgEditor"/>'s reorder:
/// sort a bank by name, or compact it (push placeholder records to the end). A placeholder
/// is a factory "Init Program" / "Init Combi" style record or one with an empty name; sort
/// sends placeholders to the end too, so they don't interleave under "I". Both operations
/// are stable — records they don't rank keep their relative order — and both retarget every
/// reference, so each patch keeps its sound.
/// </summary>
public static class PcgOrganizer
{
    /// <summary>
    /// Sorts one program bank by name (case-insensitive A→Z, placeholders last in their
    /// original order). Returns the edited bytes, or null when the bank is already in order.
    /// </summary>
    public static byte[]? SortProgramBankByName(PcgFile pcg, int bank)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var order = SortedOrder(BankNames(PcgCatalog.Build(pcg).ProgramBanks, bank), IsProgramPlaceholder);
        return order is null ? null : PcgEditor.ReorderPrograms(pcg, bank, order);
    }

    /// <summary>
    /// Pushes one program bank's placeholder records to the end, keeping everything else in
    /// its original order. Returns the edited bytes, or null when nothing would move.
    /// </summary>
    public static byte[]? CompactProgramBank(PcgFile pcg, int bank)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var order = CompactedOrder(BankNames(PcgCatalog.Build(pcg).ProgramBanks, bank), IsProgramPlaceholder);
        return order is null ? null : PcgEditor.ReorderPrograms(pcg, bank, order);
    }

    /// <summary>
    /// Sorts one combi bank by name (case-insensitive A→Z, placeholders last in their
    /// original order). Returns the edited bytes, or null when the bank is already in order.
    /// </summary>
    public static byte[]? SortCombiBankByName(PcgFile pcg, int bank)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var order = SortedOrder(BankNames(PcgCatalog.Build(pcg).CombiBanks, bank), Combi.IsEmptyOrInitName);
        return order is null ? null : PcgEditor.ReorderCombis(pcg, bank, order);
    }

    /// <summary>
    /// Pushes one combi bank's placeholder records to the end, keeping everything else in
    /// its original order. Returns the edited bytes, or null when nothing would move.
    /// </summary>
    public static byte[]? CompactCombiBank(PcgFile pcg, int bank)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var order = CompactedOrder(BankNames(PcgCatalog.Build(pcg).CombiBanks, bank), Combi.IsEmptyOrInitName);
        return order is null ? null : PcgEditor.ReorderCombis(pcg, bank, order);
    }

    /// <summary>
    /// Moves one combi to a new position within its bank (insert-shift: the record lands at
    /// <paramref name="to"/> and everything between shifts one step). Set-list references
    /// follow via <see cref="PcgEditor.ReorderCombis"/>. Returns null when from == to.
    /// </summary>
    public static byte[]? MoveCombiToPosition(PcgFile pcg, int bank, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (_, _, count) = PcgEditor.LocateBank(pcg, "CMB1", bank); // validates the bank
        var order = MovedOrder(count, from, to);
        return order is null ? null : PcgEditor.ReorderCombis(pcg, bank, order);
    }

    /// <summary>
    /// Moves one program to a new position within its bank (insert-shift). Combi timbres and
    /// program-type set-list slots follow via <see cref="PcgEditor.ReorderPrograms"/>.
    /// Returns null when from == to.
    /// </summary>
    public static byte[]? MoveProgramToPosition(PcgFile pcg, int bank, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (_, _, count) = PcgEditor.LocateBank(pcg, "PRG1", bank);
        var order = MovedOrder(count, from, to);
        return order is null ? null : PcgEditor.ReorderPrograms(pcg, bank, order);
    }

    /// <summary>
    /// Moves one set-list slot to a new position within its set list (insert-shift; whole
    /// slot blocks travel, nothing references a slot). Returns null when from == to.
    /// </summary>
    public static byte[]? MoveSetListSlot(PcgFile pcg, int setListIndex, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        int count = PcgEditor.SetListSlotsPerList(pcg);
        var order = MovedOrder(count, from, to);
        return order is null ? null : PcgEditor.ReorderSetListSlots(pcg, setListIndex, order);
    }

    /// <summary>A factory "Init Program" placeholder (covers "Init EXi Program"), or an empty name.</summary>
    public static bool IsProgramPlaceholder(string name) =>
        name.Length == 0 ||
        (name.Contains("Init", StringComparison.OrdinalIgnoreCase) &&
         name.Contains("Program", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BankNames(IReadOnlyList<IReadOnlyList<string>> banks, int bank)
    {
        if (bank < 0 || bank >= banks.Count)
            throw new ArgumentOutOfRangeException(nameof(bank));
        return banks[bank];
    }

    // newOrder for a sort: real records ranked by name (OrderBy is stable, so equal names
    // keep their relative order), then placeholders in their original order.
    private static int[]? SortedOrder(IReadOnlyList<string> names, Func<string, bool> isPlaceholder)
    {
        var indices = Enumerable.Range(0, names.Count).ToArray();
        var order = indices.Where(i => !isPlaceholder(names[i]))
            .OrderBy(i => names[i], StringComparer.OrdinalIgnoreCase)
            .Concat(indices.Where(i => isPlaceholder(names[i])))
            .ToArray();
        return IsIdentity(order) ? null : order;
    }

    // newOrder for a compact: real records in original order, then placeholders in original order.
    private static int[]? CompactedOrder(IReadOnlyList<string> names, Func<string, bool> isPlaceholder)
    {
        var indices = Enumerable.Range(0, names.Count).ToArray();
        var order = indices.Where(i => !isPlaceholder(names[i]))
            .Concat(indices.Where(i => isPlaceholder(names[i])))
            .ToArray();
        return IsIdentity(order) ? null : order;
    }

    // newOrder for a move-to-position, in ReorderX's convention: newOrder[newPos] = oldIndex.
    // Semantics match List.RemoveAt(from) then List.Insert(to, item): moving down (from < to)
    // closes the gap upward; moving up (from > to) pushes the block down. Null when from == to.
    private static int[]? MovedOrder(int count, int from, int to)
    {
        if (from < 0 || from >= count)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (to < 0 || to >= count)
            throw new ArgumentOutOfRangeException(nameof(to));
        if (from == to)
            return null;

        var order = new int[count];
        for (int i = 0; i < count; i++)
            order[i] = i;
        if (from < to)
            for (int i = from; i < to; i++)
                order[i] = i + 1;
        else
            for (int i = to + 1; i <= from; i++)
                order[i] = i - 1;
        order[to] = from;
        return order;
    }

    private static bool IsIdentity(int[] order)
    {
        for (int i = 0; i < order.Length; i++)
            if (order[i] != i)
                return false;
        return true;
    }
}
