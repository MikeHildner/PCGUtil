using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgEditorCombiTests
{
    [Fact]
    public void SwapCombis_swaps_records_and_keeps_every_slot_resolving_to_the_same_sound()
    {
        var pcg = Sample.Parse();
        var before = ResolveAllNamedSlots(pcg);

        // Swap two combis used by Set List 0: Let's Go Crazy (7/57) and Freeze Frame (7/58).
        var edited = PcgEditor.SwapCombis(pcg, 7, 57, 7, 58);
        var pcg2 = PcgReader.Parse(edited);

        // The two combi records actually swapped names.
        var catalog2 = PcgCatalog.Build(pcg2);
        Assert.Equal("Freeze Frame", catalog2.CombiBanks[7][57]);
        Assert.Equal("Let's Go Crazy", catalog2.CombiBanks[7][58]);

        // The slot references were retargeted to follow each combi.
        var setLists2 = SetListReader.Read(pcg2);
        Assert.Equal(58, setLists2[0].Slots[1].Reference.Index);
        Assert.Equal(57, setLists2[0].Slots[2].Reference.Index);

        // Integrity: every named slot still loads the same combi/program as before.
        var after = ResolveAllNamedSlots(pcg2);
        Assert.Equal(before.Count, after.Count);
        foreach (var (key, value) in before)
            Assert.Equal(value, after[key]);
    }

    [Fact]
    public void CopyCombi_overwrites_destination_and_leaves_source()
    {
        var pcg = Sample.Parse();
        var edited = PcgEditor.CopyCombi(pcg, srcBank: 7, srcIndex: 57, dstBank: 0, dstIndex: 0);

        var catalog = PcgCatalog.Build(PcgReader.Parse(edited));
        Assert.Equal("Let's Go Crazy", catalog.CombiBanks[0][0]); // destination overwritten
        Assert.Equal("Let's Go Crazy", catalog.CombiBanks[7][57]); // source unchanged
    }

    [Fact]
    public void RenameCombi_writes_the_name()
    {
        var pcg = Sample.Parse();
        var edited = PcgEditor.RenameCombi(pcg, 7, 57, "My Combi");

        var catalog = PcgCatalog.Build(PcgReader.Parse(edited));
        Assert.Equal("My Combi", catalog.CombiBanks[7][57]);
    }

    [Fact]
    public void SwapCombis_rejects_out_of_range()
    {
        var pcg = Sample.Parse();
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SwapCombis(pcg, 7, 57, 99, 0));
    }

    [Fact]
    public void SwapCombis_does_not_disturb_song_type_slots()
    {
        var pcg = Sample.Parse();

        // The Song slot at Set List 15 / slot 31 is stored as bank 0, index 0 — the same
        // (bank, index) as the first INT-A combi. Swapping that combi must NOT rewrite the
        // song slot's reference; only true Combi-type slots are retargeted.
        var before = SetListReader.Read(pcg)[15].Slots[31].Reference;
        Assert.Equal(PcgItemKind.Song, before.Kind);

        var edited = PcgEditor.SwapCombis(pcg, 0, 0, 0, 1);
        var after = SetListReader.Read(PcgReader.Parse(edited))[15].Slots[31].Reference;

        Assert.Equal(PcgItemKind.Song, after.Kind);
        Assert.Equal(before.Bank, after.Bank);
        Assert.Equal(before.Index, after.Index);
        Assert.Equal(before.Raw, after.Raw); // byte-identical reference
    }

    private static Dictionary<string, string> ResolveAllNamedSlots(PcgFile pcg)
    {
        var catalog = PcgCatalog.Build(pcg);
        var map = new Dictionary<string, string>();
        foreach (var setList in SetListReader.Read(pcg))
            foreach (var slot in setList.NamedSlots)
                map[$"{setList.Index}:{slot.Index}"] = catalog.Resolve(slot.Reference) ?? "(unresolved)";
        return map;
    }
}
