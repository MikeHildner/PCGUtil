namespace PcgUtil.Core;

/// <summary>
/// Cross-references which Programs/Combis are used by Set List slots, so the UI can
/// show usage counts and so reorganizing patches stays safe.
/// </summary>
public static class PcgUsage
{
    /// <summary>
    /// Counts how many Set List slots reference each combi, keyed by (bank, index).
    /// </summary>
    public static IReadOnlyDictionary<(int Bank, int Index), int> CombiReferenceCounts(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        return CombiReferenceCounts(SetListReader.Read(pcg));
    }

    public static IReadOnlyDictionary<(int Bank, int Index), int> CombiReferenceCounts(IReadOnlyList<SetList> setLists)
    {
        var counts = new Dictionary<(int, int), int>();
        foreach (var setList in setLists)
            foreach (var slot in setList.Slots)
            {
                if (slot.Reference.Kind != PcgItemKind.Combi)
                    continue;
                var key = (slot.Reference.Bank, slot.Reference.Index);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        return counts;
    }

    /// <summary>
    /// Counts references to each Program — combi timbres that play an internal program plus
    /// program-type set-list slots — keyed by (program-bank list index, number).
    /// </summary>
    public static IReadOnlyDictionary<(int Bank, int Index), int> ProgramReferenceCounts(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        return BuildUsageReport(pcg).Programs.ToDictionary(p => (p.BankIndex, p.Number), p => p.ReferenceCount);
    }

    /// <summary>
    /// Cross-references how every Program is used, and lists named patches with no in-file
    /// references. "Unreferenced" means nothing in this file points at the patch — it may still
    /// be played directly on the hardware, and Song-type references are not decoded yet.
    /// </summary>
    public static UsageReport BuildUsageReport(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var catalog = PcgCatalog.Build(pcg);
        var setLists = SetListReader.Read(pcg);
        var combis = CombiReader.Read(pcg);

        var sites = new Dictionary<(int Bank, int Index), List<UsageSite>>();
        void AddSite(int bankIndex, int number, UsageSite site)
        {
            if (bankIndex < 0) return; // PcgId with no in-file bank (GM / virtual)
            if (!sites.TryGetValue((bankIndex, number), out var list))
                sites[(bankIndex, number)] = list = new List<UsageSite>();
            list.Add(site);
        }

        foreach (var combi in combis)
        {
            if (combi.IsEmptyOrInit) continue;
            foreach (var timbre in combi.Timbres)
            {
                if (!timbre.UsesInternalProgram) continue;
                AddSite(
                    PcgCatalog.ProgramBankIndexForPcgId(timbre.ProgramBankPcgId), timbre.ProgramNumber,
                    new UsageSite(UsageSiteKind.CombiTimbre,
                        $"Combi bank {combi.Bank:D2} #{combi.Index:D3} '{combi.Name}' timbre {timbre.Index + 1}"));
            }
        }

        foreach (var setList in setLists)
            foreach (var slot in setList.Slots)
            {
                if (slot.IsEmpty || slot.Reference.Kind != PcgItemKind.Program) continue;
                AddSite(
                    PcgCatalog.ProgramBankIndexForPcgId(slot.Reference.Bank), slot.Reference.Index,
                    new UsageSite(UsageSiteKind.SetListSlot,
                        $"Set List {setList.Index:D3} slot {slot.Index:D3} '{slot.Name}'"));
            }

        var programs = sites
            .Select(entry => new ProgramUsage
            {
                BankIndex = entry.Key.Bank,
                Number = entry.Key.Index,
                Name = NameAt(catalog.ProgramBanks, entry.Key.Bank, entry.Key.Index),
                Sites = entry.Value,
            })
            .OrderByDescending(p => p.ReferenceCount)
            .ThenBy(p => p.BankIndex).ThenBy(p => p.Number)
            .ToList();

        var referenced = sites.Keys.ToHashSet();
        var unreferencedPrograms = EnumerateNamed(catalog.ProgramBanks)
            .Where(p => !referenced.Contains((p.Bank, p.Number)))
            .Select(p => new UnreferencedPatch(p.Bank, p.Number, p.Name))
            .ToList();

        var combiCounts = CombiReferenceCounts(setLists);
        var unreferencedCombis = combis
            .Where(c => !c.IsEmptyOrInit && combiCounts.GetValueOrDefault((c.Bank, c.Index)) == 0)
            .Select(c => new UnreferencedPatch(c.Bank, c.Index, c.Name))
            .ToList();

        return new UsageReport
        {
            Programs = programs,
            UnreferencedPrograms = unreferencedPrograms,
            UnreferencedCombis = unreferencedCombis,
        };
    }

    private static string NameAt(IReadOnlyList<IReadOnlyList<string>> banks, int bank, int number) =>
        bank >= 0 && bank < banks.Count && number >= 0 && number < banks[bank].Count
            ? banks[bank][number]
            : string.Empty;

    private static IEnumerable<(int Bank, int Number, string Name)> EnumerateNamed(
        IReadOnlyList<IReadOnlyList<string>> banks)
    {
        for (int b = 0; b < banks.Count; b++)
            for (int i = 0; i < banks[b].Count; i++)
                if (!string.IsNullOrEmpty(banks[b][i]))
                    yield return (b, i, banks[b][i]);
    }
}
