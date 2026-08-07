namespace PcgUtil.Core;

/// <summary>
/// Draws gig sheets onto PDF pages — one landscape page per sound, laid out for a music
/// stand: which layers sound and where they sit on the keyboard, the effects, and what the
/// player's hands and feet actually move.
///
/// The layout is arithmetic rather than a stylesheet, which is the point of doing it here:
/// the pages land exactly where they are put, instead of wherever a browser's print dialogue
/// decides. Coordinates run from the top of the page (<see cref="Y"/> flips them for PDF's
/// bottom-left origin), and the keyboard is drawn at its natural size — 52 white keys of
/// 14 pt is 728 pt, which is precisely what fits between the margins.
/// </summary>
public static class GigSheetPdf
{
    private const double PageW = PdfWriter.LetterLandscapeWidth;    // 792
    private const double PageH = PdfWriter.LetterLandscapeHeight;   // 612
    private const double Margin = 28.8;                             // 0.4 inch
    private const double Right = PageW - Margin;
    private const double ColumnGap = 34.4;
    private const double ColumnW = (PageW - 2 * Margin - ColumnGap) / 2;   // 350
    private const double RightColumnX = Margin + ColumnW + ColumnGap;

    private const string Ink = "111111", Muted = "666666", Faint = "888888";
    private const string Rule = "dddddd", RowRule = "eeeeee", HeadFill = "f3f3f3";
    private const string BoxFill = "f7f6f3", Split = "c0392b";

    // Distinguishable in colour and, because a printer may drop backgrounds entirely, always
    // paired with a label and an outline.
    private static readonly string[] LayerColours =
        { "b5651d", "2f5d8a", "5d7c3f", "8a3d6b", "a08020", "41707a" };

    public static byte[] Render(IReadOnlyList<GigSheet> sheets, string? sourceFile = null)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        var pdf = new PdfWriter();
        foreach (var sheet in sheets)
        {
            pdf.BeginPage(PageW, PageH);
            Draw(pdf, sheet, sourceFile);
        }
        return pdf.ToBytes();
    }

    public static byte[] Render(GigSheet sheet, string? sourceFile = null) =>
        Render(new[] { sheet }, sourceFile);

    /// <summary>PDF measures up from the bottom; the layout below thinks from the top.</summary>
    private static double Y(double fromTop) => PageH - fromTop;

    private static void Draw(PdfWriter pdf, GigSheet sheet, string? sourceFile)
    {
        double top = Margin;

        // ----- header -----
        // A set-list song wears its slot colour; a combi printed on its own has none.
        pdf.Box(Margin, Y(top + 22), 9, 22,
            sheet.Colour is { } colour ? SetListSlotColors.Css(colour).TrimStart('#') : "ffffff",
            "bbbbbb", 0.5);
        double titleBase = top + 17;
        string number = sheet.Number;
        double nameX = Margin + 15;
        if (number.Length > 0)
        {
            pdf.Text(number, nameX, Y(titleBase), PdfFont.HelveticaBold, 20, Ink);
            nameX += PdfWriter.Measure(number, PdfFont.HelveticaBold, 20) + 10;
        }
        string title = PdfWriter.Truncate(sheet.Title, PdfFont.HelveticaBold, 20, 330);
        pdf.Text(title, nameX, Y(titleBase), PdfFont.HelveticaBold, 20, Ink);

        // The meta line shares the band with the title, and a combi running four KARMA
        // modules has plenty to say — so it gets only the room the title leaves it.
        double metaRoom = Right - (nameX + PdfWriter.Measure(title, PdfFont.HelveticaBold, 20)) - 14;
        pdf.TextRight(PdfWriter.Truncate(Meta(sheet), PdfFont.Helvetica, 9, metaRoom),
            Right, Y(top + 8), PdfFont.Helvetica, 9, Muted);
        if (sheet.TargetName is { } target)
            pdf.TextRight(PdfWriter.Truncate(target, PdfFont.Helvetica, 9, metaRoom),
                Right, Y(top + 19), PdfFont.Helvetica, 9, Ink);

        top += 26;
        var pieces = new List<string>();
        if (sheet.SlotSettings is { } slotSettings) pieces.Add(slotSettings);
        if (sheet.Karma.Count > 0)
            pieces.Add("KARMA " + string.Join(", ", sheet.Karma.Select(k => $"{k.Module} {k.Display}")));
        string settings = string.Join(" · ", pieces);
        double settingsRoom = sheet.AlsoAt.Count > 0 ? ColumnW + 40 : ColumnW * 2;
        pdf.Text(PdfWriter.Truncate(settings, PdfFont.Helvetica, 8.5, settingsRoom),
            Margin, Y(top + 7), PdfFont.Helvetica, 8.5, Muted);
        if (sheet.AlsoAt.Count > 0)
            pdf.TextRight(PdfWriter.Truncate(Repeats(sheet), PdfFont.Helvetica, 8.5, ColumnW),
                Right, Y(top + 7), PdfFont.Helvetica, 8.5, Muted);
        top += 12;
        pdf.Line(Margin, Y(top), Right, Y(top), Ink, 1.5);
        top += 4;

        if (sheet.Unavailable is { } why)
        {
            pdf.Text(why, Margin, Y(top + 20), PdfFont.Helvetica, 10, Ink);
            Footer(pdf, sourceFile);
            return;
        }

        // ----- keyboard -----
        top = Heading(pdf, "WHAT PLAYS WHERE", Margin, top + 6, ColumnW * 2);
        top = Keyboard(pdf, sheet, top + 2);

        // ----- left column -----
        double left = top + 6;
        left = Heading(pdf, "LAYERS", Margin, left, ColumnW);
        left = Layers(pdf, sheet, left);
        if (sheet.Silent.Count > 0)
        {
            string reason = sheet.Silent.Select(s => s.SilentReason).Distinct().Count() == 1
                ? " (" + sheet.Silent[0].SilentReason + ")" : "";
            left = Note(pdf, $"{sheet.Silent.Count} other timbre"
                + (sheet.Silent.Count == 1 ? " is" : "s are") + " loaded but never sound"
                + (sheet.Silent.Count == 1 ? "s" : "") + " from the keyboard" + reason + ".",
                Margin, left + 2, ColumnW);
        }
        var tweaked = sheet.Layers.Where(l => l.Tweaks.Count > 0).ToList();
        if (tweaked.Count > 0)
            Tweaks(pdf, tweaked, left + 6);

        // ----- right column -----
        double right = top + 6;
        right = Heading(pdf, "EFFECTS", RightColumnX, right, ColumnW);
        right = Effects(pdf, sheet, right);
        right = Heading(pdf, "WHAT MOVES WHAT · WHILE YOU PLAY", RightColumnX, right + 6, ColumnW);
        right = Controls(pdf, sheet, right);
        Notes(pdf, sheet, right + 6);

        Footer(pdf, sourceFile);
    }

    private static string Meta(GigSheet sheet) =>
        sheet.Tempo > 0 ? $"{sheet.Source} · {sheet.Tempo:0.##} BPM" : sheet.Source;

    /// <summary>
    /// The other songs on this page. Slot settings belong to a slot, so when a repeat differs
    /// the page says which — printing one slot's volume under three songs' names would be a
    /// quiet lie.
    /// </summary>
    private static string Repeats(GigSheet sheet)
    {
        string also = "also played at " + string.Join(", ", sheet.AlsoAt.Select(s => s.Index.ToString("D3")));
        var differing = sheet.DifferingSlots.ToList();
        return differing.Count == 0 ? also
            : also + " — " + string.Join(", ", differing.Select(s =>
                $"{s.Index:D3} volume {s.Volume}"
                + (s.Transpose == sheet.Slot?.Transpose ? "" : $", transpose {s.Transpose:+0;-0}")
                + (s.HoldTimeIndex == sheet.Slot?.HoldTimeIndex ? "" : $", hold {s.HoldTimeLabel}")));
    }

    private static double Heading(PdfWriter pdf, string text, double x, double top, double width)
    {
        pdf.Text(text, x, Y(top + 7), PdfFont.HelveticaBold, 7.5, Muted);
        pdf.Line(x, Y(top + 10), x + width, Y(top + 10), Rule, 0.6);
        return top + 14;
    }

    private static double Note(PdfWriter pdf, string text, double x, double top, double width)
    {
        foreach (string line in Wrap(text, PdfFont.Helvetica, 8, width))
        {
            pdf.Text(line, x, Y(top + 6), PdfFont.Helvetica, 8, Muted);
            top += 9.5;
        }
        return top;
    }

    private static double Keyboard(PdfWriter pdf, GigSheet sheet, double top)
    {
        const double W = 14, B = 8.4, KeyH = 46, LaneH = 13, LaneGap = 3;
        var layers = sheet.Layers.Take(8).ToList();
        double laneTop = top;
        double keysTop = top + layers.Count * (LaneH + LaneGap);

        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var (l, r) = KeyboardMap.ZoneSpan(layer.BottomKey, layer.TopKey, W, B);
            double x = Margin + l, w = Math.Max(r - l, 2), y = laneTop + i * (LaneH + LaneGap);
            pdf.Box(x, Y(y + LaneH), w, LaneH, LayerColours[i % LayerColours.Length], "333333", 0.6);

            string label = $"T{layer.Number} {layer.Program}"
                + (layer.Transpose == 0 ? "" : $" {layer.Transpose:+0;-0}");
            bool inside = PdfWriter.Measure(label, PdfFont.HelveticaBold, 8) + 10 < w;
            if (inside)
                pdf.Text(label, x + 5, Y(y + 9.5), PdfFont.HelveticaBold, 8, "ffffff");
            else
                pdf.Text(PdfWriter.Truncate(label, PdfFont.Helvetica, 8, Right - (x + w) - 6),
                    x + w + 4, Y(y + 9.5), PdfFont.Helvetica, 8, Ink);
        }

        for (int n = KeyboardMap.LowestKey; n <= KeyboardMap.HighestKey; n++)
            if (!KeyboardMap.IsBlack(n))
                pdf.Box(Margin + KeyboardMap.Span(n, W, B).Left, Y(keysTop + KeyH), W, KeyH, "ffffff", Ink, 0.7);
        for (int n = KeyboardMap.LowestKey; n <= KeyboardMap.HighestKey; n++)
            if (KeyboardMap.IsBlack(n))
                pdf.FillRect(Margin + KeyboardMap.Span(n, W, B).Left, Y(keysTop + KeyH * 0.62), B, KeyH * 0.62, Ink);

        foreach (double x in layers
            .Select(l => KeyboardMap.ZoneSpan(l.BottomKey, l.TopKey, W, B).Left)
            .Where(x => x > 1).Select(x => Math.Round(x, 2)).Distinct())
            pdf.Line(Margin + x, Y(keysTop - 3), Margin + x, Y(keysTop + KeyH + 3), Split, 1.2,
                new double[] { 4, 3 });

        foreach (var (note, label) in new[] { (21, "A0"), (60, "C4"), (108, "C8") })
            pdf.TextCentre(label, Margin + KeyboardMap.Centre(note, W, B), Y(keysTop + KeyH + 9),
                PdfFont.Helvetica, 7.5, Faint);

        return keysTop + KeyH + 12;
    }

    private static double Layers(PdfWriter pdf, GigSheet sheet, double top)
    {
        double[] columns = { 0, 24, 150, 200, 262, 300 };   // T, program, engine, keys, vel, vol
        string[] heads = { "T", "PROGRAM", "ENGINE", "KEYS", "VEL", "VOL" };

        pdf.FillRect(Margin, Y(top + 10), ColumnW, 10, HeadFill);
        for (int i = 0; i < heads.Length; i++)
            pdf.Text(heads[i], Margin + columns[i] + (i == 0 ? 10 : 0), Y(top + 7.5), PdfFont.HelveticaBold, 6.5, Muted);
        top += 11;

        int index = 0;
        foreach (var layer in sheet.Layers)
        {
            string colour = LayerColours[index++ % LayerColours.Length];
            pdf.Box(Margin, Y(top + 8), 7, 7, colour, "999999", 0.5);
            pdf.Text(layer.Number.ToString(), Margin + 10, Y(top + 8), PdfFont.Helvetica, 9, Ink);
            pdf.Text(PdfWriter.Truncate(layer.Program, PdfFont.Helvetica, 9, 128),
                Margin + columns[1], Y(top + 8), PdfFont.Helvetica, 9, Ink);
            pdf.Text(layer.Engine ?? "", Margin + columns[2], Y(top + 8), PdfFont.Helvetica, 9, Muted);
            pdf.Text(layer.KeyRange, Margin + columns[3], Y(top + 8), PdfFont.Helvetica, 9, Ink);
            pdf.Text(layer.VelocityRange, Margin + columns[4], Y(top + 8), PdfFont.Helvetica, 9, Muted);
            pdf.Text(layer.Volume.ToString(), Margin + columns[5], Y(top + 8), PdfFont.Helvetica, 9, Ink);
            top += 11;
            pdf.Line(Margin, Y(top), Margin + ColumnW, Y(top), RowRule, 0.5);
        }
        return top;
    }

    private static void Tweaks(PdfWriter pdf, IReadOnlyList<GigLayer> tweaked, double top)
    {
        var layer = tweaked[0];
        top = Heading(pdf, $"T{layer.Number} TWEAKS · WHAT THIS COMBI CHANGES", Margin, top, ColumnW);
        if (layer.Drawbars is { } bars)
        {
            pdf.Text("Drawbars " + bars, Margin, Y(top + 8), PdfFont.HelveticaBold, 10, Ink);
            top += 13;
        }
        var named = layer.Tweaks.Where(t => t.Name is not null).ToList();
        foreach (var entry in named.Take(12))
        {
            pdf.Text(PdfWriter.Truncate(entry.Label, PdfFont.Helvetica, 9, 240),
                Margin, Y(top + 8), PdfFont.Helvetica, 9, Muted);
            pdf.TextRight(TweakValue(entry), Margin + ColumnW, Y(top + 8), PdfFont.Helvetica, 9, Ink);
            top += 10.5;
            pdf.Line(Margin, Y(top), Margin + ColumnW, Y(top), RowRule, 0.4);
        }
        var extra = new List<string>();
        if (named.Count > 12) extra.Add($"+{named.Count - 12} more");
        if (tweaked.Count > 1)
            extra.Add(string.Join(", ", tweaked.Skip(1).Select(l => $"T{l.Number}"))
                + (tweaked.Count == 2 ? " is tweaked too" : " are tweaked too"));
        if (extra.Count > 0) Note(pdf, string.Join(". ", extra) + ".", Margin, top + 2, ColumnW);
    }

    private static double Effects(PdfWriter pdf, GigSheet sheet, double top)
    {
        if (!sheet.Inserts.Any() && sheet.Masters.Count == 0)
            return Note(pdf, "No effects in this patch.", RightColumnX, top, ColumnW);

        foreach (var chain in sheet.Chains)
        {
            double chainTop = top;
            foreach (var step in chain.Steps) top = Effect(pdf, step, top, indent: 7);
            // One bar down the left says "these feed each other" without an arrow glyph.
            pdf.FillRect(RightColumnX, Y(top - 2), 3, top - chainTop - 2, "999999");
            top += 2;
        }
        foreach (var effect in sheet.Standalone) top = Effect(pdf, effect, top, indent: 0);
        foreach (var master in sheet.Masters) top = Effect(pdf, master, top, indent: 0);
        return top;
    }

    private static double Effect(PdfWriter pdf, GigEffect effect, double top, double indent)
    {
        double x = RightColumnX + indent, w = ColumnW - indent;
        pdf.Box(x, Y(top + 12), w, 12, BoxFill, Rule, 0.5);
        string colour = effect.IsOn ? Ink : "999999";
        pdf.Text(effect.Label, x + 5, Y(top + 8.5), PdfFont.HelveticaBold, 7.5, Muted);
        pdf.Text(PdfWriter.Truncate(effect.TypeName + (effect.IsOn ? "" : " (off)"),
                PdfFont.Helvetica, 9, w - 42),
            x + 34, Y(top + 8.5), PdfFont.Helvetica, 9, colour);
        return top + 14;
    }

    private static double Controls(PdfWriter pdf, GigSheet sheet, double top)
    {
        if (sheet.Controls.Count == 0)
            return Note(pdf, "Nothing in this patch responds to the panel controls.",
                RightColumnX, top, ColumnW);

        foreach (var control in sheet.Controls)
        {
            pdf.Text(PdfWriter.Truncate(control.Source, PdfFont.HelveticaBold, 8.5, 96),
                RightColumnX, Y(top + 8), PdfFont.HelveticaBold, 8.5, Ink);
            var lines = Wrap(control.Moves, PdfFont.Helvetica, 8.5, ColumnW - 100).Take(2).ToList();
            foreach (string line in lines)
            {
                pdf.Text(line, RightColumnX + 100, Y(top + 8), PdfFont.Helvetica, 8.5, Ink);
                top += 10;
            }
            if (lines.Count == 0) top += 10;
            pdf.Line(RightColumnX, Y(top), RightColumnX + ColumnW, Y(top), RowRule, 0.4);
        }
        return top;
    }

    private static void Notes(PdfWriter pdf, GigSheet sheet, double top)
    {
        top = Heading(pdf, "NOTES", RightColumnX, top, ColumnW);
        if (sheet.Notes.Length > 0)
        {
            double size = 9 * NoteScale(sheet.NotesFont);
            foreach (string paragraph in sheet.Notes.Split('\n'))
                foreach (string line in Wrap(paragraph, PdfFont.Helvetica, size, ColumnW))
                {
                    if (top > PageH - Margin - 24) return;   // never write over the footer
                    pdf.Text(line, RightColumnX, Y(top + size), PdfFont.Helvetica, size, "333333");
                    top += size * 1.35;
                }
            return;
        }
        // Ruled to the foot of the page: the space is there, and a sheet you can write on
        // during a rehearsal is worth more than a tidy margin.
        for (double line = top + 14; line < PageH - Margin - 22; line += 14)
            pdf.Line(RightColumnX, Y(line), RightColumnX + ColumnW, Y(line), "cccccc", 0.5);
    }

    /// <summary>Notes print at the size chosen on the instrument, so an XL reminder shouts.</summary>
    private static double NoteScale(int commentFont) => commentFont switch
    {
        0 => 0.75, 4 => 0.875, 12 => 1.25, 16 => 1.6, _ => 1.0,
    };

    private static void Footer(PdfWriter pdf, string? sourceFile)
    {
        double y = PageH - Margin;
        pdf.Line(Margin, Y(y - 12), Right, Y(y - 12), Rule, 0.5);
        pdf.Text(sourceFile ?? "", Margin, Y(y - 3), PdfFont.Helvetica, 8, Faint);
        pdf.TextRight("PCGUtil · read from the file, nothing played", Right, Y(y - 3),
            PdfFont.Helvetica, 8, Faint);
    }

    /// <summary>
    /// A tone-adjust reading in words where the instrument uses words. A two-state control
    /// names its states in its own label — "Perc Level (Soft/Loud)" — so "1" prints as
    /// "Loud", and a plain switch prints off/on rather than 0/1. Nobody reading a sheet in a
    /// hurry should have to remember which way round a switch goes.
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

    /// <summary>Greedy word wrap against the real font metrics.</summary>
    private static IEnumerable<string> Wrap(string text, PdfFont font, double size, double width)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var line = new System.Text.StringBuilder();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && PdfWriter.Measure(candidate, font, size) > width)
            {
                yield return line.ToString();
                line.Clear().Append(word);
            }
            else
            {
                line.Clear().Append(candidate);
            }
        }
        if (line.Length > 0) yield return PdfWriter.Truncate(line.ToString(), font, size, width);
    }
}
