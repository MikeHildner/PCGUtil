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
