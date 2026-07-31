using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The offline "Copy From Program (IFX-All used)": a program's insert effects pack into a
/// combi's vacant slots without disturbing anything already there. Runs against the
/// Save All backup, which carries both engine types and busy factory content.
/// </summary>
public class ProgramFxCopyTests
{
    // Record offset via the public bank machinery — the tests' own reimplementation, so the
    // assertions don't lean on the code under test.
    private static (long Offset, int RecordSize) Locate(PcgFile pcg, string sectionId, int bank, int index)
    {
        var chunk = PcgBankIdentity.CanonicalBanks(pcg, sectionId)[bank]!;
        int recordSize = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            pcg.Data.AsSpan((int)chunk.DataOffset + 4, 4));
        return (chunk.DataOffset + 12 + (long)index * recordSize, recordSize);
    }

    private static PcgFile Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "files"))) dir = dir.Parent;
        string path = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "files"), "save-all.PCG",
            SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("save-all.PCG not found under files/.");
        return PcgReader.Parse(File.ReadAllBytes(path));
    }

    // A real program with at least minIfx used insert effects.
    private static ProgramInfo SourceProgram(IReadOnlyList<ProgramInfo> programs, int minIfx = 1) =>
        programs.First(p => !p.IsEmpty
            && p.Effects.Take(12).Count(e => e.HasEffect) >= minIfx);

    // A real combi with at least minFree structurally free insert slots.
    private static Combi TargetCombi(IReadOnlyList<Combi> combis, int minFree) =>
        combis.First(c => !c.IsEmptyOrInit
            && c.Effects.Take(12).Count(e => !e.HasEffect && !e.ChainOn) >= minFree
            && FreeCount(c) >= minFree);

    private static int FreeCount(Combi c)
    {
        var used = new bool[12];
        for (int k = 0; k < 12; k++)
        {
            if (c.Effects[k].HasEffect) used[k] = true;
            if (c.Effects[k].ChainOn && c.Effects[k].ChainTo >= k + 2)
                for (int j = k; j < c.Effects[k].ChainTo; j++) used[j] = true;
        }
        return used.Count(u => !u);
    }

    [Fact]
    public void Programs_decode_their_effects_with_chains()
    {
        var pcg = Load();
        var programs = ProgramReader.Read(pcg).Where(p => !p.IsEmpty).ToList();

        Assert.All(programs, p => Assert.Equal(16, p.Effects.Count));
        Assert.All(programs.SelectMany(p => p.Effects), e => Assert.InRange(e.TypeId, 0, 197));
        // Chains exist in factory content, and every chain link points forward.
        var chained = programs.SelectMany(p => p.Effects.Take(12)).Where(e => e.ChainOn).ToList();
        Assert.NotEmpty(chained);
        Assert.All(chained.Where(e => e.ChainTo is >= 1 and <= 12),
            e => Assert.True(e.ChainTo >= (int)e.Slot + 1));
    }

    [Fact]
    public void Plan_matches_the_arithmetic_and_packs_in_order()
    {
        var pcg = Load();
        var src = SourceProgram(ProgramReader.Read(pcg), minIfx: 2);
        var dst = TargetCombi(CombiReader.Read(pcg), minFree: 4);

        var plan = ProgramFxCopy.Plan(pcg, src.Bank, src.Index, dst.Bank, dst.Index);

        Assert.True(plan.Fits);
        Assert.Equal(plan.NeededIfx, plan.Placements.Count);
        // Ascending on both sides — chains can only run forward.
        Assert.Equal(plan.Placements.Select(p => p.SourceIfx).OrderBy(x => x),
                     plan.Placements.Select(p => p.SourceIfx));
        Assert.Equal(plan.Placements.Select(p => p.DestinationIfx).OrderBy(x => x),
                     plan.Placements.Select(p => p.DestinationIfx));
        // Every destination really is free in the combi.
        Assert.All(plan.Placements, p => Assert.Contains(p.DestinationIfx, plan.FreeIfx));
    }

    [Fact]
    public void Plan_refuses_a_full_combi()
    {
        var pcg = Load();
        var src = SourceProgram(ProgramReader.Read(pcg));
        var dst = CombiReader.Read(pcg).First(c => !c.IsEmptyOrInit);

        // Doctor every IFX slot of the combi to hold an effect (type 1 at the confirmed
        // offsets), so nothing is free.
        var (offset, _) = Locate(pcg, "CMB1", dst.Bank, dst.Index);
        var doctored = (byte[])pcg.Data.Clone();
        for (int k = 0; k < 12; k++)
            doctored[offset + 88 + 74 * k] = 1;

        var plan = ProgramFxCopy.Plan(PcgReader.Parse(doctored), src.Bank, src.Index, dst.Bank, dst.Index);

        Assert.False(plan.Fits);
        Assert.Empty(plan.Placements);
        Assert.Empty(plan.FreeIfx);
        Assert.Throws<InvalidOperationException>(() => ProgramFxCopy.Apply(
            PcgReader.Parse(doctored), src.Bank, src.Index, dst.Bank, dst.Index, timbre: 15));
    }

    [Fact]
    public void Apply_moves_the_effects_verbatim_and_leaves_the_rest_alone()
    {
        var pcg = Load();
        var src = SourceProgram(ProgramReader.Read(pcg), minIfx: 2);
        var dst = TargetCombi(CombiReader.Read(pcg), minFree: 4);
        var plan = ProgramFxCopy.Plan(pcg, src.Bank, src.Index, dst.Bank, dst.Index);

        var edited = ProgramFxCopy.Apply(pcg, src.Bank, src.Index, dst.Bank, dst.Index, timbre: 15);

        var after = PcgReader.Parse(edited);
        var combiAfter = CombiReader.Read(after).Single(c => c.Bank == dst.Bank && c.Index == dst.Index);

        // The timbre plays the program.
        Assert.Equal(src.Index, combiAfter.Timbres[15].ProgramNumber);
        Assert.Equal(PcgCatalog.ProgramBankPcgIdForIndex(src.Bank), combiAfter.Timbres[15].ProgramBankPcgId);

        // Each placed slot carries the source's header AND its parameter area — which sits
        // 64 bytes BEFORE the header (probe-proven geometry). Every other slot untouched.
        var (progOffset, _) = Locate(pcg, "PRG1", src.Bank, src.Index);
        var (combiOffset, _) = Locate(pcg, "CMB1", dst.Bank, dst.Index);
        var placedDst = plan.Placements.Select(p => p.DestinationIfx).ToHashSet();
        foreach (var p in plan.Placements)
        {
            Assert.Equal(src.Effects[p.SourceIfx].TypeId, combiAfter.Effects[p.DestinationIfx].TypeId);
            Assert.Equal(src.Effects[p.SourceIfx].IsOn, combiAfter.Effects[p.DestinationIfx].IsOn);
            for (int b = 0; b < 9; b++) // header, minus the renumberable chain byte
            {
                if (b == 2) continue;
                Assert.Equal(pcg.Data[progOffset + 88 + 74 * p.SourceIfx + b],
                             edited[combiOffset + 88 + 74 * p.DestinationIfx + b]);
            }
            for (int b = 0; b < 64; b++) // the packed parameter area travels verbatim
                Assert.Equal(pcg.Data[progOffset + 24 + 74 * p.SourceIfx + b],
                             edited[combiOffset + 24 + 74 * p.DestinationIfx + b]);
        }
        for (int k = 0; k < 12; k++)
        {
            if (placedDst.Contains(k)) continue;
            for (int b = 0; b < 9; b++)
                Assert.Equal(pcg.Data[combiOffset + 88 + 74 * k + b], edited[combiOffset + 88 + 74 * k + b]);
            for (int b = 0; b < 64; b++)
                Assert.Equal(pcg.Data[combiOffset + 24 + 74 * k + b], edited[combiOffset + 24 + 74 * k + b]);
        }

        // Masters untouched — they were not requested (params + headers, 912..1189).
        for (long o = combiOffset + 912; o < combiOffset + 1190; o++)
            Assert.Equal(pcg.Data[o], edited[o]);

        AssertChecksumsValid(pcg, edited);
    }

    [Fact]
    public void A_chain_is_renumbered_to_its_packed_slots()
    {
        var pcg = Load();
        var programs = ProgramReader.Read(pcg);
        var combis = CombiReader.Read(pcg);
        var srcSeed = SourceProgram(programs);
        var dstSeed = TargetCombi(combis, minFree: 6);

        // Doctor a deterministic scenario on real records: the program's IFX1 chains to
        // IFX3 (empty IFX2 inside the chain travels too), and the combi's IFX1–2 are busy,
        // so the chain must land shifted and be renumbered.
        var (progOffset, _) = Locate(pcg, "PRG1", srcSeed.Bank, srcSeed.Index);
        var (combiOffset, _) = Locate(pcg, "CMB1", dstSeed.Bank, dstSeed.Index);
        var doctored = (byte[])pcg.Data.Clone();
        for (int k = 0; k < 12; k++) // start the program's IFX from a clean slate
        {
            Array.Clear(doctored, (int)(progOffset + 88 + 74 * k), 74);
            doctored[progOffset + 88 + 74 * k + 1] = 0x10;
        }
        doctored[progOffset + 88 + 74 * 0] = 5;                    // IFX1: Stereo Compressor
        doctored[progOffset + 88 + 74 * 0 + 1] = 0x40 | 0x80;      // on + chained
        doctored[progOffset + 88 + 74 * 0 + 2] = 3;                // → IFX3
        doctored[progOffset + 88 + 74 * 2] = 11;                   // IFX3: the chain target
        doctored[progOffset + 88 + 74 * 2 + 1] = 0x40;
        for (int k = 0; k < 2; k++)                                // combi: IFX1–2 busy
            if (doctored[combiOffset + 88 + 74 * k] == 0)
                doctored[combiOffset + 88 + 74 * k] = 1;

        var file = PcgReader.Parse(doctored);
        var plan = ProgramFxCopy.Plan(file, srcSeed.Bank, srcSeed.Index, dstSeed.Bank, dstSeed.Index);
        Assert.True(plan.Fits);
        Assert.Equal(3, plan.NeededIfx); // IFX1 + interior IFX2 + IFX3

        var edited = ProgramFxCopy.Apply(file, srcSeed.Bank, srcSeed.Index, dstSeed.Bank, dstSeed.Index, timbre: 15);
        var combiAfter = CombiReader.Read(PcgReader.Parse(edited))
            .Single(c => c.Bank == dstSeed.Bank && c.Index == dstSeed.Index);

        var head = plan.Placements.Single(p => p.SourceIfx == 0);
        var tail = plan.Placements.Single(p => p.SourceIfx == 2);
        Assert.True(combiAfter.Effects[head.DestinationIfx].ChainOn);
        Assert.Equal(tail.DestinationIfx + 1, combiAfter.Effects[head.DestinationIfx].ChainTo);
        Assert.Equal(11, combiAfter.Effects[tail.DestinationIfx].TypeId);
    }

    [Fact]
    public void The_timbre_bus_follows_the_packed_effect()
    {
        var pcg = Load();
        var programs = ProgramReader.Read(pcg);
        var combis = CombiReader.Read(pcg);
        var srcSeed = SourceProgram(programs);
        var dstSeed = TargetCombi(combis, minFree: 6);

        // Program feeds IFX1 (bus value 1); combi IFX1–2 busy, so the bus must be remapped.
        var (progOffset, _) = Locate(pcg, "PRG1", srcSeed.Bank, srcSeed.Index);
        var (combiOffset, _) = Locate(pcg, "CMB1", dstSeed.Bank, dstSeed.Index);
        var doctored = (byte[])pcg.Data.Clone();
        for (int k = 0; k < 12; k++)
        {
            Array.Clear(doctored, (int)(progOffset + 88 + 74 * k), 74);
            doctored[progOffset + 88 + 74 * k + 1] = 0x10;
        }
        doctored[progOffset + 88] = 5;
        doctored[progOffset + 88 + 1] = 0x40;
        doctored[progOffset + 2565] = (byte)((doctored[progOffset + 2565] & 0xE0) | 1); // bus = IFX1
        for (int k = 0; k < 2; k++)
            if (doctored[combiOffset + 88 + 74 * k] == 0)
                doctored[combiOffset + 88 + 74 * k] = 1;

        var file = PcgReader.Parse(doctored);
        var plan = ProgramFxCopy.Plan(file, srcSeed.Bank, srcSeed.Index, dstSeed.Bank, dstSeed.Index);
        var placed = plan.Placements.Single(p => p.SourceIfx == 0);

        var edited = ProgramFxCopy.Apply(file, srcSeed.Bank, srcSeed.Index, dstSeed.Bank, dstSeed.Index, timbre: 15);

        long tOff = combiOffset + 4802 + 15L * 188;
        Assert.Equal(placed.DestinationIfx + 1, edited[tOff + 29] & 0x1F);
        // Sends came from the program (engine-aware offset).
        bool isExi = PcgBankIdentity.ProgramBankType(pcg, srcSeed.Bank) == ProgramBankType.Exi;
        int sendOffset = isExi ? 2864 : 3196;
        Assert.Equal(doctored[progOffset + sendOffset], edited[tOff + 15]);
        Assert.Equal(doctored[progOffset + sendOffset + 1], edited[tOff + 16]);
    }

    [Fact]
    public void Masters_copy_only_when_asked()
    {
        var pcg = Load();
        var src = SourceProgram(ProgramReader.Read(pcg));
        var dst = TargetCombi(CombiReader.Read(pcg), minFree: 4);
        var (progOffset, _) = Locate(pcg, "PRG1", src.Bank, src.Index);
        var (combiOffset, _) = Locate(pcg, "CMB1", dst.Bank, dst.Index);

        var withMfx = ProgramFxCopy.Apply(pcg, src.Bank, src.Index, dst.Bank, dst.Index, 15, includeMfx: true);
        for (long b = 0; b < 1052 - 912; b++)
            Assert.Equal(pcg.Data[progOffset + 912 + b], withMfx[combiOffset + 912 + b]);
        for (long b = 0; b < 1190 - 1052; b++) // TFX region untouched
            Assert.Equal(pcg.Data[combiOffset + 1052 + b], withMfx[combiOffset + 1052 + b]);

        var withTfx = ProgramFxCopy.Apply(pcg, src.Bank, src.Index, dst.Bank, dst.Index, 15, includeTfx: true);
        for (long b = 0; b < 1190 - 1052; b++)
            Assert.Equal(pcg.Data[progOffset + 1052 + b], withTfx[combiOffset + 1052 + b]);
        for (long b = 0; b < 1052 - 912; b++) // MFX region untouched
            Assert.Equal(pcg.Data[combiOffset + 912 + b], withTfx[combiOffset + 912 + b]);
    }

    [Fact]
    public void Clearing_an_insert_slot_is_guarded_structurally()
    {
        var pcg = Load();
        var combis = CombiReader.Read(pcg);

        // A loaded slot nothing feeds and no chain touches → clears to empty.
        foreach (var c in combis.Where(c => !c.IsEmptyOrInit))
        {
            var clearable = ClearableSlot(pcg, c);
            if (clearable is not { } slot)
                continue;
            var edited = ProgramFxCopy.ClearInsertEffect(pcg, c.Bank, c.Index, slot);
            var after = CombiReader.Read(PcgReader.Parse(edited))
                .Single(x => x.Bank == c.Bank && x.Index == c.Index);
            Assert.False(after.Effects[slot].HasEffect);
            AssertChecksumsValid(pcg, edited);
            break;
        }

        // A slot some timbre plays through → refused.
        foreach (var c in combis.Where(c => !c.IsEmptyOrInit))
        {
            var (offset, _) = Locate(pcg, "CMB1", c.Bank, c.Index);
            var fed = Enumerable.Range(0, 16)
                .Select(t => pcg.Data[offset + 4802 + t * 188 + 29] & 0x1F)
                .FirstOrDefault(bus => bus is >= 1 and <= 12);
            if (fed == 0)
                continue;
            Assert.Throws<InvalidOperationException>(() =>
                ProgramFxCopy.ClearInsertEffect(pcg, c.Bank, c.Index, fed - 1));
            break;
        }
    }

    private static int? ClearableSlot(PcgFile pcg, Combi c)
    {
        var (offset, _) = Locate(pcg, "CMB1", c.Bank, c.Index);
        var busesFed = Enumerable.Range(0, 16)
            .Select(t => pcg.Data[offset + 4802 + t * 188 + 29] & 0x1F)
            .Where(b => b is >= 1 and <= 12).Select(b => b - 1).ToHashSet();
        for (int k = 0; k < 12; k++)
        {
            var e = c.Effects[k];
            if (!e.HasEffect || e.ChainOn || busesFed.Contains(k))
                continue;
            bool inChain = c.Effects.Take(12).Any(x => x.ChainOn && x.ChainTo >= 1
                && k >= (int)x.Slot && k <= x.ChainTo - 1);
            bool dkitFed = Enumerable.Range(0, 16).Any(t =>
                (pcg.Data[offset + 4802 + t * 188 + 29] & 0x80) != 0
                && Enumerable.Range(0, 12).Any(j =>
                    (pcg.Data[offset + 4802 + t * 188 + 17 + j] & 0x1F) == k + 1));
            if (!inChain && !dkitFed)
                return k;
        }
        return null;
    }

    private static void AssertChecksumsValid(PcgFile pcg, byte[] edited)
    {
        foreach (var chunk in pcg.EnumerateChunks())
        {
            if (chunk.HasChildren || chunk.DataOffset < 1 || chunk.Size <= 0) continue;
            if (chunk.DataEnd > pcg.Data.Length) continue;
            byte stored = pcg.Data[chunk.DataOffset - 1];
            if (stored != PcgChecksum.Sum(pcg.Data, chunk.DataOffset, chunk.Size)) continue;
            Assert.Equal(PcgChecksum.Sum(edited, chunk.DataOffset, chunk.Size), edited[chunk.DataOffset - 1]);
        }
    }
}
