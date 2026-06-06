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
        Assert.Contains(hits, h => h.Kind == SearchHitKind.Program && h.Location == "INT-A #000");
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
        Assert.Contains(hits, h => h.Kind == SearchHitKind.SetListSlot);
    }
}
