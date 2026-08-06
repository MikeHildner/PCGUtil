namespace PcgUtil.Core;

/// <summary>
/// Reads the category-name tables from the <c>GLB1</c> (Global settings) chunk.
///
/// GLB1's name block starts at payload offset 12912: six consecutive tables of
/// 24-byte, space-padded entries — program categories (18) at 12912, program
/// sub-categories (18×8) at 13344, combi categories (18) at 16800, combi
/// sub-categories at 17232, KARMA GE categories at 20688, and their sub-categories
/// at 21120. Indices 16–17 of each category table are the user-renameable slots
/// (factory default "User 16"/"User 17"). Located by anchoring the known factory
/// names in the sample's GLB1; sound packs may carry no GLB1 at all, in which case
/// callers fall back to the factory tables.
/// </summary>
public static class GlobalReader
{
    private const int NameLength = 24;
    private const int CategoryCount = 18;
    private const int MasterTuneOffset = 0;   // signed cents around A440
    private const int KeyTransposeOffset = 1; // signed semitones
    private const int ProgramCategoriesOffset = 12912;
    private const int CombiCategoriesOffset = 16800;
    private const int MidiChannelOffset = 24576;  // 00~0F = channel 1–16

    /// <summary>Per-file global settings and category names, or null when the file carries no GLB1.</summary>
    public sealed class GlobalInfo
    {
        /// <summary>Master tune in cents around A440 (probe-verified: +20 → byte 20).</summary>
        public required int MasterTune { get; init; }

        /// <summary>Global key transpose in semitones (probe-verified: +2 → byte 2).</summary>
        public required int KeyTranspose { get; init; }

        /// <summary>
        /// The global MIDI channel, 0–15 for channels 1–16 — the channel the keyboard plays
        /// on. This is what decides which of a combi's timbres actually sound when you press
        /// a key: a timbre answers the keyboard only when its own channel is this one (or
        /// "Gch"). Null when the file's GLB1 is too short to carry it, in which case callers
        /// should say the channel is unknown rather than assume one.
        /// </summary>
        public required int? MidiChannel { get; init; }

        public required IReadOnlyList<string> ProgramCategoryNames { get; init; }
        public required IReadOnlyList<string> CombiCategoryNames { get; init; }
    }

    public static GlobalInfo? Read(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var glb = pcg.FindFirst("GLB1");
        if (glb is null || glb.Size < CombiCategoriesOffset + CategoryCount * NameLength)
            return null;

        return new GlobalInfo
        {
            MasterTune = (sbyte)pcg.Data[glb.DataOffset + MasterTuneOffset],
            KeyTranspose = (sbyte)pcg.Data[glb.DataOffset + KeyTransposeOffset],
            // Its own guard: the channel sits well past the name tables this method already
            // requires, so a shorter GLB1 still yields categories rather than nothing.
            MidiChannel = glb.Size > MidiChannelOffset
                ? pcg.Data[glb.DataOffset + MidiChannelOffset] & 0x0F : null,
            ProgramCategoryNames = ReadNames(pcg.Data, glb.DataOffset + ProgramCategoriesOffset),
            CombiCategoryNames = ReadNames(pcg.Data, glb.DataOffset + CombiCategoriesOffset),
        };
    }

    private static IReadOnlyList<string> ReadNames(byte[] data, long tableOffset)
    {
        var names = new string[CategoryCount];
        for (int i = 0; i < CategoryCount; i++)
            names[i] = PcgText.ReadFixedString(data, tableOffset + (long)i * NameLength, NameLength);
        return names;
    }
}
