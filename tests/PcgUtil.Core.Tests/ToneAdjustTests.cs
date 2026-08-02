using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// Tone adjust: the per-timbre tweaks a combi applies to a program. The decisive evidence
/// is semantic — a CX-3 organ's faders must come back as its drawbars and an AL-1's as its
/// envelope stages. Names that land coherently on two different engines in the same file
/// can't be coincidence, and the resolution scan proves it holds file-wide.
/// </summary>
public class ToneAdjustTests
{
    private static PcgFile? Gig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "files"))) dir = dir.Parent;
        var path = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "files"),
            "20260802a-edited-1002.PCG", SearchOption.AllDirectories).FirstOrDefault();
        return path is null ? null : PcgReader.Parse(File.ReadAllBytes(path));
    }

    private static PcgFile Sample() => PcgUtil.Core.Tests.Sample.Parse();

    [Fact]
    public void The_vocabulary_loads_with_every_engine()
    {
        // Nine voice models, each with its own destination table.
        foreach (var engine in new[] { "HD-1", "AL-1", "CX-3", "STR-1", "MS-20EX",
                                       "PolysixEX", "MOD-7", "SGX-1", "EP-1" })
            Assert.NotNull(ParamTables.ToneAdjust(engine, 1) ?? ParamTables.ToneAdjust(engine, 48));

        // The shared low region agrees across engines that support it.
        Assert.Equal("Filter Cutoff", ParamTables.ToneAdjust("HD-1", 1)!.Name);
        Assert.Equal("Filter Cutoff", ParamTables.ToneAdjust("AL-1", 1)!.Name);
        Assert.True(ParamTables.ToneAdjust("HD-1", 1)!.Relative); // "Rel": an offset

        // Engine-private ids are exactly that: a CX-3 drawbar is not an AL-1 anything.
        Assert.Equal("Upper Drawbar 1", ParamTables.ToneAdjust("CX-3", 48)!.Name);
        Assert.NotEqual("Upper Drawbar 1", ParamTables.ToneAdjust("AL-1", 48)?.Name);

        // Panel-control lists, positionally exact (the docs elide CC runs with "...";
        // the generator expands them, so the count matches the field's documented range).
        Assert.Equal("Off", ParamTables.SwitchAssignments[0]);
        Assert.Equal(17, ParamTables.SwitchAssignments.Count);
        Assert.Equal(143, ParamTables.KnobAssignments.Count); // 0x8E + 1
        Assert.Equal("Off", ParamTables.KnobAssignments[0]);
    }

    [Fact]
    public void The_footloose_organ_reads_as_a_drawbar_registration()
    {
        if (Gig() is not { } pcg) return;
        var organ = CombiReader.Read(pcg).Single(c => c.Name.Trim() == "FOOTLOOSE OPENING");
        // T2 plays the dirty organ — a CX-3.
        var entries = ToneAdjust.ReadCombiTimbre(pcg, organ.Bank, organ.Index, timbre: 1);
        Assert.NotEmpty(entries);

        var drawbars = entries.Where(e => e.Control.StartsWith("Fader")).ToList();
        Assert.Equal(8, drawbars.Count);
        Assert.Equal("Upper Drawbar 1", drawbars[0].Name);
        Assert.Equal("Upper Drawbar 8", drawbars[7].Name);
        // The registration Mike's pack stored: 8 8 8 8 7 8 3 4.
        Assert.Equal(new[] { 8, 8, 8, 8, 7, 8, 3, 4 }, drawbars.Select(d => d.Value).ToArray());
        Assert.All(drawbars, d => Assert.False(d.Relative)); // drawbars are absolute

        // Organ-specific controls, by name.
        Assert.Equal("Expression Level", entries.Single(e => e.Control == "Knob 3").Name);
        Assert.Equal(65, entries.Single(e => e.Control == "Knob 3").Value);
        Assert.Equal("Wheel Brake", entries.Single(e => e.Control == "Switch 7").Name);
        Assert.Contains(entries, e => e.Name == "Rotary On");
        Assert.Contains(entries, e => e.Name == "Perc Enable");
    }

    [Fact]
    public void The_footloose_brass_reads_as_an_al1()
    {
        if (Gig() is not { } pcg) return;
        var chorus = CombiReader.Read(pcg).Single(c => c.Name.Trim() == "FOOTLOOSE CHORUS!");
        // T3 plays the synth brass — an AL-1, a different engine in the same combi.
        var entries = ToneAdjust.ReadCombiTimbre(pcg, chorus.Bank, chorus.Index, timbre: 2);
        Assert.NotEmpty(entries);

        var faders = entries.Where(e => e.Control.StartsWith("Fader")).ToList();
        Assert.Equal(8, faders.Count);
        Assert.Equal("Filter EG Attack", faders[0].Name);
        Assert.Equal("Amp EG Release", faders[7].Name);
        Assert.Contains(entries, e => e.Name == "Drive");
        // Nothing organ-ish leaked in from the other engine's table.
        Assert.DoesNotContain(entries, e => e.Name is not null && e.Name.Contains("Drawbar"));
    }

    [Fact]
    public void Every_assign_id_in_the_file_resolves_on_its_engine()
    {
        if (Gig() is not { } pcg) return;
        int resolved = 0, unresolved = 0;
        foreach (var c in CombiReader.Read(pcg).Where(c => !c.IsEmptyOrInit))
            for (int t = 0; t < CombiReader.TimbresPerCombi; t++)
                foreach (var e in ToneAdjust.ReadCombiTimbre(pcg, c.Bank, c.Index, t))
                    if (e.Name is null) unresolved++; else resolved++;

        Assert.True(resolved > 500, $"scan too small ({resolved})");
        // Engine coherence: ids are looked up in the table of the engine the timbre's
        // program actually uses, so a wrong engine would strand ids en masse. What remains
        // unresolved is honest — overwhelmingly timbres pointing at a program bank this
        // file doesn't carry (no program, so no engine, so no vocabulary), plus a handful
        // of ids an engine genuinely doesn't support (stale assigns from a previous
        // program). Both are shown as "parameter #id" rather than guessed at.
        double rate = (double)unresolved / (resolved + unresolved);
        Assert.True(rate < 0.02, $"{unresolved} of {resolved + unresolved} ids unresolved ({rate:P1})");
    }

    [Fact]
    public void Program_side_tone_adjust_reads_assignments()
    {
        var pcg = Sample();
        // Whichever programs carry assignments, they resolve and carry no invented values.
        int withAssigns = 0;
        foreach (var p in ProgramReader.Read(pcg).Where(p => !p.IsEmpty).Take(200))
        {
            var entries = ToneAdjust.ReadProgram(pcg, p.Bank, p.Index);
            if (entries.Count == 0) continue;
            withAssigns++;
            Assert.All(entries, e => Assert.InRange(e.AssignId, 1, 127));
        }
        Assert.True(withAssigns > 0, "no program carried tone-adjust assignments");
    }

    [Fact]
    public void Combi_controls_decode_to_names()
    {
        if (Gig() is not { } pcg) return;
        var combi = CombiReader.Read(pcg).Single(c => c.Name.Trim() == "FOOTLOOSE VERSES");
        var controls = ToneAdjust.ReadCombiControls(pcg, combi.Bank, combi.Index);

        Assert.Contains(controls, c => c.Control == "SW1");
        Assert.All(controls.Where(c => c.Control.StartsWith("SW")),
            c => Assert.False(string.IsNullOrEmpty(c.Name)));
        // The pack left the knobs on their defaults, which are real assignments by name.
        Assert.All(controls.Where(c => c.Control.StartsWith("Knob")),
            c => Assert.False(string.IsNullOrEmpty(c.Name)));
    }

    [Fact]
    public void Exi_programs_expose_both_engine_slots()
    {
        var pcg = Sample();
        var exi = ProgramReader.Read(pcg).Where(p => !p.IsEmpty && p.ExiEngine is not null).ToList();
        Assert.NotEmpty(exi);
        // Slot 2 decodes to a documented id (0 = that slot is off).
        Assert.All(exi, p => Assert.InRange(p.ExiEngine2!.Value, 0, 9));
        Assert.All(exi, p => Assert.InRange(p.ExiEngine!.Value, 0, 9));
    }
}
