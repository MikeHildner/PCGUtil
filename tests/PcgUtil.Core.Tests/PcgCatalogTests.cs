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
    public void Resolve_returns_null_for_unmapped_program_bank_id()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());
        // PcgId 7 falls in the gap with no in-file program bank (GM=6, 7..16 unused).
        var bogus = new SetListReference
        {
            Kind = PcgItemKind.Program,
            Bank = 7,
            Index = 0,
            Raw = Array.Empty<byte>(),
        };
        Assert.Null(catalog.Resolve(bogus));
    }

    // A program reference's bank byte is a hardware PcgId, not a list index: U-A is PcgId 17
    // but the 7th program bank (list index 6). Direct indexing used to mis-resolve these.
    [Fact]
    public void Resolves_user_bank_program_via_pcgid()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());
        const int uaListIndex = 6, uaPcgId = 17;

        var bank = catalog.ProgramBanks[uaListIndex];
        int number = 0;
        while (number < bank.Count && string.IsNullOrEmpty(bank[number])) number++;
        Assert.True(number < bank.Count, "U-A has no named programs to test with");

        var reference = new SetListReference
        {
            Kind = PcgItemKind.Program,
            Bank = uaPcgId,
            Index = number,
            Raw = Array.Empty<byte>(),
        };
        Assert.Equal(bank[number], catalog.Resolve(reference));
        Assert.Equal(catalog.ResolveProgram(uaPcgId, number), catalog.Resolve(reference));
    }
}
