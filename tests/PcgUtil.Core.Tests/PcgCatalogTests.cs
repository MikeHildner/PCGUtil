using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgCatalogTests
{
    [Fact]
    public void Builds_program_and_combi_banks()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());
        Assert.Equal(20, catalog.ProgramBanks.Count);
        Assert.Equal(14, catalog.CombiBanks.Count);
        Assert.All(catalog.ProgramBanks, bank => Assert.Equal(128, bank.Count));
        Assert.All(catalog.CombiBanks, bank => Assert.Equal(128, bank.Count));
    }

    [Fact]
    public void Reads_known_bank_names()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());
        Assert.Equal("Berlin Grand SW2 U.C.", catalog.ProgramBanks[0][0]);
        Assert.Equal("K-Lab: Katja's House", catalog.CombiBanks[0][0]);
    }

    // Resolutions confirmed against the Set List 000 hardware screenshot.
    [Fact]
    public void Resolves_combi_and_program_slot_references()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var setLists = SetListReader.Read(pcg);

        // Slot 0 -> Program INT-A #000 (Berlin Grand)
        Assert.Equal("Berlin Grand SW2 U.C.", catalog.Resolve(setLists[0].Slots[0].Reference));

        // Slot 1 -> Combi USER-A #057 (Let's Go Crazy)
        Assert.Equal("Let's Go Crazy", catalog.Resolve(setLists[0].Slots[1].Reference));

        // SL1 slot 59 -> Program "Power of Synth"
        Assert.Equal("Power of Synth", catalog.Resolve(setLists[1].Slots[59].Reference));
    }

    [Fact]
    public void Resolve_returns_null_for_out_of_range_bank()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());
        var bogus = new SetListReference
        {
            Kind = PcgItemKind.Program,
            Bank = 30,
            Index = 0,
            Raw = Array.Empty<byte>(),
        };
        Assert.Null(catalog.Resolve(bogus));
    }
}
