using System.Buffers.Binary;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgDiffTests
{
    [Fact]
    public void Identical_files_produce_an_empty_report()
    {
        var report = PcgDiff.Compare(Sample.Parse(), Sample.Parse());
        Assert.True(report.IsEmpty);
        Assert.Empty(report.Programs);
        Assert.Empty(report.Combis);
        Assert.Empty(report.SetListSlots);
        Assert.Empty(report.RenamedSetLists);
    }

    [Fact]
    public void A_program_rename_reports_exactly_one_renamed_entry()
    {
        var before = Sample.Parse();
        var after = PcgReader.Parse(PcgEditor.RenameProgram(Sample.Parse(), 0, 0, "Diff Renamed Zz9"));

        var report = PcgDiff.Compare(before, after);

        var entry = Assert.Single(report.Programs);
        Assert.Equal(DiffKind.Renamed, entry.Kind);
        Assert.Equal(0, entry.Bank);
        Assert.Equal(0, entry.IndexAfter);
        Assert.Equal("Berlin Grand SW2 U.C.", entry.NameBefore);
        Assert.Equal("Diff Renamed Zz9", entry.NameAfter);
        Assert.Empty(report.Combis);
        Assert.Empty(report.SetListSlots);
    }

    [Fact]
    public void Swapping_two_unreferenced_combis_reports_a_moved_pair_and_nothing_else()
    {
        var before = Sample.Parse();

        // Two unreferenced combis in the same bank: swapping them retargets nothing.
        var pair = PcgUsage.BuildUsageReport(before).UnreferencedCombis
            .GroupBy(c => c.BankIndex)
            .First(g => g.Count() >= 2)
            .Take(2).ToList();
        int bank = pair[0].BankIndex;
        var after = PcgReader.Parse(PcgEditor.SwapCombis(Sample.Parse(), bank, pair[0].Number, bank, pair[1].Number));

        var report = PcgDiff.Compare(before, after);

        Assert.Equal(2, report.Combis.Count);
        Assert.All(report.Combis, e => Assert.Equal(DiffKind.Moved, e.Kind));
        Assert.Contains(report.Combis, e => e.IndexBefore == pair[0].Number && e.IndexAfter == pair[1].Number);
        Assert.Contains(report.Combis, e => e.IndexBefore == pair[1].Number && e.IndexAfter == pair[0].Number);
        Assert.Empty(report.Programs);
        Assert.Empty(report.SetListSlots);
    }

    [Fact]
    public void Copying_onto_a_placeholder_reports_added_and_onto_a_real_patch_reports_replaced()
    {
        var before = Sample.Parse();
        var catalog = PcgCatalog.Build(before);

        // Destination 1: a factory "Init Program" placeholder in a bank of the source's
        // engine type (INT-A is EXi; program moves can't cross HD-1/EXi banks).
        var (initBank, initIndex) = FindPlaceholderProgram(catalog, ProgramBankType.Exi);
        var afterAdd = PcgReader.Parse(PcgEditor.CopyProgram(Sample.Parse(), 0, 0, initBank, initIndex));
        var added = Assert.Single(PcgDiff.Compare(before, afterAdd).Programs);
        Assert.Equal(DiffKind.Added, added.Kind);
        Assert.Equal(initBank, added.Bank);
        Assert.Equal(initIndex, added.IndexAfter);
        Assert.Equal("Berlin Grand SW2 U.C.", added.NameAfter);

        // Destination 2: a real, differently-named patch in another EXi bank.
        var (realBank, realIndex) = FindRealProgram(catalog, ProgramBankType.Exi, avoidName: "Berlin Grand SW2 U.C.");
        var afterReplace = PcgReader.Parse(PcgEditor.CopyProgram(Sample.Parse(), 0, 0, realBank, realIndex));
        var replaced = Assert.Single(PcgDiff.Compare(before, afterReplace).Programs);
        Assert.Equal(DiffKind.Replaced, replaced.Kind);
        Assert.Equal(catalog.ProgramBanks[realBank][realIndex], replaced.NameBefore);
        Assert.Equal("Berlin Grand SW2 U.C.", replaced.NameAfter);
    }

    [Fact]
    public void Set_list_edits_report_slot_moves_and_list_renames()
    {
        var before = Sample.Parse();

        var afterSwap = PcgReader.Parse(PcgEditor.SwapSetListSlots(Sample.Parse(), 0, 1, 2));
        var swapReport = PcgDiff.Compare(before, afterSwap);
        Assert.Equal(2, swapReport.SetListSlots.Count);
        Assert.All(swapReport.SetListSlots, e => Assert.Equal(DiffKind.Moved, e.Kind));
        Assert.Contains(swapReport.SetListSlots, e => e.Bank == 0 && e.IndexBefore == 1 && e.IndexAfter == 2);
        Assert.Contains(swapReport.SetListSlots, e => e.Bank == 0 && e.IndexBefore == 2 && e.IndexAfter == 1);

        var afterRename = PcgReader.Parse(PcgEditor.RenameSetList(Sample.Parse(), 0, "Diff SL Zz9"));
        var renameReport = PcgDiff.Compare(before, afterRename);
        var renamed = Assert.Single(renameReport.RenamedSetLists);
        Assert.Equal(0, renamed.Bank);
        Assert.Equal("Diff SL Zz9", renamed.NameAfter);
        Assert.Empty(renameReport.SetListSlots);
    }

    [Fact]
    public void Swapping_a_referenced_program_reports_the_move_and_the_retargeted_referrers_as_edited()
    {
        var before = Sample.Parse();
        // INT-A #000 (Berlin Grand) is heavily referenced; the swap retargets combi timbres
        // and the set-list slots that load it directly.
        var after = PcgReader.Parse(PcgEditor.SwapPrograms(Sample.Parse(), 0, 0, 0, 1));

        var report = PcgDiff.Compare(before, after);

        Assert.Contains(report.Programs, e => e.Kind == DiffKind.Moved && e.IndexBefore == 0 && e.IndexAfter == 1);
        Assert.Contains(report.Programs, e => e.Kind == DiffKind.Moved && e.IndexBefore == 1 && e.IndexAfter == 0);
        Assert.Contains(report.Combis, e => e.Kind == DiffKind.Edited);          // retargeted timbres
        Assert.Contains(report.SetListSlots, e =>                                // retargeted direct slot
            e.Kind == DiffKind.Edited && e.Bank == 0 && e.IndexAfter == 0 &&
            e.Detail is not null && e.Detail.Contains("#001"));
    }

    [Fact]
    public void Mismatched_record_layouts_are_rejected()
    {
        var pristine = Sample.Parse();
        var bank0 = pristine.FindFirst("PRG1")!.Children[0];
        var bytes = (byte[])pristine.Data.Clone();
        int sizeOffset = (int)bank0.DataOffset + 4;
        uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sizeOffset, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(sizeOffset, 4), size - 4);
        var patched = PcgReader.Parse(bytes);

        Assert.Throws<InvalidOperationException>(() => PcgDiff.Compare(pristine, patched));
    }

    private static (int Bank, int Index) FindPlaceholderProgram(PcgCatalog catalog, ProgramBankType type)
    {
        for (int b = 0; b < catalog.ProgramBanks.Count; b++)
        {
            if (catalog.ProgramBankTypes[b] != type)
                continue;
            for (int i = 0; i < catalog.ProgramBanks[b].Count; i++)
                if (PcgOrganizer.IsProgramPlaceholder(catalog.ProgramBanks[b][i]) &&
                    !string.IsNullOrEmpty(catalog.ProgramBanks[b][i]))
                    return (b, i);
        }
        throw new InvalidOperationException("Sample has no named init-placeholder program of that type.");
    }

    // A real (non-placeholder) program in a bank of the given type, in a bank other than
    // INT-A (the copy source) and with a name different from the copied program's.
    private static (int Bank, int Index) FindRealProgram(PcgCatalog catalog, ProgramBankType type, string avoidName)
    {
        for (int b = 1; b < catalog.ProgramBanks.Count; b++)
        {
            if (catalog.ProgramBankTypes[b] != type)
                continue;
            for (int i = 0; i < catalog.ProgramBanks[b].Count; i++)
            {
                var name = catalog.ProgramBanks[b][i];
                if (!PcgOrganizer.IsProgramPlaceholder(name) && name != avoidName)
                    return (b, i);
            }
        }
        throw new InvalidOperationException("Sample has no real program of that type outside INT-A.");
    }
}
