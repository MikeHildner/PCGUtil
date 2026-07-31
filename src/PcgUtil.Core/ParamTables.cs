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
/// The effect parameter tables — one per effect type id (0..185) — generated from the
/// vendor SysEx documentation by <c>tools/ParamTableGen</c> and embedded as a resource,
/// so builds never need the source documents. Offsets inside a table are relative to the
/// effect slot's <em>parameter area</em>; see <see cref="EffectParams"/> for where that
/// sits inside a record.
/// </summary>
public static class ParamTables
{
    private static readonly Lazy<Loaded> _effects = new(() => Load("effects.json.gz"));

    public static ParamTable? Effect(int typeId) =>
        _effects.Value.Tables.TryGetValue(typeId, out var t) ? t : null;

    /// <summary>The dynamic-modulation source names (the "Off~Tempo" list many effect
    /// parameters select from), indexed by raw value.</summary>
    public static IReadOnlyList<string> DmodSources => _effects.Value.Enums.TryGetValue("dmod", out var e)
        ? e : Array.Empty<string>();

    private sealed record Loaded(IReadOnlyDictionary<int, ParamTable> Tables,
                                 IReadOnlyDictionary<string, string[]> Enums);

    private static Loaded Load(string resourceName)
    {
        var assembly = typeof(ParamTables).Assembly;
        string full = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(full)!;
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(ReadAll(gz));

        var tables = new Dictionary<int, ParamTable>();
        foreach (var t in doc.RootElement.GetProperty("tables").EnumerateArray())
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
            int id = t.GetProperty("id").GetInt32();
            tables[id] = new ParamTable(id, t.GetProperty("name").GetString()!,
                t.GetProperty("size").GetInt32(), fields);
        }

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
