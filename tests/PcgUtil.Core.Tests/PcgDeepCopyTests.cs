using System.Linq;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// Deep copy: a combi travels with the programs its timbres reference, landing in free
/// slots (or reusing byte-identical programs already present), and the copied combi's
/// timbre bytes are rewritten so it keeps its sound in the destination file.
/// </summary>
public class PcgDeepCopyTests
{
    // Sample facts: combi bank 10 (USER-D) is all "Init Combi"; program bank 19 (USER-GG)
    // is HD-1 with 100+ free slots and bank 15 (USER-CC) is EXi with 50+ free; combi
    // (7,57) "Let's Go Crazy" has internal timbres.
    private const int SrcCombiBank = 7;
    private const int SrcCombiIndex = 57;
    private const int Hd1Bank = 19;
    private const int ExiBank = 15;
    private const int DstCombiBank = 10;

    [Fact]
    public void Deep_copy_lands_programs_and_repoints_timbres()
    {
        var (source, renamedProgram) = SourceWithOneRenamedProgram();
        var destination = Sample.Parse();
        var srcCatalog = PcgCatalog.Build(source);

        var plan = PcgDeepCopy.Plan(source, SrcCombiBank, SrcCombiIndex, destination, Hd1Bank, ExiBank);

        // The renamed program is no longer byte-identical to anything in the destination,
        // so it must be the plan's single real copy; every other dependency reuses.
        Assert.Null(plan.Error);
        var copy = Assert.Single(plan.Copies);
        Assert.Equal("Xdeep Prog Zz9", copy.Name);
        int expectedBank = copy.Type == ProgramBankType.Hd1 ? Hd1Bank : ExiBank;
        Assert.Equal(expectedBank, copy.DestinationBank);
        Assert.Equal(PcgDeepCopy.FreeProgramSlots(PcgCatalog.Build(destination).ProgramBanks[expectedBank])[0],
            copy.DestinationIndex);
        Assert.NotEmpty(plan.Reused);

        // Every mapping stays within its engine type.
        var dstTypes = PcgCatalog.Build(destination).ProgramBankTypes;
        Assert.All(plan.Programs, m => Assert.Equal(m.Type, dstTypes[m.DestinationBank]));

        // Mapped timbres and skips partition 0..15 exactly.
        var mapped = plan.Programs.SelectMany(p => p.Timbres);
        var all = mapped.Concat(plan.Skips.Select(s => s.Timbre)).OrderBy(i => i).ToList();
        Assert.Equal(Enumerable.Range(0, 16), all);

        var editedBytes = PcgEditor.CopyCombiDeepAcross(source, destination, DstCombiBank, 0, plan);
        AssertAllLeafChecksumsValid(destination, editedBytes);
        var edited = PcgReader.Parse(editedBytes);
        var editedCatalog = PcgCatalog.Build(edited);
        var editedCombi = CombiReader.Read(edited).Single(c => c.Bank == DstCombiBank && c.Index == 0);

        Assert.Equal("Let's Go Crazy", editedCombi.Name);

        // Every mapped timbre resolves in the destination to the name it resolved to in
        // the source (addresses may differ — that's the point).
        var sourceCombi = CombiReader.Read(source).Single(c => c.Bank == SrcCombiBank && c.Index == SrcCombiIndex);
        foreach (var m in plan.Programs)
            foreach (var t in m.Timbres)
            {
                var timbre = editedCombi.Timbres[t];
                var srcTimbre = sourceCombi.Timbres[t];
                Assert.Equal(
                    srcCatalog.ResolveProgram(srcTimbre.ProgramBankPcgId, srcTimbre.ProgramNumber),
                    editedCatalog.ResolveProgram(timbre.ProgramBankPcgId, timbre.ProgramNumber));
            }
        Assert.Contains(editedCombi.Timbres,
            t => editedCatalog.ResolveProgram(t.ProgramBankPcgId, t.ProgramNumber) == renamedProgram);

        // Skipped timbres keep their source bytes.
        foreach (var s in plan.Skips)
        {
            var timbre = editedCombi.Timbres[s.Timbre];
            Assert.Equal((s.ProgramBankPcgId, s.ProgramNumber),
                (timbre.ProgramBankPcgId, timbre.ProgramNumber));
        }

        // The source combi's own record in the destination is untouched, and so is the
        // free bank beyond the one allocated slot.
        Assert.Equal("Let's Go Crazy", editedCatalog.CombiBanks[SrcCombiBank][SrcCombiIndex]);
        var before = PcgCatalog.Build(destination).ProgramBanks[expectedBank];
        var after = editedCatalog.ProgramBanks[expectedBank];
        for (int i = 0; i < before.Count; i++)
            if (i != copy.DestinationIndex)
                Assert.Equal(before[i], after[i]);
    }

    [Fact]
    public void Second_copy_reuses_everything()
    {
        var (source, _) = SourceWithOneRenamedProgram();
        var destination = Sample.Parse();
        var plan = PcgDeepCopy.Plan(source, SrcCombiBank, SrcCombiIndex, destination, Hd1Bank, ExiBank);
        var once = PcgReader.Parse(PcgEditor.CopyCombiDeepAcross(source, destination, DstCombiBank, 0, plan));

        var again = PcgDeepCopy.Plan(source, SrcCombiBank, SrcCombiIndex, once, Hd1Bank, ExiBank);

        Assert.Null(again.Error);
        Assert.Empty(again.Copies);
        Assert.Equal(plan.Programs.Count, again.Reused.Count());

        // Reuse never lands in a wrong-type bank.
        var types = PcgCatalog.Build(once).ProgramBankTypes;
        Assert.All(again.Programs, m => Assert.Equal(m.Type, types[m.DestinationBank]));
    }

    [Fact]
    public void Missing_or_full_type_bank_is_a_plan_error_and_apply_refuses()
    {
        var (source, _) = SourceWithOneRenamedProgram();
        var destination = Sample.Parse();

        // INT-A is factory-full (no placeholder slots) and EXi-typed.
        var dstCatalog = PcgCatalog.Build(destination);
        Assert.Empty(PcgDeepCopy.FreeProgramSlots(dstCatalog.ProgramBanks[0]));
        Assert.Equal(ProgramBankType.Exi, dstCatalog.ProgramBankTypes[0]);

        // Whatever type the forced copy is, it has nowhere to go: HD-1 gets no bank at
        // all, EXi gets the full INT-A.
        var plan = PcgDeepCopy.Plan(source, SrcCombiBank, SrcCombiIndex, destination, null, 0);
        Assert.NotNull(plan.Error);
        Assert.False(plan.CanApply);
        Assert.Throws<InvalidOperationException>(
            () => PcgEditor.CopyCombiDeepAcross(source, destination, DstCombiBank, 0, plan));
    }

    [Fact]
    public void Wrong_type_bank_choice_is_rejected_outright()
    {
        var (source, _) = SourceWithOneRenamedProgram();
        var destination = Sample.Parse();

        // Passing an EXi bank in the HD-1 role (and vice versa) is a caller error.
        Assert.Throws<InvalidOperationException>(
            () => PcgDeepCopy.Plan(source, SrcCombiBank, SrcCombiIndex, destination, ExiBank, Hd1Bank));
    }

    // Vendor-pack transplant: MONEY4FREE OPEN plays nine programs spanning BOTH engine
    // types — U-EE is EXi and U-FF is HD-1 in the pack — plus seven filler timbres
    // pointing at a bank the pack doesn't carry. Silently passes without the pack.
    [Fact]
    public void Vendor_pack_combi_transplants_with_its_programs()
    {
        if (VendorPack.Parse() is not { } pack)
            return;

        var destination = Sample.Parse();
        var plan = PcgDeepCopy.Plan(pack, 11, 0, destination, Hd1Bank, ExiBank); // U-E #000

        Assert.Null(plan.Error);
        Assert.Equal("MONEY4FREE OPEN", plan.CombiName);
        Assert.True(plan.UsesUserKarmaGes); // M4NSeqInCMAJ.mid lives in the pack's .KGE
        Assert.Equal(7, plan.Skips.Count(s =>
            s.Reason == DeepCopySkipReason.Unresolved && s.ProgramBankPcgId == 0 && s.ProgramNumber == 0));
        Assert.Equal(9, plan.Copies.Count()); // 9 distinct programs, nothing to reuse on first transplant
        Assert.All(plan.Programs, m => Assert.Contains(m.SourceBank, new[] { 17, 18 })); // U-EE / U-FF

        // Mixed-type proof: the pack's U-EE is EXi, U-FF is HD-1, and each copy lands in
        // the destination bank of its own type.
        Assert.True(plan.Needs(ProgramBankType.Hd1));
        Assert.True(plan.Needs(ProgramBankType.Exi));
        Assert.All(plan.Programs, m => Assert.Equal(m.Type == ProgramBankType.Hd1 ? Hd1Bank : ExiBank, m.DestinationBank));
        Assert.All(plan.Programs.Where(m => m.SourceBank == 17), m => Assert.Equal(ProgramBankType.Exi, m.Type));
        Assert.All(plan.Programs.Where(m => m.SourceBank == 18), m => Assert.Equal(ProgramBankType.Hd1, m.Type));

        var editedBytes = PcgEditor.CopyCombiDeepAcross(pack, destination, DstCombiBank, 1, plan);
        AssertAllLeafChecksumsValid(destination, editedBytes);
        var edited = PcgReader.Parse(editedBytes);
        var editedCatalog = PcgCatalog.Build(edited);
        var combi = CombiReader.Read(edited).Single(c => c.Bank == DstCombiBank && c.Index == 1);

        foreach (var m in plan.Programs)
            foreach (var t in m.Timbres)
            {
                var name = editedCatalog.ResolveProgram(
                    combi.Timbres[t].ProgramBankPcgId, combi.Timbres[t].ProgramNumber);
                Assert.NotNull(name);
                Assert.StartsWith("MONEY4", name);
            }
        foreach (var s in plan.Skips.Where(s => s.Reason == DeepCopySkipReason.Unresolved))
            Assert.Equal((0, 0),
                (combi.Timbres[s.Timbre].ProgramBankPcgId, combi.Timbres[s.Timbre].ProgramNumber));
    }

    // Shallow cross-file copy gets the same protection: the pack's U-EE (EXi) programs
    // must not land in the sample's HD-1 USER-GG — this exact move produced a file the
    // hardware refused ("File unavailable").
    [Fact]
    public void Shallow_program_copy_rejects_type_mismatch()
    {
        if (VendorPack.Parse() is not { } pack)
            return;

        var destination = Sample.Parse();
        var ex = Assert.Throws<InvalidOperationException>(
            () => PcgEditor.CopyProgramAcross(pack, 17, 0, destination, Hd1Bank, 120));
        Assert.Contains("EXi", ex.Message);

        // Same-type copy is still allowed.
        var ok = PcgEditor.CopyProgramAcross(pack, 17, 0, destination, ExiBank, 120);
        Assert.NotNull(ok);
    }

    [Fact]
    public void FreeCombiSlots_finds_init_slots()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());

        // USER-D (bank 10) is entirely "Init Combi" in the sample; INT-A is factory-full.
        Assert.Equal(Enumerable.Range(0, 128), PcgDeepCopy.FreeCombiSlots(catalog.CombiBanks[DstCombiBank]));
        Assert.Empty(PcgDeepCopy.FreeCombiSlots(catalog.CombiBanks[0]));
    }

    [Fact]
    public void FreeProgramSlots_are_ascending_placeholders()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());
        var free = PcgDeepCopy.FreeProgramSlots(catalog.ProgramBanks[Hd1Bank]);

        Assert.NotEmpty(free);
        Assert.Equal(free.OrderBy(i => i), free);
        Assert.All(free, i => Assert.True(PcgOrganizer.IsProgramPlaceholder(catalog.ProgramBanks[Hd1Bank][i])));
    }

    // Renames the first internally played program of the test combi in the SOURCE file so
    // exactly one dependency is byte-unique (everything else reuses on a same-sample copy).
    private static (PcgFile Source, string RenamedProgram) SourceWithOneRenamedProgram()
    {
        var source = Sample.Parse();
        var combi = CombiReader.Read(source).Single(c => c.Bank == SrcCombiBank && c.Index == SrcCombiIndex);
        var timbre = combi.Timbres.First(t => t.UsesInternalProgram);
        int bank = PcgCatalog.ProgramBankIndexForPcgId(timbre.ProgramBankPcgId);
        var renamed = PcgReader.Parse(
            PcgEditor.RenameProgram(source, bank, timbre.ProgramNumber, "Xdeep Prog Zz9"));
        return (renamed, "Xdeep Prog Zz9");
    }

    // Same convention detection as PcgEditorCrossFileTests: a leaf is checksummed when its
    // stored byte matched its data in the pristine destination; those leaves must still
    // match in the edited bytes.
    private static void AssertAllLeafChecksumsValid(PcgFile pristine, byte[] edited)
    {
        foreach (var chunk in pristine.EnumerateChunks())
        {
            if (chunk.HasChildren || chunk.DataOffset < 1 || chunk.Size <= 0) continue;
            if (chunk.DataEnd > pristine.Data.Length) continue;

            byte stored = pristine.Data[chunk.DataOffset - 1];
            if (stored != PcgChecksum.Sum(pristine.Data, chunk.DataOffset, chunk.Size)) continue;

            Assert.Equal(PcgChecksum.Sum(edited, chunk.DataOffset, chunk.Size), edited[chunk.DataOffset - 1]);
        }
    }
}
