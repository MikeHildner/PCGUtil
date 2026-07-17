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
        if (VendorPack.Parse() is not { } pack)
            return;

        var combis = CombiReader.Read(pack);

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

    // GE selects live at LE16 offsets 1814/2558/3302/4046 — every combi must decode four
    // ids inside the hardware's flat 0..3583 space (2048 presets + 12×128 user).
    [Fact]
    public void Karma_ge_ids_decode_within_hardware_range()
    {
        var combis = CombiReader.Read(Sample.Parse());
        Assert.All(combis, c => Assert.Equal(4, c.KarmaGeIds.Count));
        Assert.All(combis, c => Assert.All(c.KarmaGeIds, id => Assert.InRange(id, 0, 3583)));
    }

    // The vendor pack's "MONEY4FREE OPEN" drives KARMA with its MIDI-converted user GE
    // U-A 096 — the byte-level link between the .PCG and its companion .KGE.
    [Fact]
    public void Vendor_pack_combi_references_its_user_ge()
    {
        if (VendorPack.Parse() is not { } pack)
            return;

        var money = CombiReader.Read(pack).Single(c => c.Name == "MONEY4FREE OPEN");
        Assert.Contains(Combi.KarmaUserGeBase + 96, money.KarmaGeIds);
        Assert.True(money.UsesUserKarmaGes);
    }

    [Theory]
    [InlineData(0, "preset 0000")]
    [InlineData(2047, "preset 2047")]
    [InlineData(2144, "USER-A 096")]
    [InlineData(3583, "USER-L 127")]
    public void Karma_ge_labels(int geId, string expected) =>
        Assert.Equal(expected, Combi.KarmaGeLabel(geId));

    // Effect slots live at 88+74k (12 inserts) and 976/1044/1116/1184 (MFX/TFX). A wrong
    // offset would immediately put type bytes outside the hardware's 0..197 effect list.
    [Fact]
    public void Effect_types_decode_within_hardware_range_on_every_combi()
    {
        var combis = CombiReader.Read(Sample.Parse());
        Assert.All(combis, c => Assert.Equal(16, c.Effects.Count));
        Assert.All(combis, c => Assert.All(c.Effects, e => Assert.InRange(e.TypeId, 0, 197)));
    }

    // Factory INT-A pins from byte forensics (hardware-verified checklist section 10):
    // #0 "K-Lab: Katja's House" runs a full insert chain into a Reverb Hall master;
    // #23 "Metal Morphosis" uses a single insert. Numbers are raw record bytes.
    [Fact]
    public void Factory_combi_effect_pins()
    {
        var combis = CombiReader.Read(Sample.Parse());

        var katja = combis.Single(c => c.Bank == 0 && c.Index == 0);
        Assert.Equal("K-Lab: Katja's House", katja.Name);
        Assert.Equal(40, katja.Effects[0].TypeId);   // IFX1
        Assert.True(katja.Effects[0].IsOn);
        Assert.Equal(97, katja.Effects[1].TypeId);   // IFX2
        Assert.All(katja.Effects.Skip(7).Take(5), e => Assert.False(e.HasEffect)); // IFX8-12 empty
        Assert.Equal(93, katja.Effects[12].TypeId);  // MFX1
        Assert.Equal(101, katja.Effects[13].TypeId); // MFX2 — Reverb Hall
        Assert.Equal(13, katja.Effects[14].TypeId);  // TFX1
        Assert.Equal(8, katja.Effects[15].TypeId);   // TFX2

        var metal = combis.Single(c => c.Bank == 0 && c.Index == 23);
        Assert.Equal("Metal Morphosis", metal.Name);
        Assert.Equal(56, metal.Effects[0].TypeId);
        Assert.True(metal.Effects[0].IsOn);
        Assert.All(metal.Effects.Skip(1).Take(11), e => Assert.False(e.HasEffect)); // IFX2-12 empty
        Assert.Equal(100, metal.Effects[13].TypeId); // MFX2 — Overb
    }

    [Fact]
    public void Init_combis_carry_no_effects()
    {
        var combis = CombiReader.Read(Sample.Parse());
        var init = combis.First(c => c.Bank == 4 && c.IsEmptyOrInit);
        Assert.All(init.Effects, e => Assert.False(e.HasEffect));
        Assert.All(init.Effects, e => Assert.False(e.IsOn));
    }

    [Fact]
    public void Effect_slot_labels_follow_the_hardware()
    {
        var combis = CombiReader.Read(Sample.Parse());
        var labels = combis[0].Effects.Select(e => e.Label).ToArray();
        Assert.Equal("IFX1", labels[0]);
        Assert.Equal("IFX12", labels[11]);
        Assert.Equal("MFX1", labels[12]);
        Assert.Equal("MFX2", labels[13]);
        Assert.Equal("TFX1", labels[14]);
        Assert.Equal("TFX2", labels[15]);
    }

    // A module with GE 0 reads as off — INT-A #3 "Angels Watching" drives only module A.
    [Fact]
    public void Karma_modules_report_on_off_per_module()
    {
        var combis = CombiReader.Read(Sample.Parse());
        var angels = combis.Single(c => c.Bank == 0 && c.Index == 3);
        Assert.Equal("Angels Watching", angels.Name);

        var modules = angels.KarmaModules;
        Assert.Equal(4, modules.Count);
        Assert.Equal(new[] { "A", "B", "C", "D" }, modules.Select(m => m.Label));
        Assert.Equal(727, modules[0].GeId);
        Assert.True(modules[0].IsOn);
        Assert.All(modules.Skip(1), m => Assert.False(m.IsOn));
    }

    [Fact]
    public void Effect_names_cover_the_published_list()
    {
        Assert.Equal(198, EffectNames.Count);
        Assert.Equal("No Effect", EffectNames.Name(0));
        Assert.Equal("Stereo Chorus", EffectNames.Name(40));
        Assert.Equal("Overb", EffectNames.Name(100));
        Assert.Equal("Reverb Hall", EffectNames.Name(101));
        Assert.Equal("Rotary Speaker Pro CX Custom", EffectNames.Name(197));
        Assert.Equal("Effect 198", EffectNames.Name(198)); // out of range: number still shows
    }
}
