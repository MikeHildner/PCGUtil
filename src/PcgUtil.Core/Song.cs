namespace PcgUtil.Core;

/// <summary>
/// One sequencer song from a companion .SNG file. A .PCG carries no sequencer data at all,
/// so songs only appear once the matching .SNG is loaded alongside it.
/// </summary>
public sealed class Song
{
    public required int Index { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// The song's sixteen track timbres. A song stores them in exactly the record layout a
    /// combi uses — the vendor SysEx dump describes a song as a 7810-byte timbre set plus a
    /// control block — so the same decoder and the same program references apply.
    /// </summary>
    public required IReadOnlyList<CombiTimbre> Timbres { get; init; }

    /// <summary>Timbres that actually play an internal program, in track order.</summary>
    public IEnumerable<CombiTimbre> ActiveTimbres =>
        Timbres.Where(t => t.UsesInternalProgram && !t.Mute);

    /// <summary>A song nobody has touched: default name and nothing sounding.</summary>
    public bool IsEmptyOrInit =>
        !ActiveTimbres.Any() ||
        (Name.StartsWith("NEW SONG", StringComparison.OrdinalIgnoreCase) && Name.Trim().Length <= 12);

    public string DisplayName => Name.Trim().Length == 0 ? $"Song {Index:000}" : Name.Trim();
}
