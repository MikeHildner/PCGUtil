using System.Linq;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class CombiReaderTests
{
    [Fact]
    public void Reads_combis_with_16_timbres_each()
    {
        var combis = CombiReader.Read(Sample.Parse());
        Assert.NotEmpty(combis);
        Assert.All(combis, c => Assert.Equal(16, c.Timbres.Count));
    }

    [Fact]
    public void Combi_names_match_the_catalog()
    {
        var pcg = Sample.Parse();
        var combis = CombiReader.Read(pcg);
        var catalog = PcgCatalog.Build(pcg);

        // "Let's Go Crazy" is combi bank 7 (USER-A) #57.
        var c = combis.Single(x => x.Bank == 7 && x.Index == 57);
        Assert.Equal("Let's Go Crazy", c.Name);
        Assert.Equal(catalog.CombiBanks[7][57], c.Name);
    }

    // The decode (offsets 4802 / 16×188, number @ +0, bank PcgId @ +1) is correct when
    // virtually every enabled timbre resolves to a real program name.
    [Fact]
    public void Enabled_timbres_resolve_to_real_programs()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var combis = CombiReader.Read(pcg);

        int enabled = 0, resolved = 0;
        foreach (var c in combis.Where(c => !c.IsEmpty))
            foreach (var t in c.Timbres.Where(t => t.UsesInternalProgram))
            {
                enabled++;
                if (catalog.ResolveProgram(t.ProgramBankPcgId, t.ProgramNumber) is not null)
                    resolved++;
            }

        Assert.True(enabled > 1000, $"expected many enabled timbres, got {enabled}");
        Assert.True(resolved / (double)enabled > 0.98, $"resolve rate too low: {resolved}/{enabled}");
    }

    // Zone/volume/tempo decodes must land inside their hardware ranges on every record —
    // a wrong offset shows up as violated invariants almost immediately.
    [Fact]
    public void Timbre_zones_and_combi_headers_are_within_hardware_ranges()
    {
        var combis = CombiReader.Read(Sample.Parse());

        foreach (var c in combis.Where(c => !c.IsEmptyOrInit))
        {
            Assert.InRange(c.Category, 0, 17);
            Assert.InRange(c.SubCategory, 0, 7);
            Assert.InRange(c.Tempo, 40.00m, 300.00m);

            foreach (var t in c.Timbres.Where(t => t.UsesInternalProgram))
            {
                Assert.True(t.BottomKey <= t.TopKey, $"{c.Name} T{t.Index + 1} key zone inverted");
                Assert.True(t.BottomVelocity <= t.TopVelocity, $"{c.Name} T{t.Index + 1} vel zone inverted");
                Assert.InRange(t.TopKey, 0, 127);
                Assert.InRange(t.Volume, 0, 127);
                Assert.InRange(t.MidiChannel, 0, 16); // 16 = Gch
                Assert.InRange(t.Transpose, -60, 60);
                Assert.InRange(t.Detune, -1200, 1200);
            }
        }
    }

    // The vendor pack's set-list notes describe zones in prose, giving independent ground
    // truth: "Bb4 and up is theme synth" / "F#3 to B3 velocity has tight bell" for
    // TAKEONME-MAIN. Skipped silently when the pack isn't present.
    [Fact]
    public void Vendor_pack_zones_match_their_prose_descriptions()
    {
        var path = FindVendorPack();
        if (path is null)
            return;

        var combis = CombiReader.Read(PcgReader.Parse(File.ReadAllBytes(path)));

        var main = combis.Single(c => c.Name == "TAKEONME-MAIN");
        var theme = main.Timbres[0];
        Assert.Equal(70, theme.BottomKey);          // Bb4 and up
        Assert.Equal("A#4", PcgNotes.Name(theme.BottomKey));
        Assert.Equal(127, theme.TopKey);

        var bell = main.Timbres[5];
        Assert.Equal((54, 59), (bell.BottomKey, bell.TopKey));      // F#3 to B3
        Assert.Equal(("F#3", "B3"), (PcgNotes.Name(bell.BottomKey), PcgNotes.Name(bell.TopKey)));
        Assert.Equal(89, bell.BottomVelocity);      // velocity-switched

        var money = combis.Single(c => c.Name == "MONEY4FREE OPEN");
        Assert.Equal(134.00m, money.Tempo);
        Assert.Equal(16, money.Category);           // matches the vendor's content listing
    }

    [Theory]
    [InlineData(0, "C-1")]
    [InlineData(54, "F#3")]
    [InlineData(60, "C4")]
    [InlineData(81, "A5")]
    [InlineData(127, "G9")]
    public void Note_names_follow_the_hardware_convention(int note, string expected) =>
        Assert.Equal(expected, PcgNotes.Name(note));

    [Fact]
    public void Combi_category_names_cover_the_factory_set()
    {
        Assert.Equal("Keyboard", CombiCategories.Name(0));
        Assert.Equal("LeadSplits", CombiCategories.Name(11));
        Assert.Equal("Drums/Hits", CombiCategories.Name(15));
        Assert.Equal("User 16", CombiCategories.Name(16));
    }

    private static string? FindVendorPack()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var filesDir = Path.Combine(dir.FullName, "files");
            if (Directory.Exists(filesDir))
                return Directory.EnumerateFiles(filesDir, "AUDORA*.PCG", SearchOption.AllDirectories)
                    .FirstOrDefault();
            dir = dir.Parent;
        }
        return null;
    }
}
