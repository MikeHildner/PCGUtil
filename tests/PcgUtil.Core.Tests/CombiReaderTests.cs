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
}
