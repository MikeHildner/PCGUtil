using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgSearchTests
{
    private static (PcgCatalog Catalog, IReadOnlyList<SetList> SetLists) Load()
    {
        var pcg = Sample.Parse();
        return (PcgCatalog.Build(pcg), SetListReader.Read(pcg));
    }

    [Fact]
    public void Finds_a_program_by_name_with_bank_label_location()
    {
        var (catalog, setLists) = Load();
        var hits = PcgSearch.Find(catalog, setLists, "Berlin Grand");

        Assert.NotEmpty(hits);
        // INT-A #000 is "Berlin Grand SW2 U.C."
        Assert.Contains(hits, h => h.Kind == SearchHitKind.Program && h.Location == "INT-A #000"
                                   && h.Bank == 0 && h.Index == 0);
    }

    [Fact]
    public void Is_case_insensitive()
    {
        var (catalog, setLists) = Load();
        Assert.NotEmpty(PcgSearch.Find(catalog, setLists, "berlin grand"));
    }

    [Fact]
    public void Blank_query_returns_nothing()
    {
        var (catalog, setLists) = Load();
        Assert.Empty(PcgSearch.Find(catalog, setLists, "   "));
    }

    [Fact]
    public void Finds_a_set_list_slot_by_name()
    {
        var (catalog, setLists) = Load();
        // Set List 0 slot 1 is "Let's Go Crazy".
        var hits = PcgSearch.Find(catalog, setLists, "Let's Go Crazy");
        Assert.Contains(hits, h => h.Kind == SearchHitKind.SetListSlot && h.Bank == 0 && h.Index == 1);
    }

    [Fact]
    public void Hit_coordinates_point_back_at_the_matching_name()
    {
        var (catalog, setLists) = Load();
        var hits = PcgSearch.Find(catalog, setLists, "a"); // broad on purpose: hits of every kind

        Assert.NotEmpty(hits);
        foreach (var h in hits)
        {
            var actual = h.Kind switch
            {
                SearchHitKind.Program => catalog.ProgramBanks[h.Bank][h.Index],
                SearchHitKind.Combi => catalog.CombiBanks[h.Bank][h.Index],
                SearchHitKind.DrumKit => catalog.DrumKitBanks[h.Bank][h.Index],
                SearchHitKind.WaveSequence => catalog.WaveSequenceBanks[h.Bank][h.Index],
                SearchHitKind.SetListSlot => setLists.Single(s => s.Index == h.Bank)
                                                    .Slots.Single(s => s.Index == h.Index).Name,
                _ => throw new InvalidOperationException($"unexpected kind {h.Kind}"),
            };
            Assert.Equal(h.Name, actual);
        }
    }
}
