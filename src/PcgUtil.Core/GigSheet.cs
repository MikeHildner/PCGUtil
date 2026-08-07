namespace PcgUtil.Core;

/// <summary>One KARMA module that is running, with its GE.</summary>
public sealed record GigKarma(string Module, string GeLabel, string? GeName)
{
    public string Display => GeName is null ? GeLabel : $"{GeLabel} · {GeName}";
}

/// <summary>
/// One layer of a slot's sound. <see cref="Sounds"/> is the question that matters on stage:
/// a combi can hold sixteen timbres while only a handful answer the keyboard.
/// </summary>
public sealed record GigLayer(int Number, string Program, string? Engine, int BottomKey, int TopKey,
                              int BottomVelocity, int TopVelocity, int Volume, int Transpose,
                              int Detune, bool Sounds, string? SilentReason,
                              IReadOnlyList<ToneAdjustEntry> Tweaks)
{
    public string KeyRange => $"{PcgNotes.Name(BottomKey)}–{PcgNotes.Name(TopKey)}";

    public bool FullKeyRange => BottomKey <= KeyboardMap.LowestKey && TopKey >= KeyboardMap.HighestKey;

    public bool FullVelocityRange => BottomVelocity <= 1 && TopVelocity >= 127;

    public string VelocityRange => FullVelocityRange ? "all" : $"{BottomVelocity}–{TopVelocity}";

    public string TransposeLabel => Transpose == 0 ? "at pitch" : $"{Transpose:+0;-0} semi";

    /// <summary>An organ layer's drawbar registration ("8 8 8 8 7 8 3 4"), when it has one.</summary>
    public string? Drawbars
    {
        get
        {
            var bars = Tweaks
                .Where(t => t.Name is { } n && n.StartsWith("Upper Drawbar", StringComparison.Ordinal))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => t.Value.ToString())
                .ToList();
            return bars.Count >= 8 ? string.Join(' ', bars) : null;
        }
    }
}

/// <summary>An effect slot as the sheet shows it.</summary>
public sealed record GigEffect(string Label, int TypeId, string TypeName, bool IsOn);

/// <summary>Insert effects wired one into the next, in signal order.</summary>
public sealed record GigEffectChain(IReadOnlyList<GigEffect> Steps);

/// <summary>
/// One thing a player can move, and what it does. Sources are physical controls only —
/// a joystick, a pedal, a switch, a knob — never an LFO or an envelope, which move by
/// themselves and belong to the sound rather than to the player.
/// </summary>
public sealed record GigControl(string Source, string Moves, string Where);

/// <summary>
/// Everything a printable sheet needs about one Set List slot. Assembled by
/// <see cref="Build"/> and rendered by <see cref="PcgHtmlReport"/> — keeping the two apart
/// means the judgement calls (which layers sound, how effects chain, which controls are the
/// player's) are testable as values instead of as markup.
/// </summary>
public sealed record GigSheet(SetList List, SetListSlot Slot, string Loads, string? TargetName,
                              decimal Tempo, IReadOnlyList<GigKarma> Karma,
                              IReadOnlyList<GigLayer> Layers, IReadOnlyList<GigLayer> Silent,
                              IReadOnlyList<GigEffectChain> Chains,
                              IReadOnlyList<GigEffect> Standalone,
                              IReadOnlyList<GigEffect> Masters,
                              IReadOnlyList<GigControl> Controls,
                              int? GlobalMidiChannel, string? Unavailable)
{
    /// <summary>
    /// Other slots of the same set list that load this very sound. A set list often plays one
    /// combi several times — a chorus that comes round three times — and printing three
    /// identical pages helps nobody, so the page names the repeats instead.
    /// </summary>
    public IReadOnlyList<SetListSlot> AlsoAt { get; init; } = Array.Empty<SetListSlot>();

    /// <summary>Slots among <see cref="AlsoAt"/> whose own settings differ from this one's —
    /// volume, transpose and hold belong to a slot, not to the sound it loads.</summary>
    public IEnumerable<SetListSlot> DifferingSlots => AlsoAt.Where(s =>
        s.Volume != Slot.Volume || s.Transpose != Slot.Transpose
        || s.HoldTimeIndex != Slot.HoldTimeIndex);

    /// <summary>Layers that answer the keyboard, plus the ones that never will.</summary>
    public IEnumerable<GigLayer> AllLayers => Layers.Concat(Silent);

    /// <summary>Every insert effect the sheet lists, chained or not.</summary>
    public IEnumerable<GigEffect> Inserts => Chains.SelectMany(c => c.Steps).Concat(Standalone);

    /// <summary>
    /// One page per distinct sound, in the order it is first played. Two slots that load the
    /// same combi share a page, which names the repeats; the pages come back in set order, so
    /// a printed stack still reads the way the gig runs.
    /// </summary>
    public static IReadOnlyList<GigSheet> BuildPages(PcgFile pcg, PcgCatalog catalog, SetList list,
                                                     IEnumerable<int> slotIndices,
                                                     IReadOnlyList<Combi>? combis = null,
                                                     IReadOnlyList<ProgramInfo>? programs = null,
                                                     IReadOnlyList<IReadOnlyList<string>>? geBanks = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(slotIndices);
        combis ??= CombiReader.Read(pcg);
        programs ??= ProgramReader.Read(pcg);

        var pages = new List<GigSheet>();
        var seen = new Dictionary<(PcgItemKind, int, int), int>();   // sound -> page index
        foreach (int index in slotIndices.Distinct().OrderBy(i => i))
        {
            var slot = list.Slots.ElementAtOrDefault(index);
            if (slot is null || slot.IsEmpty) continue;
            var key = (slot.Reference.Kind, slot.Reference.Bank, slot.Reference.Index);
            if (seen.TryGetValue(key, out int at))
            {
                pages[at] = pages[at] with { AlsoAt = pages[at].AlsoAt.Append(slot).ToList() };
                continue;
            }
            seen[key] = pages.Count;
            pages.Add(Build(pcg, catalog, list, slot, combis, programs, geBanks));
        }
        return pages;
    }

    /// <summary>
    /// Builds the sheet for one slot. Never throws for a slot the file can't resolve — a
    /// vendor pack routinely references banks it doesn't carry, and one bad slot must not
    /// take down a whole set list's worth of sheets.
    /// </summary>
    public static GigSheet Build(PcgFile pcg, PcgCatalog catalog, SetList list, SetListSlot slot,
                                 IReadOnlyList<Combi>? combis = null,
                                 IReadOnlyList<ProgramInfo>? programs = null,
                                 IReadOnlyList<IReadOnlyList<string>>? geBanks = null)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(slot);

        int? globalChannel = GlobalReader.Read(pcg)?.MidiChannel;
        string loads = Loading(slot, catalog);
        string? name = catalog.Resolve(slot.Reference);

        var empty = new GigSheet(list, slot, loads, name, 0, Array.Empty<GigKarma>(),
            Array.Empty<GigLayer>(), Array.Empty<GigLayer>(), Array.Empty<GigEffectChain>(),
            Array.Empty<GigEffect>(), Array.Empty<GigEffect>(), Array.Empty<GigControl>(),
            globalChannel, null);

        if (slot.Reference.Kind == PcgItemKind.Song)
            return empty with { Unavailable = "This slot loads a song. Sequencer data lives in the "
                + "companion .SNG file, not in the .PCG." };

        if (slot.Reference.Kind == PcgItemKind.Program)
            return BuildProgram(pcg, catalog, empty, slot, programs, globalChannel);

        var combi = (combis ?? CombiReader.Read(pcg))
            .FirstOrDefault(c => c.Bank == slot.Reference.Bank && c.Index == slot.Reference.Index);
        if (combi is null)
            return empty with { Unavailable = $"{loads} isn't in this file." };

        var layers = new List<GigLayer>();
        var silent = new List<GigLayer>();
        programs ??= ProgramReader.Read(pcg);
        foreach (var timbre in combi.Timbres)
        {
            if (timbre.Status is TimbreStatus.Off) continue;
            var layer = LayerOf(pcg, catalog, programs, combi, timbre, globalChannel);
            (layer.Sounds ? layers : silent).Add(layer);
        }

        var (chains, standalone, masters) = GroupEffects(combi.Effects);
        var karma = combi.KarmaModules.Where(m => m.IsOn)
            .Select(m => new GigKarma(m.Label, m.GeLabel,
                geBanks is null ? null : KgeReader.UserGeName(geBanks, m.GeId)))
            .ToList();

        return empty with
        {
            Tempo = combi.Tempo,
            Karma = karma,
            Layers = layers,
            Silent = silent,
            Chains = chains,
            Standalone = standalone,
            Masters = masters,
            Controls = GigControls.Build(pcg, combi, layers, programs),
        };
    }

    private static GigSheet BuildProgram(PcgFile pcg, PcgCatalog catalog, GigSheet empty,
                                         SetListSlot slot, IReadOnlyList<ProgramInfo>? programs,
                                         int? globalChannel)
    {
        int bank = PcgCatalog.ProgramBankIndexForPcgId(slot.Reference.Bank);
        var info = (programs ?? ProgramReader.Read(pcg))
            .FirstOrDefault(p => p.Bank == bank && p.Index == slot.Reference.Index);
        if (info is null)
            return empty with { Unavailable = $"{empty.Loads} isn't in this file." };

        // A program plays across the whole keyboard; its own oscillator zones are a different
        // feature, and guessing them here would put lines on the sheet that aren't true.
        var layer = new GigLayer(1, info.Name, EngineName(info), 0, 127, 1, 127, 127, 0, 0,
            Sounds: true, SilentReason: null, Tweaks: ToneAdjust.ReadProgram(pcg, bank, slot.Reference.Index));
        var (chains, standalone, masters) = GroupEffects(info.Effects);

        return empty with
        {
            Layers = new[] { layer },
            Chains = chains,
            Standalone = standalone,
            Masters = masters,
            Controls = GigControls.ForProgram(pcg, bank, slot.Reference.Index, EngineNames(info), info.Name),
            GlobalMidiChannel = globalChannel,
        };
    }

    private static GigLayer LayerOf(PcgFile pcg, PcgCatalog catalog, IReadOnlyList<ProgramInfo> programs,
                                    Combi combi, CombiTimbre timbre, int? globalChannel)
    {
        int bank = PcgCatalog.ProgramBankIndexForPcgId(timbre.ProgramBankPcgId);
        var info = programs.FirstOrDefault(p => p.Bank == bank && p.Index == timbre.ProgramNumber);
        string program = catalog.ResolveProgram(timbre.ProgramBankPcgId, timbre.ProgramNumber)
            ?? $"#{timbre.ProgramNumber:D3}";

        // Three different silences, and a player needs to tell them apart: a timbre that
        // plays only external gear, one listening on a channel the keyboard doesn't send,
        // and one that is simply muted.
        string? reason =
            timbre.Mute ? "muted"
            : timbre.Status is TimbreStatus.Ext or TimbreStatus.Ex2 ? "plays external gear only"
            : !OnKeyboardChannel(timbre.MidiChannel, globalChannel)
                ? $"listens on MIDI channel {timbre.MidiChannel + 1}"
                : null;

        var tweaks = SafeTweaks(pcg, combi, timbre);
        return new GigLayer(timbre.Index + 1, program, info is null ? null : EngineName(info),
            timbre.BottomKey, timbre.TopKey, timbre.BottomVelocity, timbre.TopVelocity,
            timbre.Volume, timbre.Transpose, timbre.Detune, reason is null, reason, tweaks);
    }

    private static IReadOnlyList<ToneAdjustEntry> SafeTweaks(PcgFile pcg, Combi combi, CombiTimbre timbre)
    {
        try
        {
            return ToneAdjust.ReadCombiTimbre(pcg, combi.Bank, combi.Index, timbre.Index);
        }
        catch (Exception)
        {
            return Array.Empty<ToneAdjustEntry>();   // a bank the file doesn't carry
        }
    }

    /// <summary>The keyboard plays a timbre set to the global channel, or to "Gch".</summary>
    private static bool OnKeyboardChannel(int channel, int? globalChannel) =>
        channel == 16 || channel == (globalChannel ?? 0);

    private static string EngineName(ProgramInfo info) =>
        info.ExiEngine is { } e and > 0 ? ExiEngines.Name(e) : "HD-1";

    private static IReadOnlyList<string> EngineNames(ProgramInfo info) =>
        info.ExiEngine is { } e and > 0
            ? new[] { ExiEngines.Name(e), info.ExiEngine2 is { } e2 and > 0 ? ExiEngines.Name(e2) : "" }
            : new[] { "HD-1", "" };

    /// <summary>
    /// Sorts the sixteen effect slots into chains, loners and masters. Chain links are
    /// forward-only and 1-based; a chain may hop over an empty slot, which routes signal
    /// without adding an effect, so those are followed but not printed.
    /// </summary>
    private static (List<GigEffectChain> Chains, List<GigEffect> Standalone, List<GigEffect> Masters)
        GroupEffects(IReadOnlyList<CombiEffect> effects)
    {
        var chains = new List<GigEffectChain>();
        var standalone = new List<GigEffect>();
        var masters = effects.Where(e => e.Slot > EffectSlot.Ifx12 && e.HasEffect)
                             .Select(Effect).ToList();

        var inserts = effects.Where(e => e.Slot <= EffectSlot.Ifx12).ToList();
        if (inserts.Count == 0) return (chains, standalone, masters);

        int Next(int i) =>
            inserts[i].ChainOn && inserts[i].ChainTo >= i + 2 && inserts[i].ChainTo <= inserts.Count
                ? inserts[i].ChainTo - 1 : -1;

        var isTarget = new bool[inserts.Count];
        for (int i = 0; i < inserts.Count; i++)
            if (Next(i) is var n && n >= 0) isTarget[n] = true;

        var used = new HashSet<int>();
        for (int i = 0; i < inserts.Count; i++)
        {
            if (isTarget[i] || Next(i) < 0) continue;   // not the head of a chain
            var steps = new List<GigEffect>();
            for (int at = i, hops = 0; at >= 0 && hops <= inserts.Count; at = Next(at), hops++)
            {
                if (!used.Add(at)) break;               // malformed data must not loop
                if (inserts[at].HasEffect) steps.Add(Effect(inserts[at]));
            }
            if (steps.Count > 1) chains.Add(new GigEffectChain(steps));
            else standalone.AddRange(steps);            // a chain of one is just an effect
        }

        for (int i = 0; i < inserts.Count; i++)
            if (!used.Contains(i) && inserts[i].HasEffect)
                standalone.Add(Effect(inserts[i]));

        standalone.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        return (chains, standalone, masters);
    }

    private static GigEffect Effect(CombiEffect e) =>
        new(e.Label, e.TypeId, e.TypeName, e.IsOn);

    private static string Loading(SetListSlot slot, PcgCatalog catalog)
    {
        if (slot.Reference.Kind == PcgItemKind.Song)
            return $"Song {slot.Reference.Index:D3}";
        string label = slot.Reference.Kind == PcgItemKind.Program
            ? PcgBankLabels.Program(PcgCatalog.ProgramBankIndexForPcgId(slot.Reference.Bank))
            : PcgBankLabels.Combi(slot.Reference.Bank);
        _ = catalog;
        return $"{slot.Reference.Kind} {label} #{slot.Reference.Index:D3}";
    }
}
