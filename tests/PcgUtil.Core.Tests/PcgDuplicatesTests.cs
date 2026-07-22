using System.Buffers.Binary;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgDuplicatesTests
{
    // ----- Real-file pins -----

    [Fact]
    public void Init_placeholders_are_excluded_and_counted()
    {
        var report = PcgDuplicates.Combis(Sample.Parse());

        // The factory "Init Combi" floods (bank USER-D alone is 128 of them) are summarized,
        // not listed as duplicate groups.
        Assert.True(report.InitPlaceholderCount >= 128);
        Assert.All(report.SoundDuplicates, g =>
            Assert.All(g.Members, m => Assert.False(Combi.IsEmptyOrInitName(m.Name))));
        Assert.All(report.NameCollisions, g => Assert.False(Combi.IsEmptyOrInitName(g.Name)));
        // The gig file really does hold renamed inits — named slots whose content is pure
        // factory init ("Band On The Run" in USER-A #026 among them). Content matching
        // catches what name matching never could.
        Assert.Equal(4, report.RenamedInits.Count);
        Assert.Contains(report.RenamedInits, m => m.Bank == 7 && m.Index == 26 && m.Name == "Band On The Run");
    }

    [Fact]
    public void Program_renamed_inits_are_found_in_the_sample()
    {
        var report = PcgDuplicates.Programs(Sample.Parse());
        // Three named program slots hold untouched init content; one even carries its own
        // save location in its name ("U-C045 All Star Arp" sitting at USER-C #045).
        Assert.Equal(3, report.RenamedInits.Count);
        Assert.Contains(report.RenamedInits, m => m.Bank == 8 && m.Index == 45 && m.Name == "U-C045 All Star Arp");
    }

    [Fact]
    public void Groups_are_ordered_by_count_descending()
    {
        var report = PcgDuplicates.Combis(Sample.Parse());
        for (int i = 1; i < report.SoundDuplicates.Count; i++)
            Assert.True(report.SoundDuplicates[i - 1].Count >= report.SoundDuplicates[i].Count);
    }

    [Fact]
    public void Programs_report_is_well_formed()
    {
        // Program sounds may all be unique (no groups) — assert the contract holds.
        var report = PcgDuplicates.Programs(Sample.Parse());
        Assert.All(report.SoundDuplicates, g =>
        {
            Assert.True(g.Count >= 2);
            Assert.NotEmpty(g.Names);
            Assert.All(g.Members, m => Assert.False(string.IsNullOrEmpty(m.Name)));
        });
        Assert.All(report.NameCollisions, g => Assert.True(g.DistinctSounds >= 2));
    }

    // ----- Synthetic pins: Dan's two questions made executable -----

    [Fact]
    public void Renamed_copy_still_groups_as_the_same_sound()
    {
        // Copy a real combi into an init slot, then rename the copy — content matching
        // must still pair it with its source.
        var copied = PcgReader.Parse(PcgEditor.CopyCombi(Sample.Parse(), 7, 57, 10, 0));
        var pcg = PcgReader.Parse(PcgEditor.RenameCombi(copied, 10, 0, "Zz Renamed Copy"));
        string sourceName = CombiReader.Read(pcg).First(c => c.Bank == 7 && c.Index == 57).Name;

        var report = PcgDuplicates.Combis(pcg);
        var group = Assert.Single(report.SoundDuplicates, g =>
            g.Members.Any(m => m.Bank == 7 && m.Index == 57) &&
            g.Members.Any(m => m.Bank == 10 && m.Index == 0));
        Assert.Contains("Zz Renamed Copy", group.Names);
        Assert.Contains(sourceName, group.Names);
        Assert.False(group.AllExact);
        Assert.False(group.FavoriteOnly);
        // Real content in a former init slot is a duplicate, not a renamed init.
        Assert.DoesNotContain(report.RenamedInits, m => m.Bank == 10 && m.Index == 0);
    }

    [Fact]
    public void Favorited_exact_copy_still_groups_and_flags_favorite_only()
    {
        var pristine = Sample.Parse();
        var (bank, srcIndex, dstIndex) = FindRealAndPlaceholderInSameProgramBank(pristine);
        var copied = PcgReader.Parse(PcgEditor.CopyProgram(pristine, bank, srcIndex, bank, dstIndex));

        var exact = FindGroupWith(PcgDuplicates.Programs(copied), bank, srcIndex, dstIndex);
        Assert.True(exact.AllExact);
        Assert.False(exact.FavoriteOnly);

        // Star the copy by flipping the favorite bit directly, then re-scan: same group,
        // no longer exact, and the difference is recognized as favorite-only.
        var bytes = (byte[])copied.Data.Clone();
        long record = LocateRecord(copied, "PRG1", bank, dstIndex);
        bytes[record + ProgramReader.FavoriteOffset] |= (byte)ProgramReader.FavoriteBit;
        var starred = FindGroupWith(PcgDuplicates.Programs(PcgReader.Parse(bytes)), bank, srcIndex, dstIndex);
        Assert.False(starred.AllExact);
        Assert.True(starred.FavoriteOnly);
        Assert.Single(starred.Names); // same name on both — only the star differs
    }

    [Fact]
    public void Renamed_init_is_reported_as_init_content()
    {
        var pristineCount = PcgDuplicates.Combis(Sample.Parse()).InitPlaceholderCount;
        var pcg = PcgReader.Parse(PcgEditor.RenameCombi(Sample.Parse(), 10, 5, "My New Song"));

        var report = PcgDuplicates.Combis(pcg);
        var renamed = Assert.Single(report.RenamedInits, m => m.Bank == 10 && m.Index == 5);
        Assert.Equal("My New Song", renamed.Name);
        Assert.Equal(pristineCount - 1, report.InitPlaceholderCount);
        // Init content never shows up as a duplicate group member.
        Assert.DoesNotContain(report.SoundDuplicates,
            g => g.Members.Any(m => m.Bank == 10 && m.Index == 5));
    }

    [Fact]
    public void Same_name_different_sound_is_a_name_collision()
    {
        var pristine = Sample.Parse();
        var (keep, rename) = FindTwoDistinctSoundCombis(pristine);
        var pcg = PcgReader.Parse(PcgEditor.RenameCombi(pristine, rename.Bank, rename.Index, keep.Name));

        var report = PcgDuplicates.Combis(pcg);
        var collision = Assert.Single(report.NameCollisions, g => g.Name == keep.Name);
        Assert.True(collision.DistinctSounds >= 2);
        Assert.Contains(collision.Members, m => m.Bank == keep.Bank && m.Index == keep.Index);
        Assert.Contains(collision.Members, m => m.Bank == rename.Bank && m.Index == rename.Index);
        // Different sounds must not merge into a sound-duplicate group.
        Assert.DoesNotContain(report.SoundDuplicates, g =>
            g.Members.Any(m => m.Bank == keep.Bank && m.Index == keep.Index) &&
            g.Members.Any(m => m.Bank == rename.Bank && m.Index == rename.Index));
    }

    // ----- Helpers (public API only) -----

    private static SoundDuplicateGroup FindGroupWith(DuplicateReport report, int bank, int a, int b) =>
        Assert.Single(report.SoundDuplicates, g =>
            g.Members.Any(m => m.Bank == bank && m.Index == a) &&
            g.Members.Any(m => m.Bank == bank && m.Index == b));

    /// <summary>A bank holding both a real program and an init placeholder (same engine type
    /// by construction, so an in-bank copy is always legal).</summary>
    private static (int Bank, int SrcIndex, int DstIndex) FindRealAndPlaceholderInSameProgramBank(PcgFile pcg)
    {
        foreach (var bankGroup in ProgramReader.Read(pcg).GroupBy(p => p.Bank))
        {
            var real = bankGroup.FirstOrDefault(p => !PcgOrganizer.IsProgramPlaceholder(p.Name));
            var placeholder = bankGroup.FirstOrDefault(p => PcgOrganizer.IsProgramPlaceholder(p.Name));
            if (real is not null && placeholder is not null)
                return (bankGroup.Key, real.Index, placeholder.Index);
        }
        throw new InvalidOperationException("Sample has no bank with both a real program and a placeholder.");
    }

    /// <summary>Two differently-named real combis that are not already sound-duplicates of
    /// each other — renaming one onto the other must create a collision, never a merge.</summary>
    private static (Combi Keep, Combi Rename) FindTwoDistinctSoundCombis(PcgFile pcg)
    {
        var report = PcgDuplicates.Combis(pcg);
        var combis = CombiReader.Read(pcg).Where(c => !c.IsEmptyOrInit).ToList();
        foreach (var keep in combis)
        {
            foreach (var other in combis)
            {
                if (other.Name == keep.Name)
                    continue;
                bool grouped = report.SoundDuplicates.Any(g =>
                    g.Members.Any(m => m.Bank == keep.Bank && m.Index == keep.Index) &&
                    g.Members.Any(m => m.Bank == other.Bank && m.Index == other.Index));
                if (!grouped)
                    return (keep, other);
            }
        }
        throw new InvalidOperationException("Sample has no two distinct-sound combis.");
    }

    private static long LocateRecord(PcgFile pcg, string sectionId, int bank, int index)
    {
        var chunk = PcgBankIdentity.CanonicalBanks(pcg, sectionId)[bank]!;
        int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)chunk.DataOffset + 4, 4));
        return chunk.DataOffset + 12 + (long)index * recordSize;
    }
}
