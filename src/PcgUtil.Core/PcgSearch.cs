namespace PcgUtil.Core;

/// <summary>What a <see cref="SearchHit"/> refers to.</summary>
public enum SearchHitKind
{
    Program,
    Combi,
    SetListSlot,
    DrumKit,
    WaveSequence,
}

/// <summary>A name match, with a human-readable location (bank label + number, or set-list/slot).</summary>
public sealed record SearchHit(SearchHitKind Kind, string Name, string Location);

/// <summary>
/// Case-insensitive substring search over every name in a PCG — program and combi names, and
/// Set List slot names. Read-only.
/// </summary>
public static class PcgSearch
{
    public static IReadOnlyList<SearchHit> Find(PcgCatalog catalog, IReadOnlyList<SetList> setLists, string query)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(setLists);
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchHit>();

        var q = query.Trim();
        var hits = new List<SearchHit>();

        for (int b = 0; b < catalog.ProgramBanks.Count; b++)
        {
            var bank = catalog.ProgramBanks[b];
            for (int i = 0; i < bank.Count; i++)
                if (Matches(bank[i], q))
                    hits.Add(new SearchHit(SearchHitKind.Program, bank[i], $"{PcgBankLabels.Program(b)} #{i:D3}"));
        }

        for (int b = 0; b < catalog.CombiBanks.Count; b++)
        {
            var bank = catalog.CombiBanks[b];
            for (int i = 0; i < bank.Count; i++)
                if (Matches(bank[i], q))
                    hits.Add(new SearchHit(SearchHitKind.Combi, bank[i], $"{PcgBankLabels.Combi(b)} #{i:D3}"));
        }

        foreach (var setList in setLists)
            foreach (var slot in setList.Slots)
                if (!slot.IsEmpty && Matches(slot.Name, q))
                    hits.Add(new SearchHit(
                        SearchHitKind.SetListSlot, slot.Name, $"Set List {setList.Index:D3} slot {slot.Index:D3}"));

        for (int b = 0; b < catalog.DrumKitBanks.Count; b++)
        {
            var bank = catalog.DrumKitBanks[b];
            for (int i = 0; i < bank.Count; i++)
                if (Matches(bank[i], q))
                    hits.Add(new SearchHit(SearchHitKind.DrumKit, bank[i], $"Drum kit bank {b:D2} #{i:D3}"));
        }

        for (int b = 0; b < catalog.WaveSequenceBanks.Count; b++)
        {
            var bank = catalog.WaveSequenceBanks[b];
            for (int i = 0; i < bank.Count; i++)
                if (Matches(bank[i], q))
                    hits.Add(new SearchHit(SearchHitKind.WaveSequence, bank[i], $"Wave seq bank {b:D2} #{i:D3}"));
        }

        return hits;
    }

    private static bool Matches(string name, string query) =>
        !string.IsNullOrEmpty(name) && name.Contains(query, StringComparison.OrdinalIgnoreCase);
}
