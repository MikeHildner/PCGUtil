using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class ProgramReaderTests
{
    // Category/sub-category pack into byte 2568 (bits 0–4 / 5–7) and the EXi engine id
    // sits at 2857 — both located by correlating the published voice name list against
    // the factory banks (768/768 and 640/640 exact matches).
    [Fact]
    public void Factory_program_pins_match_the_published_voice_name_list()
    {
        var programs = ProgramReader.Read(Sample.Parse());

        // INT-A 000 "Berlin Grand SW2 U.C." — Keyboard / A.Piano, SGX-2 piano engine.
        var berlin = programs.Single(p => p.Bank == 0 && p.Index == 0);
        Assert.StartsWith("Berlin Grand", berlin.Name);
        Assert.Equal(0, berlin.Category);
        Assert.Equal("Keyboard", ProgramCategories.Name(berlin.Category));
        Assert.Equal(0, berlin.SubCategory);
        Assert.Equal(8, berlin.ExiEngine);
        Assert.Equal("SGX-2", ExiEngines.Name(berlin.ExiEngine!.Value));

        // INT-A 040 "KE My Sound (Amp1)" — Organ on the CX-3 engine.
        var organ = programs.Single(p => p.Bank == 0 && p.Index == 40);
        Assert.Equal(1, organ.Category);
        Assert.Equal(3, organ.ExiEngine);
        Assert.Equal("CX-3", ExiEngines.Name(organ.ExiEngine!.Value));

        // INT-B 000 "De La Salsa Brass EXs18" — Brass, an HD-1 bank (no engine id).
        var brass = programs.Single(p => p.Bank == 1 && p.Index == 0);
        Assert.Equal(5, brass.Category);
        Assert.Equal("Brass", ProgramCategories.Name(brass.Category));
        Assert.Null(brass.ExiEngine);

        // INT-C 059 "Harpsichord 1 STR-1" — Keyboard / Clav-Harpsi on STR-1.
        var harpsi = programs.Single(p => p.Bank == 2 && p.Index == 59);
        Assert.Equal(0, harpsi.Category);
        Assert.Equal(3, harpsi.SubCategory);
        Assert.Equal(4, harpsi.ExiEngine);
    }

    // A wrong offset shows up immediately as out-of-range categories or engine ids.
    [Fact]
    public void Program_metadata_decodes_within_hardware_ranges()
    {
        var programs = ProgramReader.Read(Sample.Parse());
        Assert.True(programs.Count > 1000, $"expected many programs, got {programs.Count}");

        foreach (var p in programs.Where(p => !p.IsEmpty))
        {
            Assert.InRange(p.Category, 0, 17); // 16–17 = user-assignable, same as combis
            Assert.InRange(p.SubCategory, 0, 7);
            if (p.ExiEngine is { } engine)
                Assert.InRange(engine, 0, 15);
        }
    }

    [Fact]
    public void Exi_engine_is_read_only_from_exi_banks()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var programs = ProgramReader.Read(pcg);

        foreach (var p in programs)
        {
            var type = catalog.ProgramBankTypes[p.Bank];
            if (type == ProgramBankType.Exi)
                Assert.NotNull(p.ExiEngine);
            else
                Assert.Null(p.ExiEngine);
        }
    }

    [Fact]
    public void Category_and_engine_names_cover_their_tables()
    {
        Assert.Equal("Keyboard", ProgramCategories.Name(0));
        Assert.Equal("Bass/Synth Bass", ProgramCategories.Name(8));
        Assert.Equal("Drums", ProgramCategories.Name(15));
        Assert.Equal("User 16", ProgramCategories.Name(16));

        Assert.Equal("AL-1", ExiEngines.Name(2));
        Assert.Equal("SGX-2", ExiEngines.Name(8));
        Assert.Equal("EP-1", ExiEngines.Name(9));
        Assert.Equal("EXi 1", ExiEngines.Name(1));
    }
}
