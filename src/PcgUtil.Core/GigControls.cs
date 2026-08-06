using System.Runtime.CompilerServices;

namespace PcgUtil.Core;

/// <summary>
/// Works out what a player can move while playing, and what it moves.
///
/// A patch is full of modulation routings, but most sources move by themselves — envelopes,
/// LFOs, step sequencers, AMS mixers. Those belong to the sound. What belongs on a gig sheet
/// is the handful a pair of hands can reach: the joystick, the pedals, the switches, the
/// knobs, the ribbon and the slider. So this deliberately keeps a <em>whitelist</em> of
/// physical controls rather than filtering out a blacklist of internal ones — a new engine
/// with a new internal source should never leak onto the page.
///
/// The routings come from three places and are answered in the player's terms: the combi's
/// own SW1/SW2 and knob assignments, each effect's modulation sources, and each sounding
/// layer's program parameters. That last one is where the interesting answers hide — in a
/// dirty-organ patch the joystick is what runs the rotary speaker, and no effect mentions it.
/// </summary>
public static class GigControls
{
    private const int MaxSources = 8;
    private const int MaxPerSource = 3;

    public static IReadOnlyList<GigControl> Build(PcgFile pcg, Combi combi,
                                                  IReadOnlyList<GigLayer> layers,
                                                  IReadOnlyList<ProgramInfo> programs)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        ArgumentNullException.ThrowIfNull(combi);
        ArgumentNullException.ThrowIfNull(layers);

        var found = new List<(string Source, string Moves, string Where)>();

        foreach (var e in combi.Effects.Where(e => e.HasEffect))
        {
            IReadOnlyList<ParamValue>? values;
            try { values = EffectParams.ReadCombi(pcg, combi.Bank, combi.Index, e.Slot); }
            catch (Exception) { continue; }
            if (values is null) continue;
            foreach (var v in values)
                if (Destination(v.Field.Name) is { } moves && Source(v.Display) is { } source)
                    found.Add((source, moves, $"{e.Label} {e.TypeName}"));
        }

        // Only the layers that actually sound: decoding a program record is thousands of
        // field reads, and a silent timbre's routings are not the player's problem.
        foreach (var layer in layers)
        {
            var timbre = combi.Timbres.FirstOrDefault(t => t.Index + 1 == layer.Number);
            if (timbre is null) continue;
            int bank = PcgCatalog.ProgramBankIndexForPcgId(timbre.ProgramBankPcgId);
            if (bank < 0) continue;
            var info = programs.FirstOrDefault(p => p.Bank == bank && p.Index == timbre.ProgramNumber);
            if (info is null) continue;
            foreach (var (moves, source) in ProgramRoutings(pcg, bank, timbre.ProgramNumber, Engines(info)))
                found.Add((source, moves, $"T{layer.Number} {layer.Program}"));
        }

        return Summarize(found, CombiAssignments(pcg, combi));
    }

    /// <summary>The same list for a slot that loads a program directly.</summary>
    public static IReadOnlyList<GigControl> ForProgram(PcgFile pcg, int bank, int index,
                                                       IReadOnlyList<string> engines, string name)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var found = ProgramRoutings(pcg, bank, index, engines)
            .Select(r => (r.Source, r.Moves, Where: name)).ToList();
        return Summarize(found, Array.Empty<(string, string)>());
    }

    // A combi's SW1/SW2 and assignable knobs say what the control SENDS; whether anything
    // listens is the question the routings above answer. Both halves matter: "SW1 is
    // assigned but nothing uses it" is worth knowing before a gig, not during one.
    private static IReadOnlyList<(string Source, string Sends)> CombiAssignments(PcgFile pcg, Combi combi)
    {
        try
        {
            return ToneAdjust.ReadCombiControls(pcg, combi.Bank, combi.Index)
                .Select(c => (Source(c.Label) ?? c.Control, c.Label))
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static IReadOnlyList<GigControl> Summarize(
        List<(string Source, string Moves, string Where)> found,
        IReadOnlyList<(string Source, string Sends)> assigned)
    {
        var controls = new List<GigControl>();
        foreach (var group in found.GroupBy(f => f.Source)
                                   .OrderBy(g => SourceOrder(g.Key))
                                   .ThenBy(g => g.Key, StringComparer.Ordinal)
                                   .Take(MaxSources))
        {
            // Keep each destination tied to where it lives — two layers of the same organ
            // share a routing, and collapsing them loses which layer is which.
            var byMove = group.GroupBy(g => g.Moves)
                              .Select(m => (Move: m.Key, Where: m.Select(x => Short(x.Where)).Distinct().ToList()))
                              .ToList();
            string moves = string.Join(", ", byMove.Take(MaxPerSource)
                .Select(m => m.Where.Count == 0 ? m.Move : $"{m.Move} ({string.Join('/', m.Where)})"));
            if (byMove.Count > MaxPerSource) moves += $", +{byMove.Count - MaxPerSource} more";
            controls.Add(new GigControl(group.Key, moves,
                string.Join(", ", group.Select(g => g.Where).Distinct().Take(2))));
        }

        // Controls the combi assigns that nothing actually listens to. One line for the lot:
        // worth knowing before a gig, not worth four rows of the page.
        var idle = assigned.Select(a => a.Source).Distinct()
                           .Where(s => !controls.Any(c => c.Source == s)).ToList();
        if (idle.Count > 0)
            controls.Add(new GigControl(string.Join(", ", idle),
                "assigned, but nothing in this patch responds to them", ""));

        return controls;
    }

    /// <summary>"T1 FOOTLOOSE-DIRTYORGAN" → "T1"; an effect keeps its slot label.</summary>
    private static string Short(string where)
    {
        int space = where.IndexOf(' ');
        return space > 0 ? where[..space] : where;
    }

    private static IEnumerable<(string Moves, string Source)> ProgramRoutings(
        PcgFile pcg, int bank, int index, IReadOnlyList<string> engines)
    {
        foreach (var (section, field, raw) in Routings(pcg, bank, index))
        {
            if (Destination(field) is not { } moves) continue;
            string engine = section.StartsWith("EXi2 ", StringComparison.Ordinal)
                ? engines.ElementAtOrDefault(1) ?? "" : engines.ElementAtOrDefault(0) ?? "";
            string? name = ParamTables.AmsSource(engine, (int)raw);
            if (Source(name) is { } source) yield return (moves, source);
        }
    }

    // Decoding a program is expensive and a busy set list asks for the same programs over and
    // over, so keep the modulation rows per file image — the same trick that took the tone
    // adjust scan from 28 seconds to under one.
    private static readonly ConditionalWeakTable<byte[],
        Dictionary<(int Bank, int Index), List<(string Section, string Field, long Raw)>>> _cache = new();

    private static List<(string Section, string Field, long Raw)> Routings(PcgFile pcg, int bank, int index)
    {
        var perFile = _cache.GetValue(pcg.Data, _ => new());
        lock (perFile)
        {
            if (perFile.TryGetValue((bank, index), out var cached)) return cached;
            var rows = new List<(string, string, long)>();
            try
            {
                foreach (var section in RecordParams.ReadProgram(pcg, bank, index))
                    foreach (var v in section.Values)
                        if (v.Raw != 0 && v.Field.Name.EndsWith("Mod.Source", StringComparison.Ordinal))
                            rows.Add((section.Title, v.Field.Name, v.Raw));
            }
            catch (Exception)
            {
                // A bank this file doesn't carry: no routings rather than no sheet.
            }
            perFile[(bank, index)] = rows;
            return rows;
        }
    }

    private static IReadOnlyList<string> Engines(ProgramInfo info) =>
        info.ExiEngine is { } e and > 0
            ? new[] { ExiEngines.Name(e), info.ExiEngine2 is { } e2 and > 0 ? ExiEngines.Name(e2) : "" }
            : new[] { "HD-1", "" };

    /// <summary>
    /// What a modulation field controls, in words: "Rotary Speed Mod.Source" is the rotary
    /// speed, and "LFO Int.Mod.Source" is the LFO's intensity. Null for fields that aren't
    /// modulation sources at all.
    /// </summary>
    private static string? Destination(string field)
    {
        const string suffix = "Mod.Source";
        if (!field.EndsWith(suffix, StringComparison.Ordinal)) return null;
        string name = field[..^suffix.Length].TrimEnd(' ', '.');
        if (name.EndsWith("Int", StringComparison.Ordinal))
            name = name[..^3].TrimEnd(' ', '.') + " intensity";
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// The player's name for a modulation source, or null when it isn't something a player
    /// touches. The two vocabularies spell these differently — the effect tables say
    /// "JS+Y (CC#01)" where an engine's own list says "JS+Y (CC#1)" — so match on substance.
    /// </summary>
    private static string? Source(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "Off") return null;
        string s = raw;

        // The vector joystick before the main one: "Vector JS+Y" also contains "JS+Y", and
        // they are two different sticks under two different hands.
        if (s.Contains("Vector JS", StringComparison.Ordinal)) return "Vector joystick";
        if (s.Contains("JS+Y", StringComparison.Ordinal)) return "Joystick up";
        if (s.Contains("JS-Y", StringComparison.Ordinal) || s.Contains("JS–Y", StringComparison.Ordinal))
            return "Joystick down";
        if (s.Contains("JSX", StringComparison.Ordinal) || s.Contains("JS X", StringComparison.Ordinal))
            return "Joystick left/right";
        if (s.Contains("SW1", StringComparison.Ordinal)) return "SW1";
        if (s.Contains("SW2", StringComparison.Ordinal)) return "SW2";
        if (s.Contains("FootSW", StringComparison.Ordinal) || s.Contains("Foot Switch", StringComparison.Ordinal))
            return "Foot switch";
        if (s.Contains("Pedal", StringComparison.Ordinal) && !s.Contains("Soft", StringComparison.Ordinal))
            return "Foot pedal";
        if (s.Contains("Ribbon", StringComparison.Ordinal)) return "Ribbon";
        if (s.Contains("Slider", StringComparison.Ordinal)) return "Slider";
        if (s.Contains("Damper", StringComparison.Ordinal)) return "Damper pedal";
        if (s.Contains("Sostenuto", StringComparison.Ordinal)) return "Sostenuto pedal";
        if (s.Contains("Soft", StringComparison.Ordinal)) return "Soft pedal";
        if (s.Contains("Aftertouch", StringComparison.Ordinal) || s.Contains("After Touch", StringComparison.Ordinal))
            return "Aftertouch";
        if (s.Contains("Porta", StringComparison.Ordinal)) return "Portamento switch";

        int knob = s.IndexOf("Knob", StringComparison.Ordinal);
        if (knob >= 0)
        {
            foreach (char c in s[(knob + 4)..])
                if (char.IsDigit(c)) return $"Knob {c}";
            return "Knob";
        }
        return null;   // LFOs, envelopes, mixers, velocity, tempo — the sound moves these, not you
    }

    // Hands before feet before knobs: the order a player would think of them.
    private static int SourceOrder(string source) => source switch
    {
        "Joystick up" => 0,
        "Joystick down" => 1,
        "Joystick left/right" => 2,
        "Vector joystick" => 3,
        "Ribbon" => 4,
        "Slider" => 5,
        "SW1" => 6,
        "SW2" => 7,
        "Aftertouch" => 8,
        _ when source.StartsWith("Knob", StringComparison.Ordinal) => 9,
        _ => 10,
    };
}
