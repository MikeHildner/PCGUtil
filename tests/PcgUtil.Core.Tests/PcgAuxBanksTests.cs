using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgAuxBanksTests
{
    [Fact]
    public void Catalog_decodes_drum_kit_and_wave_sequence_names()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());

        Assert.Equal(15, catalog.DrumKitBanks.Count);
        Assert.Equal("Trance kit", catalog.DrumKitBanks[0][0]);

        Assert.Equal(15, catalog.WaveSequenceBanks.Count);
        Assert.Equal("19 Orch/Band HITS", catalog.WaveSequenceBanks[0][0]);
    }

    [Fact]
    public void Search_covers_drum_kits_and_wave_sequences()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var setLists = SetListReader.Read(pcg);

        var drumHit = PcgSearch.Find(catalog, setLists, "Trance kit");
        Assert.Contains(drumHit, h => h.Kind == SearchHitKind.DrumKit && h.Name == "Trance kit");

        var waveHit = PcgSearch.Find(catalog, setLists, "Orch/Band");
        Assert.Contains(waveHit, h => h.Kind == SearchHitKind.WaveSequence);
    }

    [Fact]
    public void Combi_contents_report_lists_timbre_programs()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var combis = CombiReader.Read(pcg)
            .Where(c => c.Bank == 7 && !c.IsEmptyOrInit)
            .ToList();

        var html = PcgHtmlReport.CombiContents(PcgBankLabels.Combi(7), combis, catalog);

        Assert.Contains("Combi bank USER-A", html);
        Assert.Contains("If You&#39;re Gone", html);        // a combi in the bank
        Assert.Contains("Guit. If You&#39;re Gone", html);  // its T1 program, resolved
        Assert.Contains("<td>T1</td>", html);
    }
}
