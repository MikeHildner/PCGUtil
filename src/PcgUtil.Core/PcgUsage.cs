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
}
