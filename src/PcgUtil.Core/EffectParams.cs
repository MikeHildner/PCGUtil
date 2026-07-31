namespace PcgUtil.Core;

/// <summary>
/// Reads the actual parameter settings of a record's effect slots — the values behind the
/// effect names the app has shown since the effects decode.
///
/// The geometry was cracked by a single-parameter hardware probe (2026-07-31) and then
/// proven corpus-wide at exactly zero range violations over 299,638 fields in two real
/// backups: <em>each slot's parameter area sits 64 bytes BEFORE its header</em>, encoded
/// precisely as the documentation's packed bit-stream (fields take minimal widths,
/// straddle bytes freely, low bits first; sign is two's-complement over the field's
/// allocated bits). So IFX k's parameters live at 24 + 74k — the region every doc table
/// masks out belongs to the FOLLOWING slot — and the masters sit at 912 (MFX1), 980
/// (MFX2), 1052 (TFX1) and 1120 (TFX2), whose "missing" parameter block was never
/// missing at all. The probe: Stereo Dyna Compressor with Wet/Dry 37, Sensitivity 99,
/// Attack 61, Output Level 73 diffed to bytes 24/27–29/31 of the combi record, matching
/// the packed table bit for bit.
/// </summary>
public static class EffectParams
{
    /// <summary>A slot's parameter area starts this many bytes before its header.</summary>
    public const int ParamAreaBeforeHeader = 64;

    /// <summary>Where a slot's packed parameter area begins, relative to the record start
    /// (probe-anchored, zero violations corpus-wide — see the class remarks).</summary>
    public static long ParamBase(EffectSlot slot) => slot switch
    {
        <= EffectSlot.Ifx12 => CombiReader.IfxBase + (int)slot * CombiReader.IfxStride - ParamAreaBeforeHeader,
        EffectSlot.Mfx1 => 912,
        EffectSlot.Mfx2 => 980,
        EffectSlot.Tfx1 => 1052,
        _ => 1120, // TFX2
    };

    /// <summary>The documented parameter table for an effect type (null for type 0, "No
    /// Effect", and for ids outside the documented range).</summary>
    public static ParamTable? TableFor(int typeId) =>
        typeId <= 0 ? null : ParamTables.Effect(typeId);

    /// <summary>A combi effect slot's decoded settings (null when empty/undocumented).</summary>
    public static IReadOnlyList<ParamValue>? ReadCombi(PcgFile pcg, int bank, int index, EffectSlot slot)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (offset, recordSize) = PcgEditor.LocateCombi(pcg, bank, index);
        return Read(pcg.Data, offset, CombiReader.ReadEffects(pcg.Data, offset, recordSize)[(int)slot]);
    }

    /// <summary>A program effect slot's decoded settings (null when empty/undocumented).</summary>
    public static IReadOnlyList<ParamValue>? ReadProgram(PcgFile pcg, int bank, int index, EffectSlot slot)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (offset, recordSize) = PcgEditor.LocateProgram(pcg, bank, index);
        return Read(pcg.Data, offset, CombiReader.ReadEffects(pcg.Data, offset, recordSize)[(int)slot]);
    }

    /// <summary>
    /// Decodes one effect slot's parameters from a record. Returns null when the slot is
    /// empty or its type undocumented. The Bypass constant every table starts with is
    /// filtered — the on/off switch already shows in the slot header.
    /// </summary>
    public static IReadOnlyList<ParamValue>? Read(byte[] data, long recordOffset, CombiEffect effect)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (TableFor(effect.TypeId) is not { } table)
            return null;
        long paramBase = ParamBase(effect.Slot);

        long baseOffset = recordOffset + paramBase;
        var values = new List<ParamValue>(table.Fields.Count);
        foreach (var field in table.Fields)
        {
            if (field.Fixed is not null && field.Name == "Bypass")
                continue;
            long raw = field.Read(data, baseOffset);
            values.Add(new ParamValue(field, raw, Format(field, raw)));
        }
        return values;
    }

    /// <summary>
    /// Display text for a raw value: signed values as-is, two-decimal scales recognized
    /// from the documentation's display range ("40.00~300.00" over a ×100 raw span),
    /// modulation sources by name, everything else as the number with the documented
    /// range as context.
    /// </summary>
    public static string Format(ParamField field, long raw)
    {
        if (field.Fixed is not null)
            return field.Hint ?? raw.ToString();

        // Modulation source selectors: the "Off~Tempo" range over the Dmod list.
        if (field.Hint == "Off~Tempo" && !field.Signed)
        {
            var sources = ParamTables.DmodSources;
            if (raw >= 0 && raw < sources.Count)
                return sources[(int)raw];
        }

        // Wet/Dry as the instrument writes it: a ratio ("37:63"), not a bare number.
        if (field.Name == "Wet/Dry" && !field.Signed && field.Max == 100 && raw is >= 0 and <= 100)
            return raw == 0 ? "Dry" : raw == 100 ? "Wet" : $"{raw}:{100 - raw}";

        if (field.Hint is { } hint && TryScale(field, hint, out double scale, out int decimals))
            return (raw / scale).ToString(decimals == 2 ? "0.00" : "0.0",
                System.Globalization.CultureInfo.InvariantCulture);

        return raw.ToString();
    }

    // A decimal display range over a wider raw range means a linear scale: "-15.0~+15.0"
    // over raw -30..30 is raw/2; "40.00~300.00" over 4000..30000 is raw/100. The divisor
    // is whatever maps the endpoints exactly — not necessarily a power of ten.
    private static bool TryScale(ParamField field, string hint, out double scale, out int decimals)
    {
        scale = 1;
        int tilde = hint.IndexOf('~');
        decimals = 0;
        if (tilde <= 0) return false;
        string loText = hint[..tilde], hiText = hint[(tilde + 1)..];
        decimals = DecimalsOf(hiText);
        if (decimals is < 1 or > 2 || DecimalsOf(loText) != decimals) return false;
        if (!double.TryParse(loText, System.Globalization.CultureInfo.InvariantCulture, out double lo)
            || !double.TryParse(hiText, System.Globalization.CultureInfo.InvariantCulture, out double hi)
            || hi <= lo || field.Max <= field.Min)
            return false;
        double candidate = (field.Max - field.Min) / (hi - lo);
        if (candidate <= 1) return false;
        // Real only when both endpoints map exactly under it.
        if (Math.Abs(lo * candidate - field.Min) < 0.001 && Math.Abs(hi * candidate - field.Max) < 0.001)
        {
            scale = candidate;
            return true;
        }
        return false;
    }

    private static int DecimalsOf(string text)
    {
        int dot = text.LastIndexOf('.');
        if (dot < 0) return 0;
        int count = 0;
        for (int i = dot + 1; i < text.Length && char.IsDigit(text[i]); i++) count++;
        return count;
    }
}
