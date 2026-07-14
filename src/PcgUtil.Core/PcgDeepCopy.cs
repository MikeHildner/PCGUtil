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
/// destination address. Copies that couldn't be placed (not enough free slots) carry
/// −1/−1 and put the plan in its error state.
/// </summary>
public sealed record DeepCopyProgramMapping(
    DeepCopyProgramAction Action,
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

    /// <summary>Destination program bank chosen for newly copied programs.</summary>
    public required int ProgramBank { get; init; }

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
}

/// <summary>
/// Plans a cross-file "deep" combi copy: the combi plus the programs its timbres
/// reference, so the copy keeps its sound instead of playing whatever the destination
/// happens to hold at the source's program addresses. Programs already present in the
/// destination (byte-identical records) are reused rather than duplicated, so
/// transplanting several combis from one pack shares their programs.
/// </summary>
public static class PcgDeepCopy
{
    public static DeepCopyPlan Plan(PcgFile source, int srcBank, int srcIndex,
                                    PcgFile destination, int destinationProgramBank)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var combi = CombiReader.Read(source).FirstOrDefault(c => c.Bank == srcBank && c.Index == srcIndex)
            ?? throw new InvalidOperationException($"The source file has no combi at bank {srcBank} #{srcIndex}.");

        var srcCatalog = PcgCatalog.Build(source);
        var dstCatalog = PcgCatalog.Build(destination);
        if (destinationProgramBank < 0 || destinationProgramBank >= dstCatalog.ProgramBanks.Count
            || dstCatalog.ProgramBanks[destinationProgramBank].Count == 0)
            throw new InvalidOperationException(
                $"The destination file does not contain program bank {PcgBankLabels.Program(destinationProgramBank)}.");

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

        // Map each dependency: reuse a byte-identical destination program, else queue a copy.
        var mappings = new List<DeepCopyProgramMapping>();
        var pendingCopies = new List<DeepCopyProgramMapping>();
        foreach (var dep in dependencies)
        {
            int sourceBank = PcgCatalog.ProgramBankIndexForPcgId(dep.PcgId);
            var mapping = FindIdenticalProgram(source, sourceBank, dep.Number, destination, dstCatalog) is { } hit
                ? new DeepCopyProgramMapping(DeepCopyProgramAction.Reuse, sourceBank, dep.Number, hit.Bank, hit.Index, dep.Name, dep.Timbres)
                : new DeepCopyProgramMapping(DeepCopyProgramAction.Copy, sourceBank, dep.Number, -1, -1, dep.Name, dep.Timbres);
            mappings.Add(mapping);
            if (mapping.Action == DeepCopyProgramAction.Copy)
                pendingCopies.Add(mapping);
        }

        // Assign free destination slots to the copies, in ascending slot order.
        string? error = null;
        var freeSlots = FreeProgramSlots(dstCatalog.ProgramBanks[destinationProgramBank]);
        if (pendingCopies.Count > freeSlots.Count)
            error = $"Needs {pendingCopies.Count} free slot(s) in {PcgBankLabels.Program(destinationProgramBank)}, " +
                    $"but only {freeSlots.Count} are free — pick another destination bank.";
        for (int i = 0; i < pendingCopies.Count && i < freeSlots.Count; i++)
        {
            int at = mappings.IndexOf(pendingCopies[i]);
            mappings[at] = pendingCopies[i] with { DestinationBank = destinationProgramBank, DestinationIndex = freeSlots[i] };
        }

        return new DeepCopyPlan
        {
            SourceBank = srcBank,
            SourceIndex = srcIndex,
            CombiName = combi.Name,
            ProgramBank = destinationProgramBank,
            Programs = mappings,
            Skips = skips,
            UsesUserKarmaGes = combi.UsesUserKarmaGes,
            Error = error,
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

    // First destination program record byte-identical to the source record, or null.
    // Placeholder-named records are ignored (an identical copy of a real program can't be
    // a placeholder, but the guard is cheap and self-documenting).
    private static (int Bank, int Index)? FindIdenticalProgram(
        PcgFile source, int sourceBank, int sourceIndex, PcgFile destination, PcgCatalog dstCatalog)
    {
        var (srcStart, srcSize, srcCount) = PcgEditor.LocateBank(source, "PRG1", sourceBank);
        if (sourceIndex < 0 || sourceIndex >= srcCount)
            throw new InvalidOperationException($"Source program {PcgBankLabels.Program(sourceBank)} #{sourceIndex} is out of range.");
        var srcSpan = source.Data.AsSpan((int)(srcStart + (long)sourceIndex * srcSize), srcSize);

        for (int bank = 0; bank < dstCatalog.ProgramBanks.Count; bank++)
        {
            var names = dstCatalog.ProgramBanks[bank];
            if (names.Count == 0)
                continue; // bank not carried by the destination
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
