namespace PcgUtil.Core;

/// <summary>Why a deep copy leaves a timbre's program reference untouched.</summary>
public enum DeepCopySkipReason
{
    /// <summary>Status Off/Ext/Ex2 — the timbre plays no internal program.</summary>
    ExternalStatus,
    /// <summary>The (bank, number) resolves to no program in the source file (e.g. a
    /// vendor pack's filler timbres pointing at banks the file doesn't carry).</summary>
    Unresolved,
    /// <summary>Resolves to an Init/empty placeholder program — nothing worth carrying.</summary>
    Placeholder,
}

public enum DeepCopyProgramAction
{
    /// <summary>Copy the source program record into a free destination slot.</summary>
    Copy,
    /// <summary>Point at a byte-identical program already in the destination.</summary>
    Reuse,
}

/// <summary>
/// One distinct program a deep copy maps. Banks are program-bank <em>list indices</em>;
/// <see cref="Timbres"/> are the source combi's timbre indices re-pointed to the
/// destination address. <see cref="Type"/> is the program's engine type (from its source
/// bank) — records only ever move between banks of the same type. Copies that couldn't be
/// placed (not enough free slots) carry −1/−1 and put the plan in its error state.
/// </summary>
public sealed record DeepCopyProgramMapping(
    DeepCopyProgramAction Action,
    ProgramBankType Type,
    int SourceBank, int SourceIndex,
    int DestinationBank, int DestinationIndex,
    string Name,
    IReadOnlyList<int> Timbres);

/// <summary>A timbre the deep copy leaves byte-for-byte as it came from the source.</summary>
public sealed record DeepCopyTimbreSkip(
    int Timbre, int ProgramBankPcgId, int ProgramNumber, DeepCopySkipReason Reason);

/// <summary>
/// What a deep combi copy will land where — pure data, computed by
/// <see cref="PcgDeepCopy.Plan"/> and applied by <see cref="PcgEditor.CopyCombiDeepAcross"/>.
/// The mapped timbres and the skips partition the combi's 16 timbres exactly.
/// </summary>
public sealed class DeepCopyPlan
{
    /// <summary>Source combi coordinates (combi-bank list index + slot).</summary>
    public required int SourceBank { get; init; }
    public required int SourceIndex { get; init; }
    public required string CombiName { get; init; }

    /// <summary>Destination program bank for newly copied HD-1 programs (null = none chosen).</summary>
    public required int? Hd1ProgramBank { get; init; }

    /// <summary>Destination program bank for newly copied EXi programs (null = none chosen).</summary>
    public required int? ExiProgramBank { get; init; }

    public required IReadOnlyList<DeepCopyProgramMapping> Programs { get; init; }
    public required IReadOnlyList<DeepCopyTimbreSkip> Skips { get; init; }

    /// <summary>Any KARMA module selects a user GE — those live in the pack's .KGE file,
    /// which a .PCG copy can't carry (non-blocking, informational).</summary>
    public required bool UsesUserKarmaGes { get; init; }

    /// <summary>Non-null when the plan can't be applied (e.g. not enough free slots).</summary>
    public required string? Error { get; init; }

    public bool CanApply => Error is null;
    public IEnumerable<DeepCopyProgramMapping> Copies =>
        Programs.Where(p => p.Action == DeepCopyProgramAction.Copy);
    public IEnumerable<DeepCopyProgramMapping> Reused =>
        Programs.Where(p => p.Action == DeepCopyProgramAction.Reuse);

    /// <summary>True when the combi references at least one program of the given type.</summary>
    public bool Needs(ProgramBankType type) => Programs.Any(p => p.Type == type);
}

/// <summary>
/// Plans a cross-file "deep" combi copy: the combi plus the programs its timbres
/// reference, so the copy keeps its sound instead of playing whatever the destination
/// happens to hold at the source's program addresses. Programs already present in the
/// destination (byte-identical records in same-type banks) are reused rather than
/// duplicated. HD-1 and EXi programs are allocated to separate destination banks —
/// the hardware refuses a file whose program records sit in a bank of the other engine
/// type (see <see cref="ProgramBankType"/>).
/// </summary>
public static class PcgDeepCopy
{
    public static DeepCopyPlan Plan(PcgFile source, int srcBank, int srcIndex,
                                    PcgFile destination, int? hd1ProgramBank, int? exiProgramBank)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var combi = CombiReader.Read(source).FirstOrDefault(c => c.Bank == srcBank && c.Index == srcIndex)
            ?? throw new InvalidOperationException($"The source file has no combi at bank {srcBank} #{srcIndex}.");

        var srcCatalog = PcgCatalog.Build(source);
        var dstCatalog = PcgCatalog.Build(destination);
        ValidateChosenBank(dstCatalog, hd1ProgramBank, ProgramBankType.Hd1);
        ValidateChosenBank(dstCatalog, exiProgramBank, ProgramBankType.Exi);

        // Classify the 16 timbres into skips and distinct program dependencies.
        var skips = new List<DeepCopyTimbreSkip>();
        var dependencies = new List<(int PcgId, int Number, string Name, List<int> Timbres)>();
        foreach (var t in combi.Timbres)
        {
            if (!t.UsesInternalProgram)
            {
                skips.Add(new DeepCopyTimbreSkip(t.Index, t.ProgramBankPcgId, t.ProgramNumber, DeepCopySkipReason.ExternalStatus));
                continue;
            }
            var name = srcCatalog.ResolveProgram(t.ProgramBankPcgId, t.ProgramNumber);
            if (name is null)
            {
                skips.Add(new DeepCopyTimbreSkip(t.Index, t.ProgramBankPcgId, t.ProgramNumber, DeepCopySkipReason.Unresolved));
                continue;
            }
            if (PcgOrganizer.IsProgramPlaceholder(name))
            {
                skips.Add(new DeepCopyTimbreSkip(t.Index, t.ProgramBankPcgId, t.ProgramNumber, DeepCopySkipReason.Placeholder));
                continue;
            }
            var existing = dependencies.FirstOrDefault(d => d.PcgId == t.ProgramBankPcgId && d.Number == t.ProgramNumber);
            if (existing.Timbres is null)
                dependencies.Add((t.ProgramBankPcgId, t.ProgramNumber, name, new List<int> { t.Index }));
            else
                existing.Timbres.Add(t.Index);
        }

        // Map each dependency: reuse a byte-identical same-type destination program, else
        // queue a copy in its type's pool.
        var errors = new List<string>();
        var mappings = new List<DeepCopyProgramMapping>();
        var pendingByType = new Dictionary<ProgramBankType, List<DeepCopyProgramMapping>>
        {
            [ProgramBankType.Hd1] = new(),
            [ProgramBankType.Exi] = new(),
        };
        foreach (var dep in dependencies)
        {
            int sourceBank = PcgCatalog.ProgramBankIndexForPcgId(dep.PcgId);
            var type = PcgBankIdentity.ProgramBankType(source, sourceBank);
            if (type is null)
            {
                errors.Add($"Couldn't determine the engine type of source bank {PcgBankLabels.Program(sourceBank)}.");
                continue;
            }
            var mapping = FindIdenticalProgram(source, sourceBank, dep.Number, destination, dstCatalog, type.Value) is { } hit
                ? new DeepCopyProgramMapping(DeepCopyProgramAction.Reuse, type.Value, sourceBank, dep.Number, hit.Bank, hit.Index, dep.Name, dep.Timbres)
                : new DeepCopyProgramMapping(DeepCopyProgramAction.Copy, type.Value, sourceBank, dep.Number, -1, -1, dep.Name, dep.Timbres);
            mappings.Add(mapping);
            if (mapping.Action == DeepCopyProgramAction.Copy)
                pendingByType[type.Value].Add(mapping);
        }

        // Assign free destination slots per engine type, in ascending slot order.
        foreach (var (type, pending) in pendingByType)
        {
            if (pending.Count == 0)
                continue;
            int? bank = type == ProgramBankType.Hd1 ? hd1ProgramBank : exiProgramBank;
            if (bank is null)
            {
                errors.Add($"This combi needs {pending.Count} {PcgBankIdentity.TypeLabel(type)} program slot(s) — " +
                           $"pick {PcgBankIdentity.TypeLabelWithArticle(type)} destination bank.");
                continue;
            }
            var freeSlots = FreeProgramSlots(dstCatalog.ProgramBanks[bank.Value]);
            if (pending.Count > freeSlots.Count)
                errors.Add($"Needs {pending.Count} free slot(s) in {PcgBankLabels.Program(bank.Value)}, " +
                           $"but only {freeSlots.Count} are free — pick another {PcgBankIdentity.TypeLabel(type)} bank.");
            // (bank type already validated against the role in ValidateChosenBank)
            for (int i = 0; i < pending.Count && i < freeSlots.Count; i++)
            {
                int at = mappings.IndexOf(pending[i]);
                mappings[at] = pending[i] with { DestinationBank = bank.Value, DestinationIndex = freeSlots[i] };
            }
        }

        return new DeepCopyPlan
        {
            SourceBank = srcBank,
            SourceIndex = srcIndex,
            CombiName = combi.Name,
            Hd1ProgramBank = hd1ProgramBank,
            ExiProgramBank = exiProgramBank,
            Programs = mappings,
            Skips = skips,
            UsesUserKarmaGes = combi.UsesUserKarmaGes,
            Error = errors.Count > 0 ? string.Join(" ", errors) : null,
        };
    }

    /// <summary>Indices of a program bank's free (placeholder) slots, ascending.</summary>
    public static IReadOnlyList<int> FreeProgramSlots(IReadOnlyList<string> bankNames)
    {
        ArgumentNullException.ThrowIfNull(bankNames);
        var free = new List<int>();
        for (int i = 0; i < bankNames.Count; i++)
            if (PcgOrganizer.IsProgramPlaceholder(bankNames[i]))
                free.Add(i);
        return free;
    }

    /// <summary>Indices of a combi bank's free (Init/empty) slots, ascending.</summary>
    public static IReadOnlyList<int> FreeCombiSlots(IReadOnlyList<string> bankNames)
    {
        ArgumentNullException.ThrowIfNull(bankNames);
        var free = new List<int>();
        for (int i = 0; i < bankNames.Count; i++)
            if (Combi.IsEmptyOrInitName(bankNames[i]))
                free.Add(i);
        return free;
    }

    private static void ValidateChosenBank(PcgCatalog dstCatalog, int? bank, ProgramBankType role)
    {
        if (bank is null)
            return;
        if (bank < 0 || bank >= dstCatalog.ProgramBanks.Count || dstCatalog.ProgramBanks[bank.Value].Count == 0)
            throw new InvalidOperationException(
                $"The destination file does not contain program bank {PcgBankLabels.Program(bank.Value)}.");
        if (dstCatalog.ProgramBankTypes[bank.Value] != role)
            throw new InvalidOperationException(
                $"{PcgBankLabels.Program(bank.Value)} is not {PcgBankIdentity.TypeLabelWithArticle(role)} bank.");
    }

    // First destination program record byte-identical to the source record, or null. Only
    // banks of the program's own engine type are searched — identical bytes in a bank of
    // the other type would be reinterpreted by the other engine. Placeholder-named records
    // are ignored (an identical copy of a real program can't be a placeholder, but the
    // guard is cheap and self-documenting).
    private static (int Bank, int Index)? FindIdenticalProgram(
        PcgFile source, int sourceBank, int sourceIndex, PcgFile destination, PcgCatalog dstCatalog,
        ProgramBankType type)
    {
        var (srcStart, srcSize, srcCount) = PcgEditor.LocateBank(source, "PRG1", sourceBank);
        if (sourceIndex < 0 || sourceIndex >= srcCount)
            throw new InvalidOperationException($"Source program {PcgBankLabels.Program(sourceBank)} #{sourceIndex} is out of range.");
        var srcSpan = source.Data.AsSpan((int)(srcStart + (long)sourceIndex * srcSize), srcSize);

        for (int bank = 0; bank < dstCatalog.ProgramBanks.Count; bank++)
        {
            var names = dstCatalog.ProgramBanks[bank];
            if (names.Count == 0 || dstCatalog.ProgramBankTypes[bank] != type)
                continue;
            var (dstStart, dstSize, dstCount) = PcgEditor.LocateBank(destination, "PRG1", bank);
            if (dstSize != srcSize)
                continue;
            for (int i = 0; i < dstCount && i < names.Count; i++)
            {
                if (PcgOrganizer.IsProgramPlaceholder(names[i]))
                    continue;
                if (srcSpan.SequenceEqual(destination.Data.AsSpan((int)(dstStart + (long)i * dstSize), dstSize)))
                    return (bank, i);
            }
        }
        return null;
    }
}
