namespace PcgUtil.Core;

/// <summary>
/// The effect parameter tables and the machinery to read a slot's settings — currently
/// <em>blocked on one hardware probe</em>, and deliberately not wired to any UI.
///
/// What is known: the parameters live inside the slot's 74 bytes (§19 proved it on
/// hardware — block-copying a slot carried the sound's exact settings), the routing
/// header occupies the first 9 bytes, and the documented tables (names, widths, order,
/// ranges) are certainly right — they are generated from the same source as everything
/// the instrument has ever confirmed. What is NOT known is the record's packing rule:
/// the documentation's tight bit-stream layout does not match record bytes under any
/// simple transform — base shifts 0..17, either bit order, per-byte mirroring, byte
/// padding, one-byte-per-parameter, and MSB-filled streams all decode factory content
/// at statistical noise (~15% out-of-range), while the first packed byte alone decodes
/// cleanly. A single-parameter probe on the instrument (save, change one knob, save
/// again, diff) will expose the real rule in one shot; until then this class refuses to
/// guess in front of a musician.
/// </summary>
public static class EffectParams
{
    /// <summary>Bytes between an insert slot's start and its parameter area.</summary>
    public const int IfxParamOffset = 9;

    // Master parameter areas, relative to the record start. MFX1 and TFX1 follow their
    // 2-byte headers; MFX2's parameters sit past the shared master bytes (returns/chain)
    // at 1052 — candidates validated by the in-range scan over every factory effect.
    private const int Mfx1ParamBase = 978;
    private const int Mfx2ParamBase = 1052;
    private const int Tfx1ParamBase = 1118;

    /// <summary>The documented parameter table for an effect type (null for type 0, "No
    /// Effect", and for ids outside the documented range).</summary>
    public static ParamTable? TableFor(int typeId) =>
        typeId <= 0 ? null : ParamTables.Effect(typeId);

    /// <summary>
    /// Where <paramref name="slot"/>'s parameter area begins, relative to the record
    /// start — or null when the slot's parameters are undocumented (TFX2).
    /// </summary>
    public static long? ParamBase(EffectSlot slot) => slot switch
    {
        <= EffectSlot.Ifx12 => CombiReader.IfxBase + (int)slot * CombiReader.IfxStride + IfxParamOffset,
        EffectSlot.Mfx1 => Mfx1ParamBase,
        EffectSlot.Mfx2 => Mfx2ParamBase,
        EffectSlot.Tfx1 => Tfx1ParamBase,
        _ => null, // TFX2: the documentation carries no parameter area for it
    };

    /// <summary>
    /// Decodes one effect slot's parameters from a record. Returns null when the slot is
    /// empty, its type undocumented, or its parameter area unknown (TFX2). The Bypass
    /// constant every table starts with is filtered — the on/off switch already shows in
    /// the slot header.
    /// </summary>
    public static IReadOnlyList<ParamValue>? Read(byte[] data, long recordOffset, CombiEffect effect)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (TableFor(effect.TypeId) is not { } table || ParamBase(effect.Slot) is not { } paramBase)
            return null;

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
