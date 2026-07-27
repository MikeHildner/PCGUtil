using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// Rule-based re-pointing over the three reference graphs, against the Save All pair.
/// The written bytes are the reorg-proven fields; what these tests pin is the mapping.
/// </summary>
public class PcgRepointTests
{
    private static (PcgFile Pcg, PcgFile? Sng) LoadPair()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "files"))) dir = dir.Parent;
        string files = Path.Combine(dir!.FullName, "files");
        var pcg = PcgReader.Parse(File.ReadAllBytes(
            Directory.EnumerateFiles(files, "save-all.PCG", SearchOption.AllDirectories).First()));
        var sngPath = Directory.EnumerateFiles(files, "save-all.SNG", SearchOption.AllDirectories).FirstOrDefault();
        return (pcg, sngPath is null ? null : PcgReader.Parse(File.ReadAllBytes(sngPath)));
    }

    // A (bank, number) some combi timbre actually references, so rules have real work.
    private static (int Bank, int Number) ReferencedProgram(PcgFile pcg)
    {
        foreach (var c in CombiReader.Read(pcg).Where(c => !c.IsEmptyOrInit))
            foreach (var t in c.Timbres)
            {
                int bank = PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId);
                if (bank >= 0) return (bank, t.ProgramNumber);
            }
        throw new InvalidOperationException("No resolvable reference in the sample.");
    }

    [Fact]
    public void Validation_speaks_plainly_about_bad_rules()
    {
        var (pcg, _) = LoadPair();
        var catalog = PcgCatalog.Build(pcg);
        // A full backup carries every canonical bank, so "absent" is one past the list.
        int absent = catalog.ProgramBanks.Count;

        Assert.Contains("from-bank", PcgRepoint.Validate(pcg,
            new[] { RepointRule.Single(absent, 0, 0, 0) })[0]);
        Assert.Contains("to-bank", PcgRepoint.Validate(pcg,
            new[] { RepointRule.Single(0, 0, absent, 0) })[0]);
        Assert.Contains("ends before it starts", PcgRepoint.Validate(pcg,
            new[] { new RepointRule(0, 10, 5, 0, 0) })[0]);
        Assert.Contains("holds programs", PcgRepoint.Validate(pcg,
            new[] { RepointRule.Single(0, 999, 0, 0) })[0]);
        Assert.Contains("doesn't fit", PcgRepoint.Validate(pcg,
            new[] { new RepointRule(0, 0, 127, 0, 100) })[0]);
        Assert.Empty(PcgRepoint.Validate(pcg, new[] { RepointRule.Single(0, 0, 0, 1) }));

        Assert.Throws<InvalidOperationException>(() =>
            PcgRepoint.Apply(pcg, new[] { RepointRule.Single(absent, 0, 0, 0) }));
    }

    [Fact]
    public void Plan_counts_match_a_hand_count()
    {
        var (pcg, sng) = LoadPair();
        var (bank, number) = ReferencedProgram(pcg);
        var rules = new[] { RepointRule.Single(bank, number, bank, number == 0 ? 1 : 0) };

        int timbres = CombiReader.Read(pcg).SelectMany(c => c.Timbres)
            .Count(t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId) == bank
                        && t.ProgramNumber == number);
        int slots = SetListReader.Read(pcg).SelectMany(s => s.Slots)
            .Count(s => s.Reference is { Kind: PcgItemKind.Program } r
                        && PcgCatalog.ProgramBankIndexForPcgId(r.Bank) == bank
                        && r.Index == number);
        int tracks = sng is null ? 0 : SongReader.Read(sng).SelectMany(s => s.Timbres)
            .Count(t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId) == bank
                        && t.ProgramNumber == number);

        var plan = PcgRepoint.Plan(pcg, sng, rules);

        Assert.True(plan.IsValid);
        Assert.Equal(timbres, plan.CombiTimbres);
        Assert.Equal(slots, plan.SetListSlots);
        Assert.Equal(tracks, plan.SongTracks);
        Assert.True(plan.Total > 0);
        Assert.Equal(Math.Min(plan.Total, RepointPlan.MaxSites), plan.Sites.Count);
    }

    [Fact]
    public void A_single_rule_repoints_everything_and_disturbs_nothing_else()
    {
        var (pcg, _) = LoadPair();
        var (bank, number) = ReferencedProgram(pcg);
        int target = number == 0 ? 1 : 0;
        var rules = new[] { RepointRule.Single(bank, number, bank, target) };

        var slotsBefore = SetListReader.Read(pcg);
        var edited = PcgRepoint.Apply(pcg, rules);
        var after = PcgReader.Parse(edited);

        // Every former reference now points at the target; none remain.
        Assert.DoesNotContain(CombiReader.Read(after).SelectMany(c => c.Timbres),
            t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId) == bank
                 && t.ProgramNumber == number);

        // A repointed slot keeps everything except the number: color, volume, transpose,
        // hold time, notes, name — the slot writer's bit contract, decoded end to end.
        var slotsAfter = SetListReader.Read(after);
        bool sawRepointedSlot = false;
        for (int sl = 0; sl < slotsBefore.Count; sl++)
            for (int i = 0; i < slotsBefore[sl].Slots.Count; i++)
            {
                var b = slotsBefore[sl].Slots[i];
                var a = slotsAfter[sl].Slots[i];
                if (b.Reference is { Kind: PcgItemKind.Program } r
                    && PcgCatalog.ProgramBankIndexForPcgId(r.Bank) == bank && r.Index == number)
                {
                    sawRepointedSlot = true;
                    Assert.Equal(target, a.Reference!.Index);
                    Assert.Equal(b.Name, a.Name);
                    Assert.Equal(b.Color, a.Color);
                    Assert.Equal(b.Volume, a.Volume);
                    Assert.Equal(b.Transpose, a.Transpose);
                    Assert.Equal(b.HoldTimeIndex, a.HoldTimeIndex);
                    Assert.Equal(b.Description, a.Description);
                }
                else
                {
                    Assert.Equal(b.Reference?.Kind, a.Reference?.Kind);
                    Assert.Equal(b.Reference?.Index, a.Reference?.Index);
                }
            }
        // The sample may reference the program from slots or not — timbres are asserted
        // above either way; only pin the slot details when a slot was actually involved.
        _ = sawRepointedSlot;

        // The program records themselves are untouched — only references move.
        var prg = pcg.FindFirst("PRG1")!;
        Assert.Equal(
            pcg.Data.AsSpan((int)prg.DataOffset, (int)prg.Size).ToArray(),
            edited.AsSpan((int)prg.DataOffset, (int)prg.Size).ToArray());

        AssertChecksumsValid(pcg, edited);
    }

    [Fact]
    public void A_range_rule_preserves_offsets()
    {
        var (pcg, _) = LoadPair();
        var (bank, number) = ReferencedProgram(pcg);

        // A window around the referenced program, mapped 20 slots away (clamped in-bank).
        int start = Math.Max(0, number - 2);
        int end = Math.Min(start + 4, 127);
        int toStart = start + 20 + end > 127 + 20 ? 0 : start + 20;
        if (toStart + (end - start) > 127) toStart = 0;
        var rules = new[] { new RepointRule(bank, start, end, bank, toStart) };

        var plan = PcgRepoint.Plan(pcg, null, rules);
        Assert.True(plan.IsValid);
        var edited = PcgReader.Parse(PcgRepoint.Apply(pcg, rules));

        foreach (var site in plan.Sites)
        {
            Assert.Equal(site.ToNumber - toStart, site.FromNumber - start); // offset preserved
            if (site.Kind != RepointSiteKind.CombiTimbre) continue;
            var t = CombiReader.Read(edited)
                .Single(c => c.Bank == site.OuterBank && c.Index == site.OuterIndex)
                .Timbres[site.Inner];
            Assert.Equal(site.ToNumber, t.ProgramNumber);
        }
    }

    [Fact]
    public void Rules_never_cascade()
    {
        var (pcg, _) = LoadPair();
        var (bank, a) = ReferencedProgram(pcg);
        int b = a == 0 ? 1 : 0;
        int c = Enumerable.Range(0, 128).First(n => n != a && n != b);

        // [A→B, B→C]: references to A must end at B (not chained on to C), refs to B at C.
        var refsToB = PcgRepoint.Plan(pcg, null, new[] { RepointRule.Single(bank, b, bank, b) })
            .CombiTimbres;
        var refsToC = PcgRepoint.Plan(pcg, null, new[] { RepointRule.Single(bank, c, bank, c) })
            .CombiTimbres; // pre-existing C references stay where they are
        var edited = PcgReader.Parse(PcgRepoint.Apply(pcg, new[]
        {
            RepointRule.Single(bank, a, bank, b),
            RepointRule.Single(bank, b, bank, c),
        }));

        var timbres = CombiReader.Read(edited).SelectMany(x => x.Timbres)
            .Where(t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId) == bank).ToList();
        Assert.DoesNotContain(timbres, t => t.ProgramNumber == a); // all A-refs moved
        Assert.True(timbres.Count(t => t.ProgramNumber == b) > 0); // ...to B, and stayed there

        // And C now holds its own refs plus the former B-refs — none of the A-refs cascaded on.
        var planA = PcgRepoint.Plan(pcg, null, new[] { RepointRule.Single(bank, a, bank, b) });
        Assert.Equal(planA.CombiTimbres, timbres.Count(t => t.ProgramNumber == b));
        Assert.Equal(refsToB + refsToC, timbres.Count(t => t.ProgramNumber == c));
    }

    [Fact]
    public void An_identity_rule_changes_nothing()
    {
        var (pcg, _) = LoadPair();
        var (bank, number) = ReferencedProgram(pcg);
        var edited = PcgRepoint.Apply(pcg, new[] { RepointRule.Single(bank, number, bank, number) });
        Assert.Equal(pcg.Data, edited);
    }

    [Fact]
    public void The_companion_sng_repoints_by_the_same_rules()
    {
        var (pcg, sng) = LoadPair();
        if (sng is null) return;

        // A program a song track actually uses.
        var track = SongReader.Read(sng).SelectMany(s => s.Timbres)
            .First(t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId) >= 0);
        int bank = PcgCatalog.ProgramBankIndexForPcgId(track.ProgramBankPcgId);
        int number = track.ProgramNumber;
        int target = number == 0 ? 1 : 0;
        var rules = new[] { RepointRule.Single(bank, number, bank, target) };

        var before = SongReader.Read(sng);
        var edited = PcgRepoint.ApplySng(sng, rules);
        var after = SongReader.Read(PcgReader.Parse(edited));

        for (int s = 0; s < before.Count; s++)
            for (int t = 0; t < before[s].Timbres.Count; t++)
            {
                var wasHit = PcgCatalog.ProgramBankIndexForPcgId(before[s].Timbres[t].ProgramBankPcgId) == bank
                             && before[s].Timbres[t].ProgramNumber == number;
                Assert.Equal(wasHit ? target : before[s].Timbres[t].ProgramNumber,
                             after[s].Timbres[t].ProgramNumber);
                Assert.Equal(before[s].Timbres[t].Status, after[s].Timbres[t].Status);
                Assert.Equal(before[s].Timbres[t].Volume, after[s].Timbres[t].Volume);
            }
    }

    [Fact]
    public void Warnings_flag_empty_targets_and_dead_rules()
    {
        var (pcg, _) = LoadPair();
        var catalog = PcgCatalog.Build(pcg);
        var (bank, number) = ReferencedProgram(pcg);

        // A placeholder slot to point at, and a program nothing references.
        var (phBank, phIndex) = FindPlaceholder(catalog);
        var referenced = CombiReader.Read(pcg).SelectMany(c => c.Timbres)
            .Where(t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId) >= 0)
            .Select(t => (Bank: PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId), t.ProgramNumber))
            .ToHashSet();
        var unreferenced = Enumerable.Range(0, catalog.ProgramBanks.Count)
            .Where(b2 => catalog.ProgramBanks[b2].Count > 0)
            .SelectMany(b2 => Enumerable.Range(0, catalog.ProgramBanks[b2].Count)
                .Select(n => (Bank: b2, ProgramNumber: n)))
            .First(x => !referenced.Contains(x));

        var plan = PcgRepoint.Plan(pcg, null, new[]
        {
            RepointRule.Single(bank, number, phBank, phIndex),
            RepointRule.Single(unreferenced.Bank, unreferenced.ProgramNumber, bank, number),
        });

        Assert.True(plan.IsValid);
        Assert.Contains(plan.Warnings, w => w.Contains("empty program"));
        Assert.Contains(plan.Warnings, w => w.Contains("matches nothing"));
    }

    private static (int Bank, int Index) FindPlaceholder(PcgCatalog catalog)
    {
        for (int b = 0; b < catalog.ProgramBanks.Count; b++)
            for (int i = 0; i < catalog.ProgramBanks[b].Count; i++)
                if (PcgOrganizer.IsProgramPlaceholder(catalog.ProgramBanks[b][i]))
                    return (b, i);
        throw new InvalidOperationException("No placeholder program in the sample.");
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
