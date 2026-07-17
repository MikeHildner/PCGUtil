namespace PcgUtil.Core;

/// <summary>How a Combi timbre plays its program (bits 5–7 of the timbre's third byte).</summary>
public enum TimbreStatus
{
    Off = 0,
    Int = 1,
    Both = 2,
    Ext = 3,
    Ex2 = 4,
}

/// <summary>The sixteen effect slots of a combi, in hardware order.</summary>
public enum EffectSlot
{
    Ifx1, Ifx2, Ifx3, Ifx4, Ifx5, Ifx6, Ifx7, Ifx8, Ifx9, Ifx10, Ifx11, Ifx12,
    Mfx1, Mfx2, Tfx1, Tfx2,
}

/// <summary>One effect slot of a combi: which effect is loaded and whether it runs.</summary>
public sealed record CombiEffect(EffectSlot Slot, int TypeId, bool IsOn)
{
    /// <summary>Slot label as the hardware shows it: IFX1–IFX12, MFX1/2, TFX1/2.</summary>
    public string Label => Slot switch
    {
        <= EffectSlot.Ifx12 => $"IFX{(int)Slot + 1}",
        EffectSlot.Mfx1 => "MFX1",
        EffectSlot.Mfx2 => "MFX2",
        EffectSlot.Tfx1 => "TFX1",
        _ => "TFX2",
    };

    /// <summary>Effect name from <see cref="EffectNames"/> ("No Effect" for an empty slot).</summary>
    public string TypeName => EffectNames.Name(TypeId);

    /// <summary>True when the slot holds a real effect (type is not 000: No Effect).</summary>
    public bool HasEffect => TypeId != 0;
}

/// <summary>One of a combi's four KARMA modules (A–D) and the GE it plays.</summary>
public sealed record KarmaModule(int Index, int GeId)
{
    /// <summary>Module label as the hardware shows it: A–D.</summary>
    public string Label => ((char)('A' + Index)).ToString();

    /// <summary>Display label of the selected GE ("preset 0123" or "USER-A 096").</summary>
    public string GeLabel => Combi.KarmaGeLabel(GeId);

    /// <summary>True when the module has a GE selected (GE 0 reads as off).</summary>
    public bool IsOn => GeId != 0;
}

/// <summary>A decoded Combi and its timbres.</summary>
public sealed class Combi
{
    /// <summary>Combi bank list index (matches <see cref="PcgCatalog.CombiBanks"/>).</summary>
    public required int Bank { get; init; }
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<CombiTimbre> Timbres { get; init; }

    /// <summary>Category index 0–17; name via <see cref="CombiCategories.Name"/>.</summary>
    public required int Category { get; init; }
    public required int SubCategory { get; init; }
    public required bool Favorite { get; init; }

    /// <summary>Combi tempo in BPM (40.00–300.00), or 0 when the record carries none.</summary>
    public required decimal Tempo { get; init; }

    /// <summary>
    /// GE select of each of the four KARMA modules (A–D). 0–2047 = preset GE (part of the
    /// system software); <see cref="KarmaUserGeBase"/> + bank×128 + number = user GE
    /// (USER-A..L) — user GEs live in a .KGE file, which a .PCG can't carry.
    /// Fewer than four entries when the record is too short to hold them.
    /// </summary>
    public required IReadOnlyList<int> KarmaGeIds { get; init; }

    public const int KarmaUserGeBase = 2048;

    /// <summary>True when any KARMA module selects a user GE (needs its .KGE loaded).</summary>
    public bool UsesUserKarmaGes => KarmaGeIds.Any(id => id >= KarmaUserGeBase);

    /// <summary>
    /// The sixteen effect slots (12 inserts, 2 master, 2 total) in hardware order.
    /// Empty when the record is too short to carry the effect block.
    /// </summary>
    public required IReadOnlyList<CombiEffect> Effects { get; init; }

    /// <summary>The effect slots that actually hold an effect, in hardware order.</summary>
    public IEnumerable<CombiEffect> ActiveEffects => Effects.Where(e => e.HasEffect);

    /// <summary>The four KARMA modules (A–D); a module with GE 0 reads as off.</summary>
    public IReadOnlyList<KarmaModule> KarmaModules =>
        KarmaGeIds.Select((geId, i) => new KarmaModule(i, geId)).ToList();

    /// <summary>Display label for a GE select: "preset 0123" or "USER-A 096".</summary>
    public static string KarmaGeLabel(int geId)
    {
        if (geId < KarmaUserGeBase)
            return $"preset {geId:D4}";
        int bank = (geId - KarmaUserGeBase) / 128;
        int number = (geId - KarmaUserGeBase) % 128;
        return $"USER-{(char)('A' + bank)} {number:D3}";
    }

    public bool IsEmpty => Name.Length == 0;

    /// <summary>
    /// Empty, or a factory "Init Combi" placeholder. Such combis' timbres all default to the
    /// first program, so they're excluded from usage analysis (matches how PCG Tools treats them).
    /// </summary>
    public bool IsEmptyOrInit => IsEmptyOrInitName(Name);

    /// <summary>The name-only form of <see cref="IsEmptyOrInit"/>, for callers that have a
    /// catalog name but no decoded <see cref="Combi"/>.</summary>
    public static bool IsEmptyOrInitName(string name) =>
        name.Length == 0 ||
        (name.Contains("Init", StringComparison.OrdinalIgnoreCase) &&
         name.Contains("Combi", StringComparison.OrdinalIgnoreCase));
}

/// <summary>One timbre of a <see cref="Combi"/> and the Program it references.</summary>
public sealed class CombiTimbre
{
    public required int Index { get; init; } // 0..15
    public required TimbreStatus Status { get; init; }

    /// <summary>Raw program-bank PcgId; resolve a name via <see cref="PcgCatalog.ResolveProgram"/>.</summary>
    public required int ProgramBankPcgId { get; init; }
    public required int ProgramNumber { get; init; }

    /// <summary>0–15 = MIDI channel 1–16; 16 = the global channel ("Gch").</summary>
    public required int MidiChannel { get; init; }
    public required int Volume { get; init; }
    public required int Transpose { get; init; }
    /// <summary>Detune in cents (−1200…+1200).</summary>
    public required int Detune { get; init; }
    public required bool Mute { get; init; }

    /// <summary>Key zone, MIDI notes 0–127 (name via <see cref="PcgNotes.Name"/>).</summary>
    public required int BottomKey { get; init; }
    public required int TopKey { get; init; }

    /// <summary>Velocity zone, 1–127.</summary>
    public required int BottomVelocity { get; init; }
    public required int TopVelocity { get; init; }

    /// <summary>True when the timbre actually plays an internal program (Int or Both).</summary>
    public bool UsesInternalProgram => Status is TimbreStatus.Int or TimbreStatus.Both;

    /// <summary>True when the key zone is narrower than the full keyboard.</summary>
    public bool HasKeyZone => BottomKey > 0 || TopKey < 127;

    /// <summary>True when the velocity zone is narrower than the full 1–127 range.</summary>
    public bool HasVelocityZone => BottomVelocity > 1 || TopVelocity < 127;
}

/// <summary>
/// MIDI note names as the hardware displays them: C-1 (note 0) … G9 (note 127), middle C = C4.
/// Convention verified against a vendor pack whose set-list notes describe zones in prose
/// (e.g. "F#3 to B3" decodes to exactly 54–59).
/// </summary>
public static class PcgNotes
{
    private static readonly string[] Names =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static string Name(int note) =>
        note is >= 0 and <= 127 ? $"{Names[note % 12]}{note / 12 - 1}" : "?";
}

/// <summary>
/// Factory combi category names (indices 0–17). Derived by joining the factory banks'
/// decoded category bytes with the published voice name list; 16–17 ship unnamed and are
/// user-assignable (a file's Global data can rename any of these — not yet decoded).
/// </summary>
public static class CombiCategories
{
    private static readonly string[] Names =
    {
        "Keyboard", "Organ", "Bell/Mallet/Perc", "Strings", "Brass/Reed", "Orchestral",
        "World", "Guitar/Plucked", "Pads/Vocal", "MotionSynth", "Synth", "LeadSplits",
        "BassSplits", "Complex&SFX", "BPM Sync", "Drums/Hits",
    };

    public static string Name(int category) =>
        category >= 0 && category < Names.Length ? Names[category] : $"User {category:D2}";
}
