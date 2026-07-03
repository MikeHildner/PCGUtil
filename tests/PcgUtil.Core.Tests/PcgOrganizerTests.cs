using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgOrganizerTests
{
    [Fact]
    public void SortProgramBankByName_orders_names_and_keeps_every_reference_resolving_the_same()
    {
        var pcg = Sample.Parse();
        var beforeNames = PcgCatalog.Build(pcg).ProgramBanks[0].ToList();
        var before = ResolveAllProgramRefs(pcg);

        var edited = PcgOrganizer.SortProgramBankByName(pcg, 0);
        Assert.NotNull(edited); // factory bank 0 is not alphabetical

        var pcg2 = PcgReader.Parse(edited!);
        var names2 = PcgCatalog.Build(pcg2).ProgramBanks[0];

        // Real records form an A→Z prefix; placeholders (if any) form the tail.
        int tail = names2.Count;
        while (tail > 0 && PcgOrganizer.IsProgramPlaceholder(names2[tail - 1]))
            tail--;
        for (int i = 0; i < tail; i++)
            Assert.False(PcgOrganizer.IsProgramPlaceholder(names2[i]), $"placeholder '{names2[i]}' before the tail");
        for (int i = 1; i < tail; i++)
            Assert.True(
                string.Compare(names2[i - 1], names2[i], StringComparison.OrdinalIgnoreCase) <= 0,
                $"'{names2[i - 1]}' should sort before '{names2[i]}'");

        // Same records, just rearranged.
        Assert.Equal(beforeNames.OrderBy(n => n), names2.OrderBy(n => n));

        // Integrity: every combi timbre and program-type slot still resolves to the same program.
        var after = ResolveAllProgramRefs(pcg2);
        Assert.Equal(before.Count, after.Count);
        foreach (var (key, value) in before)
            Assert.Equal(value, after[key]);
    }

    [Fact]
    public void SortProgramBankByName_returns_null_when_already_sorted()
    {
        var sorted = PcgOrganizer.SortProgramBankByName(Sample.Parse(), 0);
        Assert.NotNull(sorted);
        Assert.Null(PcgOrganizer.SortProgramBankByName(PcgReader.Parse(sorted!), 0));
    }

    [Fact]
    public void CompactProgramBank_moves_placeholders_to_the_end_and_references_follow()
    {
        // Turn a real, referenced program (USER-C #1) into an interior placeholder with a
        // unique name, so we can watch both the record and its references travel to the tail.
        var pcg = PcgReader.Parse(PcgEditor.RenameProgram(Sample.Parse(), 8, 1, "Init Program Zz9"));
        var prepNames = PcgCatalog.Build(pcg).ProgramBanks[8].ToList();
        var before = ResolveAllProgramRefs(pcg);

        var edited = PcgOrganizer.CompactProgramBank(pcg, 8);
        Assert.NotNull(edited);

        var pcg2 = PcgReader.Parse(edited!);
        var names2 = PcgCatalog.Build(pcg2).ProgramBanks[8];

        // Real records keep their original relative order, placeholders follow.
        var expectedPrefix = prepNames.Where(n => !PcgOrganizer.IsProgramPlaceholder(n)).ToList();
        Assert.Equal(expectedPrefix, names2.Take(expectedPrefix.Count));
        for (int i = expectedPrefix.Count; i < names2.Count; i++)
            Assert.True(PcgOrganizer.IsProgramPlaceholder(names2[i]), $"real record '{names2[i]}' in the tail");

        var after = ResolveAllProgramRefs(pcg2);
        Assert.Equal(before.Count, after.Count);
        foreach (var (key, value) in before)
            Assert.Equal(value, after[key]);
    }

    [Fact]
    public void SortCombiBankByName_orders_names_and_keeps_set_list_slots_resolving_the_same()
    {
        var pcg = Sample.Parse();
        var before = ResolveAllCombiRefs(pcg);

        var edited = PcgOrganizer.SortCombiBankByName(pcg, 7); // USER-A: song combis, not alphabetical
        Assert.NotNull(edited);

        var pcg2 = PcgReader.Parse(edited!);
        var names2 = PcgCatalog.Build(pcg2).CombiBanks[7];

        int tail = names2.Count;
        while (tail > 0 && Combi.IsEmptyOrInitName(names2[tail - 1]))
            tail--;
        for (int i = 1; i < tail; i++)
            Assert.True(
                string.Compare(names2[i - 1], names2[i], StringComparison.OrdinalIgnoreCase) <= 0,
                $"'{names2[i - 1]}' should sort before '{names2[i]}'");

        var after = ResolveAllCombiRefs(pcg2);
        Assert.Equal(before.Count, after.Count);
        foreach (var (key, value) in before)
            Assert.Equal(value, after[key]);
    }

    [Fact]
    public void CompactCombiBank_sends_a_referenced_init_combi_to_the_tail_with_its_slot_following()
    {
        // "Let's Go Crazy" (USER-A #57) is loaded by set list 0 slot 1. Renamed to an init
        // placeholder it must compact to the tail, and the slot reference must follow it.
        var pcg = PcgReader.Parse(PcgEditor.RenameCombi(Sample.Parse(), 7, 57, "Init Combi Zz9"));
        var prepNames = PcgCatalog.Build(pcg).CombiBanks[7].ToList();

        var edited = PcgOrganizer.CompactCombiBank(pcg, 7);
        Assert.NotNull(edited);

        var pcg2 = PcgReader.Parse(edited!);
        var catalog2 = PcgCatalog.Build(pcg2);
        var names2 = catalog2.CombiBanks[7];

        var expectedPrefix = prepNames.Where(n => !Combi.IsEmptyOrInitName(n)).ToList();
        Assert.Equal(expectedPrefix, names2.Take(expectedPrefix.Count));

        var slot = SetListReader.Read(pcg2)[0].Slots[1];
        Assert.Equal(PcgItemKind.Combi, slot.Reference.Kind);
        Assert.Equal("Init Combi Zz9", catalog2.Resolve(slot.Reference));
        Assert.True(slot.Reference.Index >= expectedPrefix.Count, "the placeholder should live in the tail");
    }

    [Fact]
    public void ReorderPrograms_rejects_non_permutations()
    {
        var pcg = Sample.Parse();
        int count = PcgCatalog.Build(pcg).ProgramBanks[0].Count;

        var duplicate = Enumerable.Range(0, count).ToArray();
        duplicate[1] = 0; // 0 appears twice
        Assert.Throws<ArgumentException>(() => PcgEditor.ReorderPrograms(pcg, 0, duplicate));
        Assert.Throws<ArgumentException>(() => PcgEditor.ReorderPrograms(pcg, 0, new[] { 0, 1, 2 }));
    }

    [Fact]
    public void ReorderPrograms_keeps_every_leaf_checksum_valid()
    {
        var pcg = Sample.Parse();
        var edited = PcgOrganizer.SortProgramBankByName(pcg, 0)!;

        foreach (var chunk in pcg.EnumerateChunks())
        {
            if (chunk.HasChildren || chunk.DataOffset < 1 || chunk.Size <= 0) continue;
            if (chunk.DataEnd > pcg.Data.Length) continue;

            // Only leaves that used the checksum convention in the original (see PcgChecksum).
            byte stored = pcg.Data[chunk.DataOffset - 1];
            if (stored != PcgChecksum.Sum(pcg.Data, chunk.DataOffset, chunk.Size)) continue;

            Assert.Equal(PcgChecksum.Sum(edited, chunk.DataOffset, chunk.Size), edited[chunk.DataOffset - 1]);
        }
    }

    // Resolves every program reference (combi timbres + program-type set-list slots) to a name.
    private static Dictionary<string, string> ResolveAllProgramRefs(PcgFile pcg)
    {
        var catalog = PcgCatalog.Build(pcg);
        var map = new Dictionary<string, string>();

        foreach (var combi in CombiReader.Read(pcg))
            foreach (var t in combi.Timbres)
                map[$"T:{combi.Bank}:{combi.Index}:{t.Index}"] =
                    catalog.ResolveProgram(t.ProgramBankPcgId, t.ProgramNumber) ?? "(none)";

        foreach (var setList in SetListReader.Read(pcg))
            foreach (var slot in setList.Slots)
                if (slot.Reference.Kind == PcgItemKind.Program)
                    map[$"S:{setList.Index}:{slot.Index}"] =
                        catalog.ResolveProgram(slot.Reference.Bank, slot.Reference.Index) ?? "(none)";

        return map;
    }

    // Resolves every combi-type set-list slot to a combi name.
    private static Dictionary<string, string> ResolveAllCombiRefs(PcgFile pcg)
    {
        var catalog = PcgCatalog.Build(pcg);
        var map = new Dictionary<string, string>();

        foreach (var setList in SetListReader.Read(pcg))
            foreach (var slot in setList.Slots)
                if (slot.Reference.Kind == PcgItemKind.Combi)
                    map[$"S:{setList.Index}:{slot.Index}"] =
                        catalog.Resolve(slot.Reference) ?? "(none)";

        return map;
    }
}
