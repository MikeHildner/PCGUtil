using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The effect parameter engine against real backups. The decisive test is the scan:
/// every used effect slot in every record must decode with every field inside its
/// documented range. At ~19 fields per used slot across thousands of slots, a wrong
/// parameter base or a broken bit-assembly rule cannot survive it — this is the same
/// statistical method that located the effect region in the first place.
/// </summary>
public class EffectParamsTests
{
    private static List<PcgFile> LoadBackups()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "files"))) dir = dir.Parent;
        string files = Path.Combine(dir!.FullName, "files");
        return new[] { "save-all.PCG", "20270726b.PCG" }
            .Select(n => Directory.EnumerateFiles(files, n, SearchOption.AllDirectories).FirstOrDefault())
            .Where(p => p is not null)
            .Select(p => PcgReader.Parse(File.ReadAllBytes(p!)))
            .ToList();
    }

    private static (long Offset, int RecordSize) Locate(PcgFile pcg, string sectionId, int bank, int index)
    {
        var chunk = PcgBankIdentity.CanonicalBanks(pcg, sectionId)[bank]!;
        int recordSize = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            pcg.Data.AsSpan((int)chunk.DataOffset + 4, 4));
        return (chunk.DataOffset + 12 + (long)index * recordSize, recordSize);
    }

    [Fact]
    public void The_tables_load_and_look_like_the_documentation()
    {
        // 185 real tables (type 0 is "No Effect"), the biggest spanning 57 bytes.
        Assert.Null(EffectParams.TableFor(0));
        var chorus = EffectParams.TableFor(40);
        Assert.NotNull(chorus);
        Assert.Equal("Stereo Chorus", chorus!.Name);
        var biggest = Enumerable.Range(1, 185).Select(ParamTables.Effect).Max(t => t!.Size);
        Assert.Equal(57, biggest);
        Assert.Equal(38, ParamTables.DmodSources.Count);
        Assert.Equal("Off", ParamTables.DmodSources[0]);
        Assert.Equal("Tempo", ParamTables.DmodSources[^1]);
    }

    /// <summary>
    /// The probe that cracked the geometry, pinned forever: Stereo Dyna Compressor on
    /// IFX1 of combi USER-G 001 with Wet/Dry 37, Sensitivity 99, Attack 61, Output Level
    /// 73, Trim 100 dialed on the instrument's own panel. If this decodes, the
    /// params-before-header rule and the packed bit-stream are anchored to hardware
    /// ground truth, not statistics.
    /// </summary>
    [Fact]
    public void The_hardware_probe_decodes_exactly()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "files"))) dir = dir.Parent;
        var path = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "files"), "probe-fx-a.PCG",
            SearchOption.AllDirectories).FirstOrDefault();
        if (path is null) return; // probe file not present in this checkout
        var pcg = PcgReader.Parse(File.ReadAllBytes(path));

        var combi = CombiReader.Read(pcg).Single(c => c.Bank == 13 && c.Index == 1); // USER-G 001
        Assert.Equal(1, combi.Effects[0].TypeId); // Stereo Dyna Compressor

        var (record, _) = Locate(pcg, "CMB1", 13, 1);
        var values = EffectParams.Read(pcg.Data, record, combi.Effects[0])!;
        long Of(string name) => values.Single(v => v.Field.Name == name).Raw;

        Assert.Equal(37, Of("Wet/Dry"));
        Assert.Equal(99, Of("Sensitivity"));
        Assert.Equal(61, Of("Attack"));
        Assert.Equal(73, Of("Output Level"));
        Assert.Equal(0, Of("Wet/Dry Mod.Source"));
        Assert.Equal(0, Of("Pre LEQ Gain"));
    }

    /// <summary>
    /// The zero-violation scan: every used effect slot in both real backups — all sixteen
    /// slots including TFX2 — decodes with every field inside its documented range. This
    /// held at exactly 0 of 299,638 when the geometry landed; any regression here means
    /// the tables, the reader, or the bases drifted.
    /// </summary>
    [Fact]
    public void Every_used_effect_slot_in_both_backups_decodes_in_range()
    {
        int slotsChecked = 0, fieldsChecked = 0;
        foreach (var pcg in LoadBackups())
            foreach (var (record, effects) in AllEffectRecords(pcg))
                foreach (var e in effects)
                {
                    if (!e.HasEffect) continue;
                    var values = EffectParams.Read(pcg.Data, record, e);
                    if (values is null) continue;
                    slotsChecked++;
                    foreach (var v in values)
                    {
                        fieldsChecked++;
                        Assert.True(v.Field.InRange(v.Raw),
                            $"{e.Label} type {e.TypeId} '{v.Field.Name}' = {v.Raw} outside "
                            + $"{v.Field.Min}..{v.Field.Max} at record {record}");
                    }
                }
        Assert.True(slotsChecked > 2000, $"only {slotsChecked} slots scanned");
        Assert.True(fieldsChecked > 100_000, $"only {fieldsChecked} fields scanned");
    }

    [Fact]
    public void Katjas_house_ifx1_reads_as_a_real_stereo_chorus()
    {
        var pcg = LoadBackups().First();
        var combi = CombiReader.Read(pcg).Single(c => c.Bank == 0 && c.Index == 0); // INT-A 000
        Assert.Equal(40, combi.Effects[0].TypeId); // §10-confirmed: IFX1 = Stereo Chorus

        var (record, _) = Locate(pcg, "CMB1", 0, 0);
        var values = EffectParams.Read(pcg.Data, record, combi.Effects[0])!;

        Assert.Contains(values, v => v.Field.Name == "Wet/Dry");
        Assert.Contains(values, v => v.Field.Name.Contains("LFO Freq"));
        Assert.All(values, v => Assert.True(v.Field.InRange(v.Raw)));
    }

    [Fact]
    public void Signed_and_scaled_fields_format_correctly()
    {
        // Stereo Dyna Compressor's Pre LEQ Gain: raw -30..30 displayed -15.0..+15.0.
        var table = EffectParams.TableFor(1)!;
        var gain = table.Fields.Single(f => f.Name == "Pre LEQ Gain");
        Assert.True(gain.Signed);
        Assert.Equal(-30, gain.Min);
        Assert.Equal(30, gain.Max);
        Assert.Equal("-7.5", EffectParams.Format(gain, -15));
        Assert.Equal("0.0", EffectParams.Format(gain, 0));

        // A modulation source renders by name from the Dmod list.
        var source = table.Fields.Single(f => f.Name == "Wet/Dry Mod.Source");
        Assert.Equal("Off", EffectParams.Format(source, 0));
        Assert.Equal("Tempo", EffectParams.Format(source, 37));
    }

    [Fact]
    public void Multi_span_fields_assemble_lsb_first()
    {
        // Stereo Dyna Compressor's Wet/Dry Mod.Intensity spans byte1[7:6] + byte2[5:0]:
        // the first span is the value's low bits. Build a synthetic parameter area
        // holding -100 (0x9C = 0b10011100): low 2 bits (00) into byte1[7:6], high 6
        // bits (100111) into byte2[5:0].
        var table = EffectParams.TableFor(1)!;
        var intensity = table.Fields.Single(f => f.Name == "Wet/Dry Mod.Intensity");
        Assert.Equal(2, intensity.Spans.Count);

        var area = new byte[64];
        area[1] = 0b00_000000;
        area[2] = 0b00_100111;
        Assert.Equal(-100, intensity.Read(area, 0));

        // +100 = 0b01100100: low 2 bits (00) into byte1[7:6], high 6 (011001) into byte2.
        area[1] = 0b00_000000;
        area[2] = 0b00_011001;
        Assert.Equal(100, intensity.Read(area, 0));
    }

    // Every record that carries the shared effect region: all combis and all programs.
    private static IEnumerable<(long Record, IReadOnlyList<CombiEffect> Effects)> AllEffectRecords(PcgFile pcg)
    {
        foreach (var c in CombiReader.Read(pcg))
        {
            var (record, _) = Locate(pcg, "CMB1", c.Bank, c.Index);
            yield return (record, c.Effects);
        }
        foreach (var p in ProgramReader.Read(pcg))
        {
            if (p.IsEmpty) continue;
            var (record, _) = Locate(pcg, "PRG1", p.Bank, p.Index);
            yield return (record, p.Effects);
        }
    }
}
