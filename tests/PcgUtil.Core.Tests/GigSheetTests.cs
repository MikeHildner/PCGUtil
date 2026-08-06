using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The gig sheet: what a player is told about one Set List slot. Two things carry these
/// tests. The structural pass builds a sheet for every named slot of every set list in the
/// sample — the net that catches a slot pointing at a bank the file doesn't carry. The
/// pinned pass reads the real Footloose combi, where the interesting facts are that only
/// three of sixteen timbres sound and that the joystick, not a switch, runs the rotary.
/// </summary>
public class GigSheetTests
{
    private static GigSheet SheetFor(PcgFile pcg, int setList, int slot)
    {
        var catalog = PcgCatalog.Build(pcg);
        var list = SetListReader.Read(pcg)[setList];
        return GigSheet.Build(pcg, catalog, list, list.Slots[slot]);
    }

    [Fact]
    public void Every_named_slot_in_the_sample_builds_a_sheet()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var combis = CombiReader.Read(pcg);
        var programs = ProgramReader.Read(pcg);
        int built = 0;

        foreach (var list in SetListReader.Read(pcg))
            foreach (var slot in list.NamedSlots)
            {
                var sheet = GigSheet.Build(pcg, catalog, list, slot, combis, programs);
                built++;

                // Nothing lost and nothing counted twice when effects are sorted into chains.
                var inserts = sheet.Inserts.ToList();
                Assert.Equal(inserts.Count, inserts.Select(e => e.Label).Distinct().Count());
                foreach (var chain in sheet.Chains)
                {
                    Assert.True(chain.Steps.Count > 1, "a chain of one is just an effect");
                    var order = chain.Steps.Select(s => s.Label).ToList();
                    Assert.Equal(order, order.Distinct());
                }
                // A layer either sounds or says why not — never both, never neither.
                foreach (var layer in sheet.AllLayers)
                    Assert.Equal(layer.Sounds, layer.SilentReason is null);
            }

        Assert.True(built > 200, $"only {built} slots built");
    }

    [Fact]
    public void The_footloose_chorus_reads_the_way_the_instrument_plays_it()
    {
        if (GigFile.Parse() is not { } pcg) return;
        var sheet = SheetFor(pcg, 0, 18);

        Assert.Equal("Footloose Chorus", sheet.Slot.Name);
        Assert.Equal("PART TIME GENIUS REPRISE", sheet.List.DisplayName);
        Assert.Equal("FOOTLOOSE CHORUS!", sheet.TargetName);
        Assert.Null(sheet.Unavailable);
        Assert.Equal(0, sheet.GlobalMidiChannel);   // the keyboard plays on channel 1

        // Three layers sound; the other thirteen are a grand piano parked on channels 4-16.
        Assert.Equal(3, sheet.Layers.Count);
        Assert.Equal(13, sheet.Silent.Count);
        Assert.All(sheet.Silent, s => Assert.Contains("MIDI channel", s.SilentReason!, StringComparison.Ordinal));

        var organ = sheet.Layers[0];
        Assert.Equal("FOOTLOOSE-DIRTYORGAN", organ.Program);
        Assert.Equal("CX-3", organ.Engine);
        Assert.Equal("C-1–A4", organ.KeyRange);
        Assert.Equal(24, organ.Transpose);
        Assert.Equal("8 8 8 8 7 8 3 4 8", organ.Drawbars);

        var brass = sheet.Layers[2];
        Assert.Equal("FOOTLOOSE-SYNTHBRASS", brass.Program);
        Assert.Equal("AL-1", brass.Engine);
        Assert.Equal("A#4–G9", brass.KeyRange);
        Assert.Equal(-12, brass.Transpose);
    }

    [Fact]
    public void The_effect_chain_comes_out_in_signal_order()
    {
        if (GigFile.Parse() is not { } pcg) return;
        var sheet = SheetFor(pcg, 0, 18);

        var chain = Assert.Single(sheet.Chains);
        Assert.Equal(new[] { "IFX1", "IFX2", "IFX3", "IFX4", "IFX5", "IFX6" },
            chain.Steps.Select(s => s.Label));
        Assert.Contains("Rotary Speaker", chain.Steps[2].TypeName, StringComparison.Ordinal);

        var alone = Assert.Single(sheet.Standalone);
        Assert.Equal("IFX7", alone.Label);
        Assert.All(sheet.Inserts, e => Assert.True(e.IsOn));
    }

    /// <summary>
    /// The question the feature exists to answer. In this patch the switches do nothing and
    /// the joystick runs the rotary speaker — a fact that lives in the program, not in any
    /// effect, and one no amount of staring at the combi's effect list would reveal.
    /// </summary>
    [Fact]
    public void The_controls_say_what_actually_moves_the_sound()
    {
        if (GigFile.Parse() is not { } pcg) return;
        var sheet = SheetFor(pcg, 0, 18);

        var up = Assert.Single(sheet.Controls, c => c.Source == "Joystick up");
        Assert.Contains("Rotary Speed", up.Moves, StringComparison.Ordinal);
        var down = Assert.Single(sheet.Controls, c => c.Source == "Joystick down");
        Assert.Contains("Wheel Brake", down.Moves, StringComparison.Ordinal);
        Assert.Contains(sheet.Controls, c => c.Source == "Foot pedal"
            && c.Moves.Contains("Expression", StringComparison.Ordinal));

        // The vector joystick is a different stick from the one that runs the rotary.
        Assert.Contains(sheet.Controls, c => c.Source == "Vector joystick");

        // Assigned but idle: said once, plainly, rather than left as a silent gap.
        Assert.Contains(sheet.Controls, c => c.Source.Contains("SW1", StringComparison.Ordinal)
            && c.Moves.Contains("nothing in this patch responds", StringComparison.Ordinal));

        // Never the sound's own movers — an LFO or an envelope is not something a player holds.
        Assert.DoesNotContain(sheet.Controls, c => c.Source.Contains("LFO", StringComparison.OrdinalIgnoreCase)
            || c.Source.Contains("EG", StringComparison.Ordinal)
            || c.Source.Contains("Velocity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_slot_that_loads_a_song_says_where_the_song_lives()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        foreach (var list in SetListReader.Read(pcg))
            foreach (var slot in list.NamedSlots.Where(s => s.Reference.Kind == PcgItemKind.Song))
            {
                var sheet = GigSheet.Build(pcg, catalog, list, slot);
                Assert.NotNull(sheet.Unavailable);
                Assert.Contains(".SNG", sheet.Unavailable!, StringComparison.Ordinal);
                Assert.Empty(sheet.Layers);
                return;
            }
    }

    [Fact]
    public void Modulation_sources_are_named_per_engine()
    {
        // The same number means different things on different engines: on an AL-1 id 50 is
        // the vector joystick, on a CX-3 that id is unused. A single shared list would put
        // the wrong control on the page.
        Assert.Equal(78, ParamTables.AmsSources("AL-1").Count);
        Assert.Equal(78, ParamTables.AmsSources("CX-3").Count);
        Assert.Equal("Vector JS+Y (CC#87)", ParamTables.AmsSource("AL-1", 50));
        Assert.Null(ParamTables.AmsSource("CX-3", 50));

        Assert.Equal("SW1 (CC#80)", ParamTables.AmsSource("CX-3", 32));
        Assert.Equal("JS+Y (CC#1)", ParamTables.AmsSource("CX-3", 13));
        Assert.Null(ParamTables.AmsSource("CX-3", 0));            // "Off" is not a control
        Assert.Empty(ParamTables.AmsSources("no such engine"));

        // The product's SGX-2 is the documents' SGX-1, aliased like the tone-adjust tables.
        Assert.NotEmpty(ParamTables.AmsSources("SGX-2"));
    }
}
