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

    [Fact]
    public void MoveSetListSlot_equals_a_chain_of_adjacent_swaps()
    {
        // Insert-shift semantics pinned against the shipped (hardware-verified) swap
        // primitive: moving 1 → 4 is exactly the swaps 1↔2, 2↔3, 3↔4 in sequence.
        var expected = Sample.Parse();
        foreach (var (a, b) in new[] { (1, 2), (2, 3), (3, 4) })
            expected = PcgReader.Parse(PcgEditor.SwapSetListSlots(expected, 0, a, b));
        var movedDown = PcgOrganizer.MoveSetListSlot(Sample.Parse(), 0, from: 1, to: 4);
        Assert.True(expected.Data.AsSpan().SequenceEqual(movedDown!));

        // And back up: 4 → 1 is the swaps 4↔3, 3↔2, 2↔1.
        expected = Sample.Parse();
        foreach (var (a, b) in new[] { (4, 3), (3, 2), (2, 1) })
            expected = PcgReader.Parse(PcgEditor.SwapSetListSlots(expected, 0, a, b));
        var movedUp = PcgOrganizer.MoveSetListSlot(Sample.Parse(), 0, from: 4, to: 1);
        Assert.True(expected.Data.AsSpan().SequenceEqual(movedUp!));
    }

    [Fact]
    public void MoveToPosition_returns_null_when_from_equals_to()
    {
        var pcg = Sample.Parse();
        Assert.Null(PcgOrganizer.MoveSetListSlot(pcg, 0, 5, 5));
        Assert.Null(PcgOrganizer.MoveCombiToPosition(pcg, 7, 5, 5));
        Assert.Null(PcgOrganizer.MoveProgramToPosition(pcg, 0, 5, 5));
    }

    [Fact]
    public void MoveToPosition_rejects_out_of_range_positions()
    {
        var pcg = Sample.Parse();
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgOrganizer.MoveCombiToPosition(pcg, 7, -1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgOrganizer.MoveCombiToPosition(pcg, 7, 5, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgOrganizer.MoveProgramToPosition(pcg, 0, 128, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgOrganizer.MoveSetListSlot(pcg, 0, 5, -1));
    }

    [Fact]
    public void MoveCombiToPosition_shifts_the_span_and_set_list_slots_follow()
    {
        var pcg = Sample.Parse();
        var beforeNames = PcgCatalog.Build(pcg).CombiBanks[7].ToList();
        var beforeRefs = ResolveAllCombiRefs(pcg);

        var edited = PcgOrganizer.MoveCombiToPosition(pcg, bank: 7, from: 57, to: 60);
        Assert.NotNull(edited);
        var after = PcgReader.Parse(edited!);

        // Order oracle: RemoveAt/Insert is the definition of insert-shift.
        var expected = beforeNames.ToList();
        var moved = expected[57];
        expected.RemoveAt(57);
        expected.Insert(60, moved);
        Assert.Equal(expected, PcgCatalog.Build(after).CombiBanks[7].ToList());

        // Every combi-type set-list slot still loads the same combi.
        var afterRefs = ResolveAllCombiRefs(after);
        Assert.Equal(beforeRefs.Count, afterRefs.Count);
        foreach (var (key, name) in beforeRefs)
            Assert.Equal(name, afterRefs[key]);
    }

    [Fact]
    public void MoveProgramToPosition_covers_both_directions_and_the_ends()
    {
        var pcg = Sample.Parse();
        int count = PcgCatalog.Build(pcg).ProgramBanks[0].Count;

        foreach (var (from, to) in new[] { (10, 20), (20, 10), (5, 0), (5, count - 1) })
        {
            var beforeNames = PcgCatalog.Build(pcg).ProgramBanks[0].ToList();
            var beforeRefs = ResolveAllProgramRefs(pcg);

            var edited = PcgOrganizer.MoveProgramToPosition(pcg, bank: 0, from, to);
            Assert.NotNull(edited);
            var after = PcgReader.Parse(edited!);

            var expected = beforeNames.ToList();
            var moved = expected[from];
            expected.RemoveAt(from);
            expected.Insert(to, moved);
            Assert.Equal(expected, PcgCatalog.Build(after).ProgramBanks[0].ToList());

            // Combi timbres and program-type slots follow the move.
            var afterRefs = ResolveAllProgramRefs(after);
            Assert.Equal(beforeRefs.Count, afterRefs.Count);
            foreach (var (key, name) in beforeRefs)
                Assert.Equal(name, afterRefs[key]);
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
