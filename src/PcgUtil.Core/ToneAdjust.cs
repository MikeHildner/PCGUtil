using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// One tone-adjust assignment: which panel control, what it points at, and the stored
/// setting. <see cref="Name"/> is null when the id isn't in the engine's documented table
/// (or the engine is unknown) — callers show the raw id rather than inventing a label.
/// </summary>
public sealed record ToneAdjustEntry(string Control, int AssignId, bool TargetsSecondPatch,
                                     string? Name, bool Relative, string? RangeHint, int Value)
{
    /// <summary>What to show: the destination's name, or an honest "#id" fallback.</summary>
    public string Label => Name ?? $"parameter #{AssignId}";

    /// <summary>Relative destinations are offsets from the program's own value, so a sign
    /// belongs on them; absolute ones are plain settings.</summary>
    public string Display => Relative ? Value.ToString("+0;-0;0") : Value.ToString();
}

/// <summary>A combi's assignable panel controls, decoded to names.</summary>
public sealed record CombiControl(string Control, int AssignId, string? Name,
                                  bool Momentary = false, bool On = false)
{
    public string Label => Name ?? $"assign #{AssignId}";
}

/// <summary>
/// Reads Tone Adjust — the per-timbre tweaks a combi applies to a program without editing
/// the program itself (brightness, envelopes, an organ's drawbars…). This is what answers
/// "what did this combi change about this layer?".
///
/// Layout differs between the two places it lives:
/// <list type="bullet">
/// <item>A combi timbre carries assigns AND values at +54 inside its 188-byte record:
/// Knob1–8 (assign, pad, signed-16 value) stride 4; Switch1–16 (assign, value bit,
/// signed-16 on-value) stride 4; Fader1–8 like the knobs; then Master Fader.</item>
/// <item>A program's own block (record 2586+) carries assigns only — there, the adjustment
/// <em>is</em> the parameter — with knobs and faders on a 2-byte stride and switches on 4
/// (assign + a 16-bit on-value).</item>
/// </list>
/// An assign byte packs a patch selector in bit 7 (which EXi slot it targets) and the id in
/// the low seven bits; id 0 means Off. Ids resolve against the referenced program's engine:
/// 1–47 are a shared region, 48+ engine-private.
/// </summary>
public static class ToneAdjust
{
    /// <summary>Offset of the tone-adjust block inside a combi timbre.</summary>
    public const int TimbreOffset = 54;

    private const int ProgramBlockOffset = 2586;

    /// <summary>
    /// A combi timbre's non-Off tone-adjust entries, named against the engine of the
    /// program that timbre plays.
    /// </summary>
    public static IReadOnlyList<ToneAdjustEntry> ReadCombiTimbre(PcgFile pcg, int bank, int index, int timbre)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        if (timbre is < 0 or >= CombiReader.TimbresPerCombi)
            throw new ArgumentOutOfRangeException(nameof(timbre), timbre, "Timbres are 0–15.");

        var (record, recordSize) = PcgEditor.LocateCombi(pcg, bank, index);
        long tOff = record + CombiReader.TimbresOffset + (long)timbre * CombiReader.TimbreStride;
        if (CombiReader.TimbresOffset + (timbre + 1) * CombiReader.TimbreStride > recordSize)
            return Array.Empty<ToneAdjustEntry>();

        var (engine1, engine2) = EnginesOf(pcg, pcg.Data[tOff + 1], pcg.Data[tOff]);
        long block = tOff + TimbreOffset;
        var data = pcg.Data;
        var entries = new List<ToneAdjustEntry>();

        void Add(string control, long assignAt, long valueAt)
        {
            if (assignAt + 1 >= data.Length || valueAt + 1 >= data.Length) return;
            byte assign = data[assignAt];
            int id = assign & 0x7F;
            if (id == 0) return;
            bool second = (assign & 0x80) != 0;
            var dest = ParamTables.ToneAdjust(second ? engine2 : engine1, id);
            entries.Add(new ToneAdjustEntry(control, id, second, dest?.Name, dest?.Relative ?? false,
                dest?.RangeHint, BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan((int)valueAt, 2))));
        }

        for (int k = 0; k < 8; k++)
            Add($"Knob {k + 1}", block + k * 4, block + k * 4 + 2);
        for (int s = 0; s < 16; s++)
            Add($"Switch {s + 1}", block + 32 + s * 4, block + 32 + s * 4 + 2);
        for (int f = 0; f < 8; f++)
            Add($"Fader {f + 1}", block + 96 + f * 4, block + 96 + f * 4 + 2);
        Add("Master fader", block + 128, block + 130);
        return entries;
    }

    /// <summary>
    /// A program's own tone-adjust assignments. The program block stores no knob/fader
    /// values (the adjustment is the parameter), so those entries carry 0; switches carry
    /// their on-value.
    /// </summary>
    public static IReadOnlyList<ToneAdjustEntry> ReadProgram(PcgFile pcg, int bank, int index)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (record, recordSize) = PcgEditor.LocateProgram(pcg, bank, index);
        if (recordSize <= ProgramBlockOffset + 100)
            return Array.Empty<ToneAdjustEntry>();

        ProgramsOf(pcg).TryGetValue((bank, index), out var info);
        string engine1 = EngineName(pcg, bank, info?.ExiEngine);
        string engine2 = EngineName(pcg, bank, info?.ExiEngine2);

        var data = pcg.Data;
        long block = record + ProgramBlockOffset;
        var entries = new List<ToneAdjustEntry>();

        void Add(string control, long assignAt, long valueAt)
        {
            if (assignAt >= data.Length) return;
            byte assign = data[assignAt];
            int id = assign & 0x7F;
            if (id == 0) return;
            bool second = (assign & 0x80) != 0;
            var dest = ParamTables.ToneAdjust(second ? engine2 : engine1, id);
            int value = valueAt >= 0 && valueAt + 1 < data.Length
                ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan((int)valueAt, 2)) : 0;
            entries.Add(new ToneAdjustEntry(control, id, second, dest?.Name,
                dest?.Relative ?? false, dest?.RangeHint, value));
        }

        for (int k = 0; k < 8; k++) Add($"Knob {k + 1}", block + k * 2, -1);
        for (int s = 0; s < 16; s++) Add($"Switch {s + 1}", block + 16 + s * 4, block + 16 + s * 4 + 2);
        for (int f = 0; f < 8; f++) Add($"Fader {f + 1}", block + 80 + f * 2, -1);
        Add("Master fader", block + 96, -1);
        return entries;
    }

    /// <summary>
    /// The combi's assignable panel controls: SW1/SW2 (with mode and current state) and
    /// the assignable knobs 5–8. Only controls set to something other than Off.
    /// </summary>
    public static IReadOnlyList<CombiControl> ReadCombiControls(PcgFile pcg, int bank, int index)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (record, recordSize) = PcgEditor.LocateCombi(pcg, bank, index);
        if (recordSize <= Knob5Offset + 3) return Array.Empty<CombiControl>();

        var data = pcg.Data;
        var controls = new List<CombiControl>();
        var switchNames = ParamTables.SwitchAssignments;
        var knobNames = ParamTables.KnobAssignments;

        for (int s = 0; s < 2; s++)
        {
            byte b = data[record + Switch1Offset + s];
            int id = b & 0x1F;
            if (id == 0) continue;
            controls.Add(new CombiControl($"SW{s + 1}", id,
                id < switchNames.Count ? switchNames[id] : null,
                Momentary: ((b >> 6) & 1) != 0, On: ((b >> 7) & 1) != 0));
        }
        for (int k = 0; k < 4; k++)
        {
            byte b = data[record + Knob5Offset + k];
            if (b == 0) continue;
            controls.Add(new CombiControl($"Knob {k + 5}", b, b < knobNames.Count ? knobNames[b] : null));
        }
        return controls;
    }

    // Combi record: SW1/SW2 assign+mode+state, then the four assignable knob assigns.
    private const int Switch1Offset = 4796;
    private const int Knob5Offset = 4798;

    // A timbre's tone adjust is expressed in the terms of the program it plays, so the
    // engine comes from that program: HD-1 by bank type, otherwise the EXi slot ids.
    private static (string First, string Second) EnginesOf(PcgFile pcg, byte bankPcgId, byte number)
    {
        int bank = PcgCatalog.ProgramBankIndexForPcgId(bankPcgId);
        if (bank < 0) return ("", "");
        ProgramsOf(pcg).TryGetValue((bank, number), out var info);
        return (EngineName(pcg, bank, info?.ExiEngine), EngineName(pcg, bank, info?.ExiEngine2));
    }

    private static string EngineName(PcgFile pcg, int bank, int? exiEngine)
    {
        if (PcgBankIdentity.ProgramBankType(pcg, bank) == ProgramBankType.Hd1)
            return "HD-1";
        // EXi: id 0 means the slot is off; 1 is HD-1 (unused in an EXi record).
        return exiEngine is null or 0 ? "" : ExiEngines.Name(exiEngine.Value);
    }

    // Decoding a file's programs costs a full pass, and a caller reading tone adjust for
    // every timbre of every combi would otherwise pay it thousands of times. Memoized per
    // byte image (the file's own data array), so an edited copy naturally gets a fresh map.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[],
        IReadOnlyDictionary<(int Bank, int Index), ProgramInfo>> _programCache = new();

    private static IReadOnlyDictionary<(int Bank, int Index), ProgramInfo> ProgramsOf(PcgFile pcg) =>
        _programCache.GetValue(pcg.Data, _ => ProgramReader.Read(pcg)
            .ToDictionary(p => (p.Bank, p.Index)));
}
