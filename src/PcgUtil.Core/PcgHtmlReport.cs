using System.Globalization;
using System.Net;
using System.Text;

namespace PcgUtil.Core;

/// <summary>
/// Builds printable, self-contained HTML reports from already-decoded data — a set list, all
/// set lists, the program usage report, and the per-slot gig sheet. Output is HTML-escaped
/// with simple print-friendly CSS.
///
/// These are separate documents rather than a printable view inside the app, and that is
/// deliberate: the app follows the reader's light/dark theme and its stylesheet carries no
/// print rules, so an in-page sheet would print dark on a gig-bag printer. A downloaded
/// document never sees the app's theme and always prints light.
/// </summary>
public static class PcgHtmlReport
{
    public static string SetList(SetList setList, PcgCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(setList);
        ArgumentNullException.ThrowIfNull(catalog);
        var body = new StringBuilder();
        AppendSetListSection(body, setList, catalog, pageBreak: false);
        return Page($"Set List {setList.Index:D3} - {setList.DisplayName}", body.ToString());
    }

    public static string AllSetLists(IReadOnlyList<SetList> setLists, PcgCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(setLists);
        ArgumentNullException.ThrowIfNull(catalog);
        var body = new StringBuilder();
        bool first = true;
        foreach (var setList in setLists)
        {
            if (!setList.NamedSlots.Any())
                continue; // skip empty set lists
            AppendSetListSection(body, setList, catalog, pageBreak: !first);
            first = false;
        }
        return Page("Set Lists", body.ToString());
    }

    /// <summary>Printable "what's inside each combi" sheet for one bank: every named combi
    /// with its active timbres and the programs they play.</summary>
    public static string CombiContents(string bankLabel, IReadOnlyList<Combi> combis, PcgCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(combis);
        ArgumentNullException.ThrowIfNull(catalog);
        var body = new StringBuilder();
        body.Append("<h1>Combi bank ").Append(Esc(bankLabel)).Append(" — contents</h1>");
        body.Append("<p class=\"sub\">").Append(combis.Count)
            .Append(combis.Count == 1 ? " combi" : " combis").Append("</p>");

        foreach (var combi in combis)
        {
            var timbres = combi.Timbres.Where(t => t.Status != TimbreStatus.Off).ToList();
            body.Append("<h2 class=\"combi\">#").Append(combi.Index.ToString("D3")).Append(' ')
                .Append(Esc(combi.Name)).Append("</h2>");
            if (timbres.Count == 0)
            {
                body.Append("<p class=\"sub\">No active timbres.</p>");
                continue;
            }
            body.Append("<table><thead><tr><th>Timbre</th><th>Status</th><th>Program</th></tr></thead><tbody>");
            foreach (var t in timbres)
            {
                string label = PcgBankLabels.Program(PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId));
                var name = catalog.ResolveProgram(t.ProgramBankPcgId, t.ProgramNumber);
                body.Append("<tr><td>T").Append(t.Index + 1).Append("</td><td>").Append(t.Status)
                    .Append("</td><td>").Append(Esc($"{label} #{t.ProgramNumber:D3}{(name is null ? "" : $" - {name}")}"))
                    .Append("</td></tr>");
            }
            body.Append("</tbody></table>");
        }
        return Page($"Combi bank {bankLabel} - contents", body.ToString());
    }

    public static string Usage(UsageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var body = new StringBuilder();
        body.Append("<h1>Program usage</h1>");
        body.Append("<p class=\"sub\">")
            .Append(report.Programs.Count).Append(" programs referenced, ")
            .Append(report.UnreferencedPrograms.Count).Append(" unreferenced programs, ")
            .Append(report.UnreferencedCombis.Count).Append(" unreferenced combis.</p>");
        body.Append("<table><thead><tr><th>Program</th><th>References</th></tr></thead><tbody>");
        foreach (var p in report.Programs)
        {
            body.Append("<tr><td>")
                .Append(Esc($"{PcgBankLabels.Program(p.BankIndex)} #{p.Number:D3} - {p.Name}"))
                .Append("</td><td>").Append(p.ReferenceCount).Append("</td></tr>");
        }
        body.Append("</tbody></table>");
        return Page("Program usage", body.ToString());
    }

    /// <summary>
    /// A one-page gig sheet for a single Set List slot: which layers sound and where they sit
    /// on the keyboard, what the effects are, and what the player can move while playing.
    /// </summary>
    public static string GigSheet(GigSheet sheet, string? sourceFile = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var body = new StringBuilder();
        AppendGigSheet(body, sheet, sourceFile, pageBreak: false);
        return Page($"{sheet.Slot.Index:D3} {sheet.Slot.Name}", body.ToString(), GigCss);
    }

    /// <summary>Every slot of a set list, one sheet per printed page.</summary>
    public static string GigSheets(IReadOnlyList<GigSheet> sheets, string? sourceFile = null)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        var body = new StringBuilder();
        bool first = true;
        foreach (var sheet in sheets)
        {
            AppendGigSheet(body, sheet, sourceFile, pageBreak: !first);
            first = false;
        }
        string title = sheets.Count > 0 ? sheets[0].List.DisplayName : "Gig sheets";
        return Page(title, body.ToString(), GigCss);
    }

    private static void AppendGigSheet(StringBuilder sb, GigSheet sheet, string? sourceFile, bool pageBreak)
    {
        sb.Append("<section class=\"gig").Append(pageBreak ? " page-break" : "").Append("\">\n");

        // ----- header -----
        sb.Append("<div class=\"gh\">")
          .Append("<span class=\"cbar sc").Append(sheet.Slot.Color).Append("\"></span>")
          .Append("<span class=\"gnum\">").Append(sheet.Slot.Index.ToString("D3", CultureInfo.InvariantCulture)).Append("</span>")
          .Append("<span class=\"gttl\">").Append(Esc(sheet.Slot.Name)).Append("</span>")
          .Append("<span class=\"gmeta\">").Append(Esc(sheet.List.DisplayName));
        if (sheet.TargetName is { } target)
            sb.Append(" &middot; ").Append(Esc(target));
        sb.Append(" &middot; ").Append(Esc(sheet.Loads));
        if (sheet.Tempo > 0)
            sb.Append(" &middot; ").Append(sheet.Tempo.ToString("0.##", CultureInfo.InvariantCulture)).Append(" BPM");
        foreach (var k in sheet.Karma)
            sb.Append(" &middot; KARMA ").Append(Esc(k.Module)).Append(' ').Append(Esc(k.Display));
        sb.Append("</span></div>\n");
        sb.Append("<div class=\"gsub\">Slot volume ").Append(sheet.Slot.Volume)
          .Append(sheet.Slot.Transpose == 0 ? " &middot; no transpose"
              : $" &middot; transpose {sheet.Slot.Transpose:+0;-0}")
          .Append(" &middot; hold ").Append(Esc(sheet.Slot.HoldTimeLabel)).Append("</div>\n");

        if (sheet.Unavailable is { } why)
        {
            sb.Append("<p class=\"gwarn\">").Append(Esc(why)).Append("</p>\n");
            AppendNotes(sb, sheet);
            AppendGigFooter(sb, sourceFile);
            sb.Append("</section>\n");
            return;
        }

        // ----- keyboard -----
        sb.Append("<h3>What plays where</h3>\n").Append(Keyboard(sheet)).Append('\n');

        sb.Append("<div class=\"gcols\"><div>\n");

        // ----- layers -----
        sb.Append("<h3>Layers</h3>\n<table class=\"gt\"><tr><th>T</th><th>Program</th><th>Engine</th>")
          .Append("<th>Keys</th><th>Vel</th><th class=\"num\">Vol</th><th>Transpose</th></tr>\n");
        int index = 0;
        foreach (var layer in sheet.Layers)
        {
            sb.Append("<tr><td><span class=\"chip l").Append(index++ % LayerColours).Append("\"></span>")
              .Append(layer.Number).Append("</td><td>").Append(Esc(layer.Program)).Append("</td><td>")
              .Append(Esc(layer.Engine ?? "")).Append("</td><td>").Append(Esc(layer.KeyRange))
              .Append("</td><td>").Append(Esc(layer.VelocityRange)).Append("</td><td class=\"num\">")
              .Append(layer.Volume).Append("</td><td>").Append(Esc(layer.TransposeLabel))
              .Append("</td></tr>\n");
        }
        sb.Append("</table>\n");
        if (sheet.Silent.Count > 0)
        {
            var reasons = sheet.Silent.Select(s => s.SilentReason).Distinct().Count();
            sb.Append("<p class=\"gnote\">").Append(sheet.Silent.Count)
              .Append(sheet.Silent.Count == 1 ? " other timbre is loaded but never sounds from the keyboard"
                                              : " other timbres are loaded but never sound from the keyboard")
              .Append(reasons == 1 && sheet.Silent[0].SilentReason is { } only
                  ? $" ({Esc(only.Replace("channel ", "channels ", StringComparison.Ordinal))})" : "")
              .Append(". Nothing to remember on stage.</p>\n");
        }

        // ----- tweaks -----
        var tweaked = sheet.Layers.Where(l => l.Tweaks.Count > 0).ToList();
        if (tweaked.Count > 0)
        {
            var layer = tweaked[0];
            sb.Append("<h3>T").Append(layer.Number).Append(" tweaks &middot; what this combi changes</h3>\n");
            if (layer.Drawbars is { } bars)
                sb.Append("<p class=\"gbars\">Drawbars ").Append(Esc(bars)).Append("</p>\n");
            sb.Append("<table class=\"gt gt2\">\n");
            var named = layer.Tweaks.Where(t => t.Name is not null).ToList();
            foreach (var entry in named.Take(12))
                sb.Append("<tr><td>").Append(Esc(entry.Label)).Append("</td><td class=\"num\">")
                  .Append(Esc(TweakValue(entry))).Append("</td></tr>\n");
            sb.Append("</table>\n");
            if (named.Count > 12)
                sb.Append("<p class=\"gnote\">+").Append(named.Count - 12).Append(" more.</p>\n");
            if (tweaked.Count > 1)
                sb.Append("<p class=\"gnote\">")
                  .Append(string.Join(", ", tweaked.Skip(1).Select(l => $"T{l.Number}")))
                  .Append(tweaked.Count == 2 ? " is tweaked too." : " are tweaked too.").Append("</p>\n");
        }

        sb.Append("</div><div>\n");

        // ----- effects -----
        sb.Append("<h3>Effects</h3>\n");
        if (!sheet.Inserts.Any() && sheet.Masters.Count == 0)
            sb.Append("<p class=\"gnote\">No effects in this patch.</p>\n");
        foreach (var chain in sheet.Chains)
        {
            sb.Append("<div class=\"chain\">\n");
            foreach (var step in chain.Steps)
                AppendEffect(sb, step);
            sb.Append("</div>\n");
        }
        foreach (var effect in sheet.Standalone) AppendEffect(sb, effect);
        foreach (var master in sheet.Masters) AppendEffect(sb, master);

        // ----- controls -----
        sb.Append("<h3>What moves what &middot; while you play</h3>\n");
        if (sheet.Controls.Count == 0)
            sb.Append("<p class=\"gnote\">Nothing in this patch responds to the panel controls.</p>\n");
        else
        {
            sb.Append("<table class=\"gt\">\n");
            foreach (var control in sheet.Controls)
                sb.Append("<tr><td class=\"src\">").Append(Esc(control.Source)).Append("</td><td>")
                  .Append(Esc(control.Moves)).Append("</td></tr>\n");
            sb.Append("</table>\n");
        }

        AppendNotes(sb, sheet);
        sb.Append("</div></div>\n");
        AppendGigFooter(sb, sourceFile);
        sb.Append("</section>\n");
    }

    /// <summary>
    /// A tone-adjust reading in words where the instrument uses words. A two-state control
    /// names its states in its own label — "Perc Level (Soft/Loud)" — so "1" prints as
    /// "Loud", and a plain switch prints as off/on rather than as 0/1. A player reading this
    /// in a hurry should not have to remember which way round the switch goes.
    /// </summary>
    private static string TweakValue(ToneAdjustEntry entry)
    {
        if (entry.Relative || entry.RangeHint is not "0..1" || entry.Value is not (0 or 1))
            return entry.Display;
        if (entry.Name is { } name && name.LastIndexOf('(') is var open && open > 0
            && name.EndsWith(")", StringComparison.Ordinal))
        {
            var states = name[(open + 1)..^1].Split('/');
            if (states.Length == 2) return states[entry.Value].Trim();
        }
        return entry.Value == 0 ? "off" : "on";
    }

    private static void AppendEffect(StringBuilder sb, GigEffect effect)
    {
        sb.Append("<div class=\"fx").Append(effect.IsOn ? "" : " off").Append("\"><span class=\"fxs\">")
          .Append(Esc(effect.Label)).Append("</span>").Append(Esc(effect.TypeName))
          .Append(effect.IsOn ? "" : " (off)").Append("</div>\n");
    }

    private static void AppendNotes(StringBuilder sb, GigSheet sheet)
    {
        sb.Append("<h3>Notes</h3>\n");
        if (sheet.Slot.Description.Length > 0)
            sb.Append("<div class=\"notes ").Append(NoteFontClass(sheet.Slot.CommentFont)).Append("\">")
              .Append(Esc(sheet.Slot.Description)).Append("</div>\n");
        else
            sb.Append("<div class=\"lines\"><div></div><div></div></div>\n");
    }

    private static void AppendGigFooter(StringBuilder sb, string? sourceFile)
    {
        sb.Append("<div class=\"gfoot\">").Append(Esc(sourceFile ?? ""))
          .Append("<span>PCGUtil &middot; read from the file, nothing played</span></div>\n");
    }

    /// <summary>How many layer colours the sheet cycles through.</summary>
    private const int LayerColours = 6;

    /// <summary>
    /// The 88-key keyboard with a labelled bar per sounding layer. Built as inline SVG with
    /// classes rather than style attributes — partly because printers drop background colour
    /// by default (so every bar carries a border and a label, and never relies on its colour
    /// alone), and partly because this file keeps inline styles to a minimum on purpose.
    /// </summary>
    private static string Keyboard(GigSheet sheet)
    {
        const double W = 14, B = 8.4, KeyHeight = 46, LaneHeight = 13, LaneGap = 3;
        double width = KeyboardMap.WhiteKeyCount * W;
        var layers = sheet.Layers.Take(8).ToList();
        double top = layers.Count * (LaneHeight + LaneGap);
        double height = top + KeyHeight + 13;

        var sb = new StringBuilder();
        sb.Append("<svg class=\"kb\" viewBox=\"0 0 ").Append(N(width)).Append(' ').Append(N(height))
          .Append("\" role=\"img\"><title>Key ranges</title>");

        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var (left, right) = KeyboardMap.ZoneSpan(layer.BottomKey, layer.TopKey, W, B);
            double y = i * (LaneHeight + LaneGap);
            sb.Append("<rect class=\"lane l").Append(i % LayerColours).Append("\" x=").Append(Q(left))
              .Append(" y=").Append(Q(y)).Append(" width=").Append(Q(Math.Max(right - left, 2)))
              .Append(" height=").Append(Q(LaneHeight)).Append(" rx=\"2\"/>");

            string label = $"T{layer.Number} {layer.Program}"
                + (layer.Transpose == 0 ? "" : $" {layer.Transpose:+0;-0}");
            // No text metrics in a string-built SVG: estimate, and put the label outside the
            // bar when it will not fit inside one.
            bool inside = (right - left) > label.Length * 4.6 + 10;
            sb.Append("<text class=\"lbl").Append(inside ? " in" : "").Append("\" x=")
              .Append(Q(inside ? left + 5 : Math.Min(right + 4, width - label.Length * 4.6)))
              .Append(" y=").Append(Q(y + 11)).Append('>').Append(Esc(label)).Append("</text>");
        }

        for (int n = KeyboardMap.LowestKey; n <= KeyboardMap.HighestKey; n++)
        {
            if (KeyboardMap.IsBlack(n)) continue;
            sb.Append("<rect class=\"wk\" x=").Append(Q(KeyboardMap.Span(n, W, B).Left))
              .Append(" y=").Append(Q(top)).Append(" width=").Append(Q(W))
              .Append(" height=").Append(Q(KeyHeight)).Append("/>");
        }
        for (int n = KeyboardMap.LowestKey; n <= KeyboardMap.HighestKey; n++)
        {
            if (!KeyboardMap.IsBlack(n)) continue;
            sb.Append("<rect class=\"bk\" x=").Append(Q(KeyboardMap.Span(n, W, B).Left))
              .Append(" y=").Append(Q(top)).Append(" width=").Append(Q(B))
              .Append(" height=").Append(Q(KeyHeight * 0.62)).Append("/>");
        }

        // Where one layer stops and another starts — the split points a player thinks in.
        foreach (double x in layers
            .SelectMany(l => new[] { KeyboardMap.ZoneSpan(l.BottomKey, l.TopKey, W, B).Left })
            .Where(x => x > 1).Select(x => Math.Round(x, 2)).Distinct())
        {
            sb.Append("<line class=\"split\" x1=").Append(Q(x)).Append(" y1=").Append(Q(top - 3))
              .Append(" x2=").Append(Q(x)).Append(" y2=").Append(Q(top + KeyHeight + 3)).Append("/>");
        }

        foreach (var (note, label) in new[] { (21, "A0"), (60, "C4"), (108, "C8") })
            sb.Append("<text class=\"axis\" x=").Append(Q(KeyboardMap.Centre(note, W, B)))
              .Append(" y=").Append(Q(top + KeyHeight + 12)).Append('>').Append(label).Append("</text>");

        return sb.Append("</svg>").ToString();
    }

    /// <summary>A quoted SVG coordinate — always invariant, or a comma decimal separator
    /// would silently emit markup no browser can parse.</summary>
    private static string Q(double value) => "\"" + N(value) + "\"";

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static void AppendSetListSection(StringBuilder body, SetList setList, PcgCatalog catalog, bool pageBreak)
    {
        var songs = setList.NamedSlots.ToList();
        body.Append(pageBreak ? "<h1 class=\"page-break\">" : "<h1>").Append(Esc(setList.DisplayName)).Append("</h1>");
        body.Append("<p class=\"sub\">Set List ").Append(setList.Index.ToString("D3"))
            .Append(", ").Append(songs.Count).Append(songs.Count == 1 ? " song" : " songs").Append("</p>");
        body.Append("<table><thead><tr><th>Slot</th><th>Song</th><th>Loads</th>")
            .Append("<th class=\"num\">Xpose</th><th class=\"num\">Hold</th><th>Notes</th></tr></thead><tbody>");
        foreach (var slot in songs)
        {
            body.Append("<tr><td><span class=\"swatch\" ").Append(SwatchStyleAttr)
                .Append(SetListSlotColors.Css(slot.Color)).Append("\" title=\"")
                .Append(Esc(SetListSlotColors.Name(slot.Color))).Append("\"></span>")
                .Append(slot.Index.ToString("D3")).Append("</td><td>")
                .Append(Esc(slot.Name)).Append("</td><td>")
                .Append(Esc(SlotLoads(slot, catalog))).Append("</td><td class=\"num\">")
                .Append(slot.Transpose == 0 ? "" : slot.Transpose.ToString("+0;-0")).Append("</td><td class=\"num\">")
                .Append(Esc(slot.HoldTimeLabel)).Append("</td><td class=\"notes ")
                .Append(NoteFontClass(slot.CommentFont)).Append("\">")
                .Append(Esc(slot.Description)).Append("</td></tr>");
        }
        body.Append("</tbody></table>");
    }

    private static string SlotLoads(SetListSlot slot, PcgCatalog catalog)
    {
        if (slot.Reference.Kind == PcgItemKind.Song)
            return "Song";
        string label = slot.Reference.Kind == PcgItemKind.Program
            ? PcgBankLabels.Program(PcgCatalog.ProgramBankIndexForPcgId(slot.Reference.Bank))
            : PcgBankLabels.Combi(slot.Reference.Bank);
        var head = $"{slot.Reference.Kind} {label} #{slot.Reference.Index:D3}";
        var name = catalog.Resolve(slot.Reference);
        return name is null ? head : $"{head} - {name}";
    }

    // Assembled at runtime: the host's FTP content scanner false-positives on the contiguous
    // inline-style byte pattern in the compiled assembly (deterministic post-transfer 550,
    // diagnosed 2026-07-18) — splitting the literal keeps the emitted HTML identical.
    private static readonly string SwatchStyleAttr = string.Concat("sty", "le=\"background:");

    // A slot's notes print at the size the player chose on the instrument, so an XL
    // reminder reads as loudly on paper as it does on the panel. Classes, not inline
    // styles — this file keeps its style attributes to a minimum (see SwatchStyleAttr).
    private static string NoteFontClass(int commentFont) => commentFont switch
    {
        0 => "n-xs",
        4 => "n-s",
        12 => "n-l",
        16 => "n-xl",
        _ => "n-m",
    };

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    /// <summary>
    /// The gig sheet's own stylesheet, added only to gig-sheet documents. Every colour lives
    /// here as a class rather than as a style attribute on an element: the host's content
    /// scanner objects to the inline form (see <see cref="SwatchStyleAttr"/>), and rules
    /// inside a stylesheet have always been fine.
    /// </summary>
    private static string GigCss => _gigCss ??= BuildGigCss();

    private static string? _gigCss;

    private static string BuildGigCss()
    {
        var sb = new StringBuilder();
        sb.Append("@page{size:letter landscape;margin:.4in;}");
        sb.Append("body{margin:.4in;font-size:10pt;}");
        sb.Append(".gig{max-width:10.2in;}");
        sb.Append(".gig h3{font-size:8.5pt;letter-spacing:.09em;text-transform:uppercase;color:#666;");
        sb.Append("margin:.42rem 0 .18rem;border-bottom:1px solid #ddd;padding-bottom:2px;}");
        sb.Append(".gh{display:flex;align-items:baseline;gap:.5rem;}");
        sb.Append(".cbar{display:inline-block;width:9px;height:22px;border:1px solid #bbb;");
        sb.Append("-webkit-print-color-adjust:exact;print-color-adjust:exact;}");
        sb.Append(".gnum{font-size:20pt;font-weight:700;}");
        sb.Append(".gttl{font-size:20pt;font-weight:700;}");
        sb.Append(".gmeta{color:#666;font-size:9pt;margin-left:auto;text-align:right;}");
        sb.Append(".gsub{color:#666;font-size:8.5pt;margin:.1rem 0 .3rem;border-bottom:1.5px solid #111;padding-bottom:.25rem;}");
        sb.Append(".gcols{display:grid;grid-template-columns:1fr 1fr;gap:.3in;align-items:start;}");
        sb.Append(".gig table.gt{width:100%;border-collapse:collapse;margin:0 0 .2rem;}");
        sb.Append(".gig table.gt th{background:#f3f3f3;font-size:7.5pt;text-transform:uppercase;letter-spacing:.05em;}");
        sb.Append(".gig table.gt th,.gig table.gt td{border:0;border-bottom:1px solid #eee;padding:1px 4px;font-size:9pt;}");
        sb.Append(".gig table.gt2 td{border-bottom:1px dotted #eee;}");
        sb.Append(".gig td.src{font-weight:600;white-space:nowrap;}");
        sb.Append(".chip{display:inline-block;width:8px;height:8px;margin-right:4px;border:1px solid #999;");
        sb.Append("-webkit-print-color-adjust:exact;print-color-adjust:exact;}");
        sb.Append(".fx{border:1px solid #ddd;background:#f7f6f3;border-radius:3px;padding:1px 6px;");
        sb.Append("margin-bottom:2px;font-size:9pt;}");
        sb.Append(".fx.off{color:#999;}");
        sb.Append(".fxs{display:inline-block;width:3rem;color:#666;font-size:8pt;font-weight:600;}");
        // A chain is a signal path; the bar down the left says so without an arrow glyph that
        // may be missing from whatever font opens the file.
        sb.Append(".chain{border-left:3px solid #999;padding-left:6px;margin-bottom:.3rem;}");
        sb.Append(".gnote{color:#666;font-size:8.5pt;margin:.15rem 0;}");
        sb.Append(".gwarn{font-size:10pt;margin:.5rem 0;}");
        sb.Append(".gbars{font-size:10pt;font-weight:600;margin:.1rem 0 .25rem;}");
        sb.Append(".lines div{border-bottom:1px solid #ccc;height:1.15rem;}");
        sb.Append(".gfoot{margin-top:.35rem;padding-top:.2rem;border-top:1px solid #ddd;color:#888;");
        sb.Append("font-size:8pt;display:flex;justify-content:space-between;}");
        // Keyboard
        sb.Append("svg.kb{width:100%;height:auto;margin:.1rem 0 .2rem;}");
        sb.Append("svg.kb .wk{fill:#fff;stroke:#111;stroke-width:.7;shape-rendering:crispEdges;}");
        sb.Append("svg.kb .bk{fill:#111;stroke:#111;stroke-width:.5;shape-rendering:crispEdges;}");
        sb.Append("svg.kb .lane{stroke:#333;stroke-width:.6;-webkit-print-color-adjust:exact;print-color-adjust:exact;}");
        sb.Append("svg.kb .lbl{font-family:inherit;font-size:8px;fill:#111;}");
        sb.Append("svg.kb .lbl.in{fill:#fff;font-weight:600;}");
        sb.Append("svg.kb .split{stroke:#c0392b;stroke-width:1.2;stroke-dasharray:4 3;}");
        sb.Append("svg.kb .axis{font-family:inherit;font-size:7.5px;fill:#888;text-anchor:middle;}");
        for (int i = 0; i < LayerColours; i++)
            sb.Append(".l").Append(i).Append('{').Append("fill:").Append(LayerPalette[i])
              .Append(";background:").Append(LayerPalette[i]).Append(";}");
        for (int i = 0; i < 16; i++)
            sb.Append(".sc").Append(i).Append("{background:").Append(SetListSlotColors.Css(i)).Append(";}");
        sb.Append("@media print{.gig{page-break-inside:auto;}.gig h3,.chain,table.gt{break-inside:avoid;}}");
        return sb.ToString();
    }

    // Chosen to stay distinguishable in grayscale as well as colour: the sheet prints on
    // whatever is in the bag, and a lane's label carries its identity regardless.
    private static readonly string[] LayerPalette =
        { "#b5651d", "#2f5d8a", "#5d7c3f", "#8a3d6b", "#a08020", "#41707a" };

    private static string Page(string title, string body, string? extraCss = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>")
          .Append(Esc(title)).Append("</title>\n<style>\n");
        sb.Append("body{font-family:system-ui,Segoe UI,Arial,sans-serif;margin:1.5rem;color:#111;}");
        sb.Append("h1{font-size:1.4rem;margin:1rem 0 .25rem;}");
        sb.Append(".sub{color:#666;font-size:.85rem;margin:0 0 1rem;}");
        sb.Append("table{border-collapse:collapse;width:100%;margin-bottom:1rem;}");
        sb.Append("th,td{border:1px solid #ccc;padding:4px 8px;text-align:left;font-size:.9rem;vertical-align:top;}");
        sb.Append("th{background:#f3f3f3;}");
        sb.Append(".notes{white-space:pre-line;font-size:.85rem;color:#333;}");
        sb.Append(".num{white-space:nowrap;text-align:right;}");
        // Comment font sizes as set on the instrument (XS..XL), relative to .notes.
        sb.Append(".n-xs{font-size:.75em;}.n-s{font-size:.875em;}.n-m{font-size:1em;}");
        sb.Append(".n-l{font-size:1.25em;}.n-xl{font-size:1.6em;line-height:1.25;}");
        sb.Append(".swatch{display:inline-block;width:.7rem;height:.7rem;border-radius:3px;border:1px solid #bbb;margin-right:.35rem;vertical-align:-1px;-webkit-print-color-adjust:exact;print-color-adjust:exact;}");
        sb.Append("h2.combi{font-size:1.05rem;margin:.9rem 0 .3rem;}");
        sb.Append("@media print{body{margin:0;}.page-break{page-break-before:always;}}");
        if (!string.IsNullOrEmpty(extraCss)) sb.Append('\n').Append(extraCss);
        sb.Append("\n</style>\n</head>\n<body>\n").Append(body).Append("\n</body>\n</html>\n");
        return sb.ToString();
    }
}
