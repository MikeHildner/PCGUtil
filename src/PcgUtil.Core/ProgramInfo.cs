namespace PcgUtil.Core;

/// <summary>A decoded Program's metadata (name plus the fields beyond it).</summary>
public sealed class ProgramInfo
{
    /// <summary>Program bank list index (matches <see cref="PcgCatalog.ProgramBanks"/>).</summary>
    public required int Bank { get; init; }
    public required int Index { get; init; }
    public required string Name { get; init; }

    /// <summary>Category index 0–15; name via <see cref="ProgramCategories.Name"/>.</summary>
    public required int Category { get; init; }
    public required int SubCategory { get; init; }
    public required bool Favorite { get; init; }

    /// <summary>EXi instrument engine id for programs in EXi banks (null for HD-1 banks);
    /// name via <see cref="ExiEngines.Name"/>.</summary>
    public required int? ExiEngine { get; init; }

    public bool IsEmpty => Name.Length == 0;
}

/// <summary>
/// Factory program category names (indices 0–15). Programs use their own vocabulary —
/// it shares only the first four entries with <see cref="CombiCategories"/>. Derived by
/// correlating the published voice name list with the record bytes (768/768 factory
/// programs match). A file's Global data can rename these — not yet decoded.
/// </summary>
public static class ProgramCategories
{
    private static readonly string[] Names =
    {
        "Keyboard", "Organ", "Bell/Mallet", "Strings", "Vocal/Airy", "Brass",
        "Woodwind/Reed", "Guitar/Plucked", "Bass/Synth Bass", "SlowSynth", "FastSynth",
        "LeadSynth", "MotionSynth", "SFX", "Short Decay/Hit", "Drums",
    };

    public static string Name(int category) =>
        category >= 0 && category < Names.Length ? Names[category] : $"User {category:D2}";
}

/// <summary>
/// EXi instrument engine names by the id byte inside EXi program records (640/640 factory
/// EXi programs match the published list). Ids 0–1 are unobserved/reserved.
/// </summary>
public static class ExiEngines
{
    public static string Name(int engineId) => engineId switch
    {
        2 => "AL-1",
        3 => "CX-3",
        4 => "STR-1",
        5 => "MS-20EX",
        6 => "PolysixEX",
        7 => "MOD-7",
        8 => "SGX-2",
        9 => "EP-1",
        _ => $"EXi {engineId}",
    };
}
