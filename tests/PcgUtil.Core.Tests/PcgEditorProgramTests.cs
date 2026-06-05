using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgEditorProgramTests
{
    [Fact]
    public void SwapPrograms_swaps_records_and_keeps_every_reference_resolving_to_the_same_program()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var name0 = catalog.ProgramBanks[0][0];
        var name1 = catalog.ProgramBanks[0][1];
        var before = ResolveAllProgramRefs(pcg);

        // Swap two programs in I-A (bank 0): #0 (Berlin Grand, heavily referenced) and #1.
        var edited = PcgEditor.SwapPrograms(pcg, 0, 0, 0, 1);
        var pcg2 = PcgReader.Parse(edited);

        // The two program records actually swapped.
        var catalog2 = PcgCatalog.Build(pcg2);
        Assert.Equal(name1, catalog2.ProgramBanks[0][0]);
        Assert.Equal(name0, catalog2.ProgramBanks[0][1]);

        // Integrity: every combi timbre and program-type set-list slot still resolves to the
        // same program as before the swap.
        var after = ResolveAllProgramRefs(pcg2);
        Assert.Equal(before.Count, after.Count);
        foreach (var (key, value) in before)
            Assert.Equal(value, after[key]);
    }

    [Fact]
    public void CopyProgram_overwrites_destination_and_leaves_source()
    {
        var pcg = Sample.Parse();
        var edited = PcgEditor.CopyProgram(pcg, srcBank: 0, srcIndex: 0, dstBank: 0, dstIndex: 1);

        var catalog = PcgCatalog.Build(PcgReader.Parse(edited));
        Assert.Equal("Berlin Grand SW2 U.C.", catalog.ProgramBanks[0][1]); // destination overwritten
        Assert.Equal("Berlin Grand SW2 U.C.", catalog.ProgramBanks[0][0]); // source unchanged
    }

    [Fact]
    public void RenameProgram_writes_the_name()
    {
        var pcg = Sample.Parse();
        var edited = PcgEditor.RenameProgram(pcg, 0, 0, "My Program");

        var catalog = PcgCatalog.Build(PcgReader.Parse(edited));
        Assert.Equal("My Program", catalog.ProgramBanks[0][0]);
    }

    [Fact]
    public void SwapPrograms_rejects_out_of_range()
    {
        var pcg = Sample.Parse();
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SwapPrograms(pcg, 0, 0, 99, 0));
    }

    // Resolves every program reference in the file (combi timbres + program-type set-list slots)
    // to a program name, so a swap can be checked for reference integrity.
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
}
