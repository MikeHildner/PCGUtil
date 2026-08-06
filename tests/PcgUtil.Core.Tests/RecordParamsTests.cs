using PcgUtil.Core;
using Xunit;
using Xunit.Abstractions;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The program record tables against real backups. Two proofs carry this feature: the
/// offsets of every field this project had already verified against hardware land exactly
/// where the tables say (so the tables address the record, not some packed area), and the
/// scan — every documented field of every program in every backup decodes inside its
/// documented range. Millions of fields cannot all fall in range by luck.
/// </summary>
public class RecordParamsTests
{
    private readonly ITestOutputHelper _output;

    public RecordParamsTests(ITestOutputHelper output) => _output = output;

    private static string FilesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "files"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "files");
    }

    private static PcgFile? Load(string name)
    {
        var path = Directory.EnumerateFiles(FilesDir(), name, SearchOption.AllDirectories).FirstOrDefault();
        return path is null ? null : PcgReader.Parse(File.ReadAllBytes(path));
    }

    private static List<PcgFile> LoadBackups() =>
        new[] { "save-all.PCG", "20270726b.PCG", "20260802a.PCG" }
            .Select(Load).Where(p => p is not null).Select(p => p!).ToList();

    private static (long Offset, int RecordSize) Locate(PcgFile pcg, int bank, int index)
    {
        var chunk = PcgBankIdentity.CanonicalBanks(pcg, "PRG1")[bank]!;
        int recordSize = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            pcg.Data.AsSpan((int)chunk.DataOffset + 4, 4));
        return (chunk.DataOffset + 12 + (long)index * recordSize, recordSize);
    }

    private static ParamField Field(bool exi, string section, string name) =>
        ParamTables.ProgramSections(exi).Single(t => t.Name == section).Fields.Single(f => f.Name == name);

    [Fact]
    public void The_record_tables_load_and_match_the_documentation()
    {
        var hd1 = ParamTables.ProgramSections(exi: false);
        var exi = ParamTables.ProgramSections(exi: true);
        Assert.Equal(73, hd1.Count);
        Assert.Equal(51, exi.Count);
        Assert.Equal(2501, hd1.Sum(t => t.Fields.Count));
        Assert.Equal(2009, exi.Sum(t => t.Fields.Count));

        // Sections come out in the documentation's order, which starts at the record's head.
        Assert.Equal("Common", hd1[0].Name);
        Assert.Contains(hd1, t => t.Name == "OSC1 Filter EG");
        Assert.Contains(exi, t => t.Name == "Common Step Sequence");
        Assert.DoesNotContain(exi, t => t.Name.StartsWith("OSC", StringComparison.Ordinal));

        // Eight engines, each with the row count its own header declares.
        var cx3 = ParamTables.EngineSections("CX-3");
        Assert.Equal(6, cx3.Count);
        Assert.Equal(151, cx3.Sum(t => t.Fields.Count));
        Assert.Equal(609, ParamTables.EngineSections("AL-1").Sum(t => t.Fields.Count));
        Assert.Equal(1104, ParamTables.EngineSections("MOD-7").Sum(t => t.Fields.Count));
        Assert.Contains(cx3, t => t.Name == "Drawbar");
        Assert.Contains(cx3, t => t.Name == "Rotary Speaker");
        // The product calls engine 8 SGX-2 and the documents call it SGX-1; same engine.
        Assert.NotEmpty(ParamTables.EngineSections("SGX-2"));
    }

    /// <summary>
    /// Every offset this project verified against hardware over the past weeks — by probe,
    /// by factory-corpus correlation, or by a byte-verified edit that loaded on the
    /// instrument — appears in these tables at exactly that offset. That is what says the
    /// tables address the record itself.
    /// </summary>
    [Fact]
    public void The_tables_agree_with_every_offset_we_already_verified()
    {
        Assert.Equal(2568, Field(false, "Common", "Category").Spans[0].Byte);
        Assert.Equal(2565, Field(false, "Common", "Bus Select").Spans[0].Byte);
        Assert.Equal(2586, Field(false, "Tone Adjust", "Knob1 Assign").Spans[0].Byte);
        Assert.Equal(ToneAdjust.TimbreOffset, 54);           // the combi side, for symmetry

        // The effect region: IFX k at 88 + 74k, the masters where the probe put them.
        Assert.Equal(88, Field(false, "Insert Effect1", "Effect Type").Spans[0].Byte);
        Assert.Equal(88 + 74, Field(false, "Insert Effect2", "Effect Type").Spans[0].Byte);
        Assert.Equal(88 + 74 * 11, Field(false, "Insert Effect12", "Effect Type").Spans[0].Byte);
        Assert.Equal(976, Field(false, "Master Effect1", "Effect Type").Spans[0].Byte);
        Assert.Equal(1184, Field(false, "Total Effect2", "Effect Type").Spans[0].Byte);

        // The EXi engine selectors, and the payload regions they choose a table for.
        Assert.Equal(2857, Field(true, "EXi1 Common", "Algorithm Type").Spans[0].Byte);
        Assert.Equal(3909, Field(true, "EXi2 Common", "Algorithm Type").Spans[0].Byte);
        Assert.Equal(new[] { 2908, 3960 }, RecordParams.ExiPayloadOffsets);
    }

    /// <summary>
    /// The same bytes, read two ways: the fields the record tables decode must agree with
    /// the purpose-built readers the app has shipped for months.
    /// </summary>
    [Fact]
    public void Decoded_fields_agree_with_the_readers_the_app_already_ships()
    {
        int programsChecked = 0;
        foreach (var pcg in LoadBackups())
            foreach (var p in ProgramReader.Read(pcg))
            {
                if (p.IsEmpty) continue;
                bool exi = PcgBankIdentity.ProgramBankType(pcg, p.Bank) != ProgramBankType.Hd1;
                var (record, _) = Locate(pcg, p.Bank, p.Index);
                programsChecked++;

                Assert.Equal(p.Category, (int)Field(exi, "Common", "Category").Read(pcg.Data, record));
                for (int k = 0; k < 12; k++)
                    Assert.Equal(p.Effects[k].TypeId,
                        (int)Field(exi, $"Insert Effect{k + 1}", "Effect Type").Read(pcg.Data, record));
                if (exi)
                {
                    Assert.Equal(p.ExiEngine, (int)Field(true, "EXi1 Common", "Algorithm Type").Read(pcg.Data, record));
                    Assert.Equal(p.ExiEngine2, (int)Field(true, "EXi2 Common", "Algorithm Type").Read(pcg.Data, record));
                }
            }
        Assert.True(programsChecked > 1000, $"only {programsChecked} programs cross-checked");
        _output.WriteLine($"cross-checked {programsChecked} programs");
    }

    /// <summary>
    /// The scan, and the proof the tables address the record: every documented, non-constant
    /// field of every program — including the EXi engine payloads read against whichever
    /// engine each slot runs — decodes inside its documented range, apart from three named
    /// field families where the stored value exceeds the range <em>text</em> (characterized
    /// in <see cref="The_out_of_range_values_are_documentation_gaps_not_bad_decoding"/>).
    /// Fifteen million reads cannot land in range by luck; a wrong base or a broken
    /// bit-assembly rule would light up thousands of fields, not three.
    ///
    /// Constants are exempt from the range check: the documentation's own footnote warns
    /// that modulation-source rows written "00(fixed)" really carry a model-specific list.
    /// </summary>
    [Fact]
    public void Every_program_field_in_every_backup_decodes_in_range()
    {
        var (census, programs, fields, engineFields) = ScanCorpus();
        int outOfRange = census.Values.Sum(v => v.Count);
        _output.WriteLine($"{programs} programs, {fields} record fields + {engineFields} engine fields, "
            + $"{outOfRange} out of range");
        foreach (var (key, v) in census.OrderByDescending(c => c.Value.Count))
            _output.WriteLine($"  {key.Section} '{key.Field}' x{v.Count} "
                + $"(up to {v.Highest}, documented {v.Min}..{v.Max})");

        Assert.True(programs > 1000, $"only {programs} programs scanned");
        Assert.True(fields > 2_000_000, $"only {fields} record fields scanned");
        Assert.True(engineFields > 100_000, $"only {engineFields} engine fields scanned");

        var unexplained = census.Keys
            .Where(k => k.Field is not ("Effect Type" or "Switch1 Assign" or "Switch2 Assign")
                        && !k.Field.EndsWith(") Polarity", StringComparison.Ordinal))
            .Select(k => $"{k.Section} '{k.Field}'").ToList();
        Assert.True(unexplained.Count == 0,
            $"{unexplained.Count} unexplained out-of-range fields: " + string.Join(", ", unexplained.Take(8)));
        Assert.True(outOfRange * 10_000L < fields + engineFields,
            $"{outOfRange} of {fields + engineFields} fields out of range — too many to be documentation drift");
    }

    /// <summary>
    /// What the three exceptions are, each bounded by better evidence than the range column:
    /// <list type="bullet">
    /// <item>Effect ids 186–197 are the premium modeled effects these 2.1 documents predate;
    /// the Parameter Guide names every one of them, and no id exceeds that list.</item>
    /// <item>Switch assign 16 is the seventeenth entry of the SW1/2 assignment list these
    /// very documents publish — the range text simply stopped at 15.</item>
    /// <item>Dynamic MIDI Polarity is allocated two bits but has only two values named; a
    /// handful of programs store a third, and it never exceeds those two bits.</item>
    /// </list>
    /// In every case the bytes are read exactly where the tables point, so what is stale is
    /// the documentation's range text, not the decode.
    /// </summary>
    [Fact]
    public void The_out_of_range_values_are_documentation_gaps_not_bad_decoding()
    {
        var (census, _, _, _) = ScanCorpus();
        Assert.Contains(census.Keys, k => k.Field == "Effect Type");
        Assert.Contains(census.Keys, k => k.Field.EndsWith(" Assign", StringComparison.Ordinal));

        foreach (var (key, v) in census)
        {
            long ceiling = key.Field switch
            {
                "Effect Type" => EffectNames.Count - 1,                     // 197, all named
                "Switch1 Assign" or "Switch2 Assign" => ParamTables.SwitchAssignments.Count - 1,
                _ => 3,                                                     // the field's own two bits
            };
            Assert.InRange(v.Highest, v.Max + 1, ceiling);
            Assert.InRange(v.Example, v.Max + 1, ceiling);
            if (key.Field.EndsWith(") Polarity", StringComparison.Ordinal))
                Assert.True(v.Count < 20, $"{key.Field} out of range in {v.Count} programs — no longer a handful");
        }
    }

    private sealed record Violation(int Count, long Example, long Highest, long Min, long Max);

    private static (Dictionary<(string Section, string Field), Violation> Census,
                    int Programs, int Fields, int EngineFields) ScanCorpus()
    {
        var census = new Dictionary<(string, string), Violation>();
        int programs = 0, fields = 0, engineFields = 0;

        foreach (var pcg in LoadBackups())
            foreach (var p in ProgramReader.Read(pcg))
            {
                if (p.IsEmpty) continue;
                bool exi = PcgBankIdentity.ProgramBankType(pcg, p.Bank) != ProgramBankType.Hd1;
                var (record, recordSize) = Locate(pcg, p.Bank, p.Index);
                programs++;

                foreach (var table in ParamTables.ProgramSections(exi))
                    Scan(pcg.Data, record, recordSize, table, table.Name, ref fields);

                if (!exi) continue;
                for (int slot = 0; slot < RecordParams.ExiPayloadOffsets.Length; slot++)
                {
                    int engineId = pcg.Data[record + (slot == 0 ? 2857 : 3909)];
                    if (engineId == 0) continue;
                    string engine = ExiEngines.Name(engineId);
                    long payload = record + RecordParams.ExiPayloadOffsets[slot];
                    int room = recordSize - RecordParams.ExiPayloadOffsets[slot];
                    foreach (var table in ParamTables.EngineSections(engine))
                        Scan(pcg.Data, payload, room, table, $"{engine} {table.Name}", ref engineFields);
                }
            }
        return (census, programs, fields, engineFields);

        void Scan(byte[] data, long baseOffset, int room, ParamTable table, string where, ref int counted)
        {
            foreach (var field in table.Fields)
            {
                if (field.Fixed is not null) continue;
                if (field.Spans.Any(s => s.Byte >= room || baseOffset + s.Byte >= data.Length)) continue;
                long raw = field.Read(data, baseOffset);
                counted++;
                if (field.InRange(raw)) continue;
                census.TryGetValue((where, field.Name), out var prior);
                census[(where, field.Name)] = new Violation(
                    (prior?.Count ?? 0) + 1, prior?.Example ?? raw,
                    Math.Max(prior?.Highest ?? long.MinValue, raw), field.Min, field.Max);
            }
        }
    }

    /// <summary>
    /// A CX-3 program reads out as an organ: nine upper drawbars, each on its documented
    /// 0–8 registration scale, plus the rotary speaker's own controls. This is the same
    /// vocabulary the tone-adjust readout showed for the Footloose organ layer, now read
    /// from the program itself.
    /// </summary>
    [Fact]
    public void A_CX_3_program_reads_out_as_an_organ()
    {
        var pcg = Load("20260802a.PCG") ?? Load("save-all.PCG");
        if (pcg is null) return;

        var organ = ProgramReader.Read(pcg).FirstOrDefault(p => !p.IsEmpty && p.ExiEngine == 3); // CX-3
        Assert.NotNull(organ);
        var sections = RecordParams.ReadProgram(pcg, organ!.Bank, organ.Index);

        var drawbars = sections.Single(s => s.Title.EndsWith("CX-3: Drawbar", StringComparison.Ordinal));
        for (int n = 1; n <= 9; n++)
        {
            var bar = drawbars.Values.Single(v => v.Field.Name == $"Upper Drawbar{n} Level");
            Assert.InRange(bar.Raw, 0, 8);
        }
        Assert.Contains(sections, s => s.Title.EndsWith("CX-3: Rotary Speaker", StringComparison.Ordinal));
        Assert.Contains(sections, s => s.Title.EndsWith("CX-3: Percussion", StringComparison.Ordinal));

        // Common sections come from the EXi record, not the HD-1 one.
        Assert.Contains(sections, s => s.Title == "Common Step Sequence");
        _output.WriteLine($"{organ.Name}: " + string.Join(" ",
            drawbars.Values.Where(v => v.Field.Name.StartsWith("Upper Drawbar", StringComparison.Ordinal))
                           .Select(v => v.Raw)));
    }

    /// <summary>An HD-1 program shows the sections its edit pages do — and no effect slots.</summary>
    [Fact]
    public void An_HD_1_program_shows_its_oscillator_filter_and_amp_sections()
    {
        var pcg = LoadBackups().FirstOrDefault();
        if (pcg is null) return;

        var hd1 = ProgramReader.Read(pcg).First(p => !p.IsEmpty && p.ExiEngine is null);
        var sections = RecordParams.ReadProgram(pcg, hd1.Bank, hd1.Index);
        var titles = sections.Select(s => s.Title).ToList();

        Assert.Contains("OSC1 Filter A", titles);
        Assert.Contains("OSC1 Amplifier EG", titles);
        Assert.Contains("Common LFO", titles);
        Assert.Contains("Pitch EG", titles);
        Assert.DoesNotContain(titles, t => t.StartsWith("Insert Effect", StringComparison.Ordinal));
        Assert.DoesNotContain(titles, t => t == "Tone Adjust");
        // KARMA is bulky and least about the sound, so it sorts last.
        Assert.EndsWith("KARMA Module", titles[^1]);

        var filter = sections.Single(s => s.Title == "OSC1 Filter A");
        Assert.Contains(filter.Values, v => v.Field.Name == "Cutoff");
        Assert.Contains(filter.Values, v => v.Field.Name == "Resonance");
        Assert.All(filter.Values, v => Assert.False(string.IsNullOrEmpty(v.Display)));
    }
}
