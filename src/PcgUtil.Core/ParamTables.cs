using System.IO.Compression;
using System.Text.Json;

namespace PcgUtil.Core;

/// <summary>One bit span of a parameter: bits <see cref="Lo"/>..<see cref="Hi"/> of byte
/// <see cref="Byte"/> (relative to the table's base offset).</summary>
public sealed record ParamSpan(int Byte, int Hi, int Lo)
{
    public int Width => Hi - Lo + 1;
}

/// <summary>
/// One documented parameter: a name, the bit spans that hold it (LSB-first — the first
/// span carries the value's low bits), and its range. Signedness is two's-complement over
/// the total allocated bits. <see cref="Fixed"/> marks constants; <see cref="Hint"/> is
/// the documentation's display range ("Dry~Wet", "-15.0~+15.0"), useful as a legend but
/// not machine-precise.
/// </summary>
public sealed record ParamField(string Name, IReadOnlyList<ParamSpan> Spans, bool Signed,
                                long Min, long Max, long? Fixed, string? Hint)
{
    public int Width => Spans.Sum(s => s.Width);

    /// <summary>Reads the field's value from <paramref name="data"/>, LSB-first across the
    /// spans, sign-extended over the field's total width when the range says so.</summary>
    public long Read(byte[] data, long baseOffset)
    {
        long value = 0;
        int shift = 0;
        foreach (var s in Spans)
        {
            long index = baseOffset + s.Byte;
            if (index < 0 || index >= data.Length)
                return 0;
            long chunk = (data[index] >> s.Lo) & ((1 << s.Width) - 1);
            value |= chunk << shift;
            shift += s.Width;
        }
        if (Signed && shift is > 0 and < 63 && (value & (1L << (shift - 1))) != 0)
            value -= 1L << shift;
        return value;
    }

    public bool InRange(long value) => value >= Min && value <= Max;
}

/// <summary>A parameter table: every documented field of one region (one effect type's
/// parameter area, a record section, …).</summary>
public sealed record ParamTable(int Id, string Name, int Size, IReadOnlyList<ParamField> Fields);

/// <summary>A field with its decoded value and display text.</summary>
public sealed record ParamValue(ParamField Field, long Raw, string Display)
{
    public bool IsDefault => Raw == 0 || Field.Fixed is not null;
}

/// <summary>
/// Display text for a decoded parameter, in whatever units the documentation declares.
/// Shared by every table-driven reader; the effect-only readings (modulation sources by
/// name, Wet/Dry ratios) live in <see cref="EffectParams.Format"/> on top of this.
/// </summary>
public static class ParamFormat
{
    /// <summary>The number, scaled when the documented display range says it is a decimal.</summary>
    public static string Number(ParamField field, long raw)
    {
        ArgumentNullException.ThrowIfNull(field);
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

/// <summary>
/// One tone-adjust destination: what a knob/switch/fader assign id points at on a given
/// engine. <see cref="Relative"/> destinations hold a signed offset from the program's own
/// value; absolute ones hold an override. <see cref="RangeHint"/> is the documentation's
/// range text ("-99..99", "0..8").
/// </summary>
public sealed record ToneAdjustDestination(int Id, string Name, bool Relative, string? RangeHint);

/// <summary>
/// The effect parameter tables — one per effect type id (0..185) — generated from the
/// vendor SysEx documentation by <c>tools/ParamTableGen</c> and embedded as a resource,
/// so builds never need the source documents. Offsets inside a table are relative to the
/// effect slot's <em>parameter area</em>; see <see cref="EffectParams"/> for where that
/// sits inside a record.
/// </summary>
public static class ParamTables
{
    private static readonly Lazy<Loaded> _effects = new(() => Load("effects.json.gz"));
    private static readonly Lazy<ToneAdjustVocabulary> _toneAdjust = new(LoadToneAdjust);
    private static readonly Lazy<RecordVocabulary> _records = new(LoadRecords);

    public static ParamTable? Effect(int typeId) =>
        _effects.Value.Tables.TryGetValue(typeId, out var t) ? t : null;

    /// <summary>The dynamic-modulation source names (the "Off~Tempo" list many effect
    /// parameters select from), indexed by raw value.</summary>
    public static IReadOnlyList<string> DmodSources => _effects.Value.Enums.TryGetValue("dmod", out var e)
        ? e : Array.Empty<string>();

    /// <summary>
    /// What a tone-adjust assign id means on a given engine. One table per voice model
    /// ("HD-1", "AL-1", "CX-3", …): ids 1–47 are a shared region whose names agree across
    /// engines that support them, 48+ are engine-private (a CX-3's 48–56 are its drawbars).
    /// Null when the engine is unknown or the id isn't valid for it.
    /// </summary>
    public static ToneAdjustDestination? ToneAdjust(string engine, int assignId) =>
        _toneAdjust.Value.Engines.TryGetValue(VocabularyEngine(engine), out var table)
        && table.TryGetValue(assignId, out var d) ? d : null;

    // The two vendor documents disagree on one engine's name: the product (and the voice
    // name list our ExiEngines table was verified against, 640/640) calls engine id 8
    // "SGX-2", while the SysEx docs file it as "SGX-1". Same engine — map it across.
    private static string VocabularyEngine(string engine) =>
        engine == "SGX-2" ? "SGX-1" : engine;

    /// <summary>Names for a combi's SW1/SW2 assign values, indexed by raw value.</summary>
    public static IReadOnlyList<string> SwitchAssignments => _toneAdjust.Value.Sw12;

    /// <summary>Names for a combi's assignable Knob5–8 values, indexed by raw value.</summary>
    public static IReadOnlyList<string> KnobAssignments => _toneAdjust.Value.Knob58;

    /// <summary>
    /// The program record's documented sections, in the documentation's order — one table
    /// per section, whose field offsets are absolute inside the record. HD-1 and EXi
    /// programs have different records (3706 vs 4960 bytes) and so different section lists.
    /// </summary>
    public static IReadOnlyList<ParamTable> ProgramSections(bool exi) =>
        exi ? _records.Value.Exi : _records.Value.Hd1;

    /// <summary>
    /// One EXi engine's own parameter sections ("Drawbar", "Rotary Speaker", …), addressed
    /// from the start of that engine's payload region inside an EXi program record. Empty
    /// when the engine is unknown.
    /// </summary>
    public static IReadOnlyList<ParamTable> EngineSections(string engine) =>
        _records.Value.Engines.TryGetValue(VocabularyEngine(engine), out var tables)
            ? tables : Array.Empty<ParamTable>();

    private sealed record RecordVocabulary(IReadOnlyList<ParamTable> Hd1, IReadOnlyList<ParamTable> Exi,
                                           IReadOnlyDictionary<string, IReadOnlyList<ParamTable>> Engines);

    private static RecordVocabulary LoadRecords()
    {
        using var doc = JsonDocument.Parse(ReadResource("records.json.gz"));
        var engines = new Dictionary<string, IReadOnlyList<ParamTable>>(StringComparer.Ordinal);
        foreach (var e in doc.RootElement.GetProperty("engines").EnumerateObject())
            engines[e.Name] = ReadTables(e.Value);
        return new RecordVocabulary(
            ReadTables(doc.RootElement.GetProperty("hd1")),
            ReadTables(doc.RootElement.GetProperty("exi")),
            engines);
    }

    private sealed record ToneAdjustVocabulary(
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, ToneAdjustDestination>> Engines,
        string[] Sw12, string[] Knob58);

    private static ToneAdjustVocabulary LoadToneAdjust()
    {
        using var doc = JsonDocument.Parse(ReadResource("toneadjust.json.gz"));
        var engines = new Dictionary<string, IReadOnlyDictionary<int, ToneAdjustDestination>>();
        foreach (var e in doc.RootElement.GetProperty("engines").EnumerateObject())
        {
            var map = new Dictionary<int, ToneAdjustDestination>();
            foreach (var row in e.Value.EnumerateArray())
            {
                int id = row.GetProperty("id").GetInt32();
                map[id] = new ToneAdjustDestination(
                    id,
                    row.GetProperty("n").GetString()!,
                    row.GetProperty("rel").GetInt32() != 0,
                    row.TryGetProperty("h", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null);
            }
            engines[e.Name] = map;
        }
        string[] List(string key) =>
            doc.RootElement.GetProperty("enums").TryGetProperty(key, out var v)
                ? v.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>();
        return new ToneAdjustVocabulary(engines, List("sw12"), List("knob58"));
    }

    private sealed record Loaded(IReadOnlyDictionary<int, ParamTable> Tables,
                                 IReadOnlyDictionary<string, string[]> Enums);

    private static byte[] ReadResource(string resourceName)
    {
        var assembly = typeof(ParamTables).Assembly;
        string full = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(full)!;
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        return ReadAll(gz);
    }

    /// <summary>Reads a {"tables": [...]} object — every resource encodes fields the same way.</summary>
    private static List<ParamTable> ReadTables(JsonElement owner)
    {
        var tables = new List<ParamTable>();
        foreach (var t in owner.GetProperty("tables").EnumerateArray())
        {
            var fields = new List<ParamField>();
            foreach (var f in t.GetProperty("fields").EnumerateArray())
            {
                var spans = f.GetProperty("b").EnumerateArray()
                    .Select(s =>
                    {
                        var a = s.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                        return new ParamSpan(a[0], a[1], a[2]);
                    })
                    .ToList();
                fields.Add(new ParamField(
                    f.GetProperty("n").GetString()!,
                    spans,
                    f.GetProperty("sg").GetInt32() != 0,
                    f.GetProperty("lo").GetInt64(),
                    f.GetProperty("hi").GetInt64(),
                    f.TryGetProperty("fx", out var fx) && fx.ValueKind == JsonValueKind.Number ? fx.GetInt64() : null,
                    f.TryGetProperty("h", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null));
            }
            tables.Add(new ParamTable(t.GetProperty("id").GetInt32(), t.GetProperty("name").GetString()!,
                t.GetProperty("size").GetInt32(), fields));
        }
        return tables;
    }

    private static Loaded Load(string resourceName)
    {
        using var doc = JsonDocument.Parse(ReadResource(resourceName));
        var tables = ReadTables(doc.RootElement).ToDictionary(t => t.Id);

        var enums = new Dictionary<string, string[]>();
        if (doc.RootElement.TryGetProperty("enums", out var en))
            foreach (var p in en.EnumerateObject())
                enums[p.Name] = p.Value.EnumerateArray().Select(x => x.GetString()!).ToArray();

        return new Loaded(tables, enums);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
