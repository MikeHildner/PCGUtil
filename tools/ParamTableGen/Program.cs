// Generates PcgUtil.Core's embedded parameter-table resources from the vendor SysEx
// documentation in the git-ignored kronos-manuals/ folder. Run by hand from the repo root:
//
//     dotnet run --project tools/ParamTableGen
//
// The outputs (src/PcgUtil.Core/Resources/*.json.gz) are committed; the inputs are not.
// Only facts travel — names, offsets, bit spans, ranges — never the documents themselves.
//
// The dump-table grammar this parses (established by a corpus study, zero violations):
//   - Rows are fixed-width columns; slice positions come from each table's "| OFS" header
//     line. Never split on '|' — some continuation rows carry a literal '|' in a cell.
//   - A row with a BLANK name cell continues the bits of the previous named field.
//     Assembly is LSB-first: the first-declared span holds the value's low bits.
//   - Field width and signedness come from the DATA(hex) range, not from any one row:
//     when the range's low hex value exceeds its high one, the field is two's-complement
//     over its total allocated bits.
//   - "HH(fixed)" means a constant; masked rows ("*" cells) are undocumented bytes.

using System.IO.Compression;
using System.Text;
using System.Text.Json;

string repoRoot = FindRepoRoot();
string dumps = Path.Combine(repoRoot, "kronos-manuals", "KRONOS_SysEx_2_1", "SysExDumps");
string enums = Path.Combine(repoRoot, "kronos-manuals", "KRONOS_SysEx_2_1", "SysExEnums");
string outDir = Path.Combine(repoRoot, "src", "PcgUtil.Core", "Resources");

if (!Directory.Exists(dumps))
{
    Console.Error.WriteLine($"Vendor docs not found at {dumps} - nothing to do.");
    return 1;
}
Directory.CreateDirectory(outDir);

// ----- Effect.txt: one table per effect type, offsets relative to the slot's param area -----
var effectTables = ParseEffectFile(Path.Combine(dumps, "Effect.txt"));
var dmod = ParseEnumList(Path.Combine(enums, "Miscellaneous Enums.txt"),
                         "* Dynamic Modulation Sources (Dmod)");

var payload = new
{
    source = "SysEx docs 2.1",
    tables = effectTables.Select(t => new
    {
        id = t.Id,
        name = t.Name,
        size = t.Size,
        fields = t.Fields.Select(f => new
        {
            n = f.Name,
            b = f.Spans.Select(s => new[] { s.Byte, s.Hi, s.Lo }).ToArray(),
            sg = f.Signed ? 1 : 0,
            lo = f.Min,
            hi = f.Max,
            fx = f.Fixed,
            h = f.Hint,
        }).ToArray(),
    }).ToArray(),
    enums = new Dictionary<string, string[]> { ["dmod"] = dmod },
};

WriteGz(Path.Combine(outDir, "effects.json.gz"), payload);

int fieldCount = effectTables.Sum(t => t.Fields.Count);
Console.WriteLine($"effects.json.gz: {effectTables.Count} tables, {fieldCount} fields, dmod {dmod.Length} entries.");

// ----- Tone adjust: one destination table per voice-model engine -----
//
// Each VoiceModels/<Engine>.txt carries a "<Engine> Tone Adjust" section with columns
// assign / type / value / name — the vocabulary behind a timbre's tone-adjust assign
// bytes. Ids 0-47 are a common region (names identical across engines where present),
// 48+ engine-private; only id 0 means Off. "type" is Rel (a signed offset from the
// program's own value) or Abs (an override).
string voiceModels = Path.Combine(repoRoot, "kronos-manuals", "KRONOS_SysEx_2_1",
                                  "SysExParams", "VoiceModels");
string[] engineNames = { "HD-1", "AL-1", "CX-3", "STR-1", "MS-20EX", "PolysixEX",
                         "MOD-7", "SGX-1", "EP-1" };
var engines = new Dictionary<string, object>();
foreach (var engine in engineNames)
{
    string path = Path.Combine(voiceModels, engine + ".txt");
    if (!File.Exists(path)) continue;
    var rows = ParseToneAdjust(path, engine);
    engines[engine] = rows.Select(r => new { id = r.Id, n = r.Name, rel = r.Relative ? 1 : 0, h = r.Hint }).ToArray();
}

var toneAdjust = new
{
    source = "SysEx docs 2.1",
    engines,
    enums = new Dictionary<string, string[]>
    {
        // Combi-level assignable controls; both lists are implicitly 0-indexed from "Off".
        ["sw12"] = ParseEnumList(Path.Combine(enums, "Miscellaneous Enums.txt"), "* SW1/2 Assignments"),
        ["knob58"] = ParseEnumList(Path.Combine(enums, "Miscellaneous Enums.txt"), "* Knob 5-8 assignments"),
    },
};
WriteGz(Path.Combine(outDir, "toneadjust.json.gz"), toneAdjust);
Console.WriteLine($"toneadjust.json.gz: {engines.Count} engines "
    + $"({string.Join(", ", engines.Select(e => $"{e.Key} {((Array)e.Value).Length}"))}), "
    + $"sw12 {((string[])toneAdjust.enums["sw12"]).Length}, knob58 {((string[])toneAdjust.enums["knob58"]).Length}.");
return 0;

// ----- Parsing -----

static List<EffectTable> ParseEffectFile(string path)
{
    var tables = new List<EffectTable>();
    EffectTable? current = null;
    int[]? pipes = null;      // column slice positions from the current header
    int currentByte = -1;
    Field? open = null;       // field awaiting possible continuation rows

    foreach (var raw in File.ReadLines(path))
    {
        string line = raw.TrimEnd('\r');
        if (line.Length == 0) continue;

        // "NN:Name" opens a table ("00:No Effect" has no rows and stays empty).
        int colon = line.IndexOf(':');
        if (colon is > 0 and <= 3 && int.TryParse(line[..colon], out int id) && !line.StartsWith("|"))
        {
            Close(ref open, current);
            current = new EffectTable(id, line[(colon + 1)..].Trim());
            tables.Add(current);
            pipes = null;
            currentByte = -1;
            continue;
        }
        if (current is null || line[0] != '|' && line[0] != '+') continue;
        if (line[0] == '+') continue; // separator
        if (line.Contains("| OFS ", StringComparison.Ordinal) || line.Contains("| OFS|", StringComparison.Ordinal))
        {
            pipes = Enumerable.Range(0, line.Length).Where(i => line[i] == '|').ToArray();
            continue;
        }
        if (pipes is null) continue;

        string Cell(int i)
        {
            int start = pipes[i] + 1;
            int end = i + 1 < pipes.Length ? pipes[i + 1] : line.Length;
            if (start >= line.Length) return "";
            end = Math.Min(end, line.Length);
            return line[start..end].Trim();
        }

        string ofs = Cell(0), bit = Cell(1), name = Cell(2), data = Cell(3), value = Cell(4);
        if (name == "*") { Close(ref open, current); continue; } // masked/undocumented
        if (ofs.Length > 0 && int.TryParse(ofs, out int b)) currentByte = b;
        if (currentByte < 0) continue;

        var (hi, lo) = ParseBits(bit);
        if (name.Length > 0)
        {
            Close(ref open, current);
            open = new Field(name, data, value);
            open.Spans.Add(new Span(currentByte, hi, lo));
        }
        else if (open is not null && data.Length == 0)
        {
            open.Spans.Add(new Span(currentByte, hi, lo)); // continuation: more bits, LSB-first
        }
    }
    Close(ref open, current);
    return tables;

    static void Close(ref Field? open, EffectTable? table)
    {
        if (open is null || table is null) { open = null; return; }
        open.Finish();
        table.Fields.Add(open);
        open = null;
    }
}

// The "<Engine> Tone Adjust" section: space-aligned assign/type/value/name rows, running
// from the header to the next section (the AMS list, which has its own header + columns).
// A blank line inside the table separates the common ids from the engine-private ones and
// must NOT end parsing — only a non-indented, non-numeric line does.
static List<(int Id, string Name, bool Relative, string? Hint)> ParseToneAdjust(string path, string engine)
{
    var rows = new List<(int, string, bool, string?)>();
    bool inSection = false;
    foreach (var raw in File.ReadLines(path))
    {
        string line = raw.TrimEnd();
        if (!inSection)
        {
            inSection = line.Trim() == engine + " Tone Adjust";
            continue;
        }
        if (line.Trim().Length == 0) continue;                  // blank: separator or padding
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) continue;
        if (!int.TryParse(parts[0], out int id))
        {
            if (line.StartsWith("assign", StringComparison.Ordinal)
                || line.StartsWith("------", StringComparison.Ordinal))
                continue;                                        // header rows
            break;                                               // next section reached
        }
        // "0  -  -  Off" | "1  Rel  -99..99  Filter Cutoff" | "48  Abs  0..8  Upper Drawbar 1"
        string type = parts[1];
        string hint = parts.Length > 2 ? parts[2] : "";
        int nameFrom = parts.Length > 3 ? 3 : 2;
        string name = string.Join(' ', parts.Skip(nameFrom));
        if (id == 0) continue;                                   // Off is implicit
        rows.Add((id, name, type == "Rel", hint == "-" ? null : hint));
    }
    return rows;
}

static (int Hi, int Lo) ParseBits(string bit)
{
    if (bit.Length == 0) return (7, 0);            // whole byte
    int tilde = bit.IndexOf('~');
    if (tilde < 0) { int b = int.Parse(bit); return (b, b); }
    return (int.Parse(bit[..tilde]), int.Parse(bit[(tilde + 1)..]));
}

// Enum lists are implicitly 0-indexed by position, so an elided run ("MIDI CC#00 / ... /
// MIDI CC#95") must be EXPANDED or every later entry lands at the wrong id. The docs use
// a bare "..." between two numbered endpoints of the same family.
static string[] ParseEnumList(string path, string header)
{
    var names = new List<string>();
    bool inList = false;
    bool pendingEllipsis = false;
    foreach (var raw in File.ReadLines(path, Encoding.UTF8))
    {
        string line = raw.TrimEnd('\r').Trim();
        if (line.StartsWith('*'))
        {
            if (inList) break;
            inList = line == header;
            continue;
        }
        if (!inList || line.Length == 0) continue;
        if (line == "...")
        {
            pendingEllipsis = true;
            continue;
        }
        if (pendingEllipsis && names.Count > 0
            && TrailingNumber(names[^1]) is { } from && TrailingNumber(line) is { } to && to > from + 1)
        {
            string prefix = names[^1][..^from.ToString("00").Length];
            for (int n = from + 1; n < to; n++)
                names.Add(prefix + n.ToString("00"));
        }
        pendingEllipsis = false;
        names.Add(line);
    }
    return names.ToArray();

    // "MIDI CC#00" -> 0; null when the entry doesn't end in digits.
    static int? TrailingNumber(string text)
    {
        int i = text.Length;
        while (i > 0 && char.IsDigit(text[i - 1])) i--;
        return i == text.Length ? null : int.Parse(text[i..]);
    }
}

static void WriteGz(string path, object payload)
{
    var json = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    });
    using var file = File.Create(path);
    using var gz = new GZipStream(file, CompressionLevel.SmallestSize);
    gz.Write(json);
}

static string FindRepoRoot()
{
    static string? Walk(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName;
    }
    return Walk(AppContext.BaseDirectory) ?? Walk(Directory.GetCurrentDirectory())
        ?? throw new InvalidOperationException("Run from inside the repo.");
}

sealed record Span(int Byte, int Hi, int Lo);

sealed class Field
{
    public Field(string name, string data, string hint) { Name = name; _data = data; Hint = hint.Length == 0 ? null : hint; }

    private readonly string _data;
    public string Name { get; }
    public string? Hint { get; }
    public List<Span> Spans { get; } = new();
    public bool Signed { get; private set; }
    public long Min { get; private set; }
    public long Max { get; private set; }
    public long? Fixed { get; private set; }

    public void Finish()
    {
        int width = Spans.Sum(s => s.Hi - s.Lo + 1);
        int paren = _data.IndexOf("(fixed)", StringComparison.Ordinal);
        if (paren > 0)
        {
            Fixed = Convert.ToInt64(_data[..paren], 16);
            Min = Max = Fixed.Value;
            return;
        }
        int tilde = _data.IndexOf('~');
        if (tilde <= 0)
        {
            Min = 0; Max = (1L << Math.Min(width, 62)) - 1; // undeclared: whole raw range
            return;
        }
        string loHex = _data[..tilde], hiHex = _data[(tilde + 1)..];
        long rawLo = Convert.ToInt64(loHex, 16);
        long rawHi = Convert.ToInt64(hiHex, 16);
        Signed = rawLo > rawHi;
        if (Signed)
        {
            // The endpoints are written at the HEX LITERAL's width (E2 means -30 as an
            // 8-bit value even when the field occupies 6 bits) — the stored value itself
            // is two's-complement over the field's allocated bits, which the reader
            // handles; only the range endpoints are decoded here.
            Min = SignExtend(rawLo, loHex.Length * 4);
            Max = SignExtend(rawHi, hiHex.Length * 4);
        }
        else
        {
            Min = rawLo; Max = rawHi;
        }
        _ = width; // reader-side concern; kept for clarity that allocation ≥ range width
    }

    private static long SignExtend(long raw, int width) =>
        width is > 0 and < 63 && (raw & (1L << (width - 1))) != 0 ? raw - (1L << width) : raw;
}

sealed class EffectTable
{
    public EffectTable(int id, string name) { Id = id; Name = name; }
    public int Id { get; }
    public string Name { get; }
    public List<Field> Fields { get; } = new();
    public int Size => Fields.Count == 0 ? 0 : Fields.Max(f => f.Spans.Max(s => s.Byte)) + 1;
}
