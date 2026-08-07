using System.Globalization;
using System.Text;

namespace PcgUtil.Core;

/// <summary>The two typefaces a gig sheet uses. Both are PDF standard fonts.</summary>
public enum PdfFont
{
    Helvetica,
    HelveticaBold,
}

/// <summary>
/// Writes a PDF containing exactly what this app draws: fixed-size pages of rectangles,
/// lines and short text runs. Small enough to own, and owning it buys three things the
/// project cares about.
///
/// It adds no dependency — the host's content scanner has rejected specific assemblies
/// outright before, and a new third-party DLL would only fail at deploy time. It needs no
/// font file: the <em>standard</em> fonts (Helvetica here) are built into every PDF reader,
/// so there is nothing to embed, resolve, or find missing on a server. And with no dates or
/// file ids written, the same page always produces the same bytes, which is a thing a test
/// can hold on to.
///
/// Text is encoded as WinAnsi (CP1252), which covers everything the sheets emit including
/// the en dash in "C-1–A4" and the middle dot between fields. Characters outside it become
/// "?" rather than corrupting the stream.
/// </summary>
public sealed class PdfWriter
{
    private readonly List<(double Width, double Height, string Content)> _pages = new();
    private readonly StringBuilder _page = new();
    private double _width, _height;
    private bool _open;

    /// <summary>US Letter, landscape, in points.</summary>
    public const double LetterLandscapeWidth = 792;
    public const double LetterLandscapeHeight = 612;

    public int PageCount => _pages.Count + (_open ? 1 : 0);

    public void BeginPage(double width = LetterLandscapeWidth, double height = LetterLandscapeHeight)
    {
        EndPage();
        _width = width;
        _height = height;
        _page.Clear();
        _open = true;
    }

    private void EndPage()
    {
        if (!_open) return;
        _pages.Add((_width, _height, _page.ToString()));
        _open = false;
    }

    public void FillRect(double x, double y, double w, double h, string rgb)
    {
        if (w <= 0 || h <= 0) return;
        _page.Append(Colour(rgb, fill: true))
             .Append(N(x)).Append(' ').Append(N(y)).Append(' ').Append(N(w)).Append(' ')
             .Append(N(h)).Append(" re f\n");
    }

    public void StrokeRect(double x, double y, double w, double h, string rgb, double lineWidth = 0.7)
    {
        if (w <= 0 || h <= 0) return;
        _page.Append(Colour(rgb, fill: false)).Append(N(lineWidth)).Append(" w ")
             .Append(N(x)).Append(' ').Append(N(y)).Append(' ').Append(N(w)).Append(' ')
             .Append(N(h)).Append(" re S\n");
    }

    /// <summary>A rectangle with both a fill and an outline, in one pass.</summary>
    public void Box(double x, double y, double w, double h, string fill, string stroke, double lineWidth = 0.7)
    {
        if (w <= 0 || h <= 0) return;
        _page.Append(Colour(fill, fill: true)).Append(Colour(stroke, fill: false))
             .Append(N(lineWidth)).Append(" w ")
             .Append(N(x)).Append(' ').Append(N(y)).Append(' ').Append(N(w)).Append(' ')
             .Append(N(h)).Append(" re B\n");
    }

    public void Line(double x1, double y1, double x2, double y2, string rgb,
                     double lineWidth = 0.7, double[]? dash = null)
    {
        _page.Append(Colour(rgb, fill: false)).Append(N(lineWidth)).Append(" w ");
        if (dash is { Length: > 0 })
            _page.Append('[').Append(string.Join(' ', dash.Select(N))).Append("] 0 d ");
        _page.Append(N(x1)).Append(' ').Append(N(y1)).Append(" m ")
             .Append(N(x2)).Append(' ').Append(N(y2)).Append(" l S\n");
        if (dash is { Length: > 0 })
            _page.Append("[] 0 d\n");
    }

    public void Text(string text, double x, double y, PdfFont font, double size, string rgb = "000000")
    {
        if (string.IsNullOrEmpty(text)) return;
        _page.Append(Colour(rgb, fill: true)).Append("BT /")
             .Append(font == PdfFont.HelveticaBold ? "F2" : "F1").Append(' ').Append(N(size))
             .Append(" Tf ").Append(N(x)).Append(' ').Append(N(y)).Append(" Td ")
             .Append(Literal(text)).Append(" Tj ET\n");
    }

    public void TextRight(string text, double rightEdge, double y, PdfFont font, double size,
                          string rgb = "000000") =>
        Text(text, rightEdge - Measure(text, font, size), y, font, size, rgb);

    public void TextCentre(string text, double centre, double y, PdfFont font, double size,
                           string rgb = "000000") =>
        Text(text, centre - Measure(text, font, size) / 2, y, font, size, rgb);

    /// <summary>Width of a string in points, from the standard font metrics.</summary>
    public static double Measure(string text, PdfFont font, double size)
    {
        ArgumentNullException.ThrowIfNull(text);
        var widths = font == PdfFont.HelveticaBold ? HelveticaBoldWidths : HelveticaWidths;
        double total = 0;
        foreach (char c in text)
        {
            int code = WinAnsi(c);
            total += widths[code - 32];
        }
        return total * size / 1000.0;
    }

    /// <summary>The text, shortened with an ellipsis until it fits.</summary>
    public static string Truncate(string text, PdfFont font, double size, double maxWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Measure(text, font, size) <= maxWidth) return text;
        for (int len = text.Length - 1; len > 0; len--)
        {
            string candidate = text[..len].TrimEnd() + "…";
            if (Measure(candidate, font, size) <= maxWidth) return candidate;
        }
        return "";
    }

    public byte[] ToBytes()
    {
        EndPage();
        if (_pages.Count == 0) BeginPage();
        EndPage();

        // Objects: 1 catalog, 2 page tree, 3 Helvetica, 4 Helvetica-Bold, then a page and a
        // content stream for each page.
        const int fixedObjects = 4;
        int pageIds = _pages.Count;
        var body = new List<byte[]>();
        var ids = new List<string>();

        var kids = string.Join(' ', Enumerable.Range(0, pageIds)
            .Select(i => $"{fixedObjects + 1 + i * 2} 0 R"));

        ids.Add($"<< /Type /Catalog /Pages 2 0 R >>");
        ids.Add($"<< /Type /Pages /Count {pageIds} /Kids [{kids}] >>");
        ids.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        ids.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        for (int i = 0; i < pageIds; i++)
        {
            var (w, h, content) = _pages[i];
            int contentId = fixedObjects + 2 + i * 2;
            ids.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(w)} {N(h)}] "
                + $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
            ids.Add($"__STREAM__{i}");
        }

        var file = new MemoryStream();
        void Write(string s) => file.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");
        file.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });   // binary marker

        var offsets = new long[ids.Count + 1];
        for (int i = 0; i < ids.Count; i++)
        {
            offsets[i + 1] = file.Position;
            Write($"{i + 1} 0 obj\n");
            if (ids[i].StartsWith("__STREAM__", StringComparison.Ordinal))
            {
                var bytes = Latin(_pages[int.Parse(ids[i][10..], CultureInfo.InvariantCulture)].Content);
                Write($"<< /Length {bytes.Length} >>\nstream\n");
                file.Write(bytes);
                Write("\nendstream\n");
            }
            else
            {
                Write(ids[i] + "\n");
            }
            Write("endobj\n");
        }

        long xref = file.Position;
        Write($"xref\n0 {ids.Count + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= ids.Count; i++)
            Write($"{offsets[i]:D10} 00000 n \n");
        // No /ID and no /Info: nothing in this file varies between runs, so the same sheet
        // always produces the same bytes and a test can pin them.
        Write($"trailer\n<< /Size {ids.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return file.ToArray();
    }

    private static string Colour(string rgb, bool fill)
    {
        int value = int.Parse(rgb.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        string r = N((value >> 16 & 0xFF) / 255.0), g = N((value >> 8 & 0xFF) / 255.0), b = N((value & 0xFF) / 255.0);
        return $"{r} {g} {b} {(fill ? "rg" : "RG")} ";
    }

    /// <summary>A PDF string literal: WinAnsi bytes with the three delimiters escaped.</summary>
    private static string Literal(string text)
    {
        var sb = new StringBuilder("(");
        foreach (char c in text)
        {
            int code = WinAnsi(c);
            if (code is '(' or ')' or '\\') sb.Append('\\');
            sb.Append((char)code);
        }
        return sb.Append(')').ToString();
    }

    /// <summary>Content streams are written byte for byte — one char, one WinAnsi byte.</summary>
    private static byte[] Latin(string content)
    {
        var bytes = new byte[content.Length];
        for (int i = 0; i < content.Length; i++) bytes[i] = (byte)content[i];
        return bytes;
    }

    /// <summary>
    /// A character's WinAnsi code, or '?' when the encoding has no glyph for it. ASCII passes
    /// straight through; the handful above it that this app actually emits are mapped by hand
    /// rather than by dragging in a codepage.
    /// </summary>
    private static int WinAnsi(char c) => c switch
    {
        >= ' ' and <= '~' => c,
        '–' => 0x96,   // en dash, as in "C-1–A4"
        '—' => 0x97,   // em dash
        '‘' => 0x91,
        '’' => 0x92,
        '“' => 0x93,
        '”' => 0x94,
        '•' => 0x95,   // bullet
        '…' => 0x85,   // ellipsis, used when a name is shortened to fit
        '·' => 0xB7,   // middle dot, the separator between header fields
        '°' => 0xB0,
        '©' => 0xA9,
        '®' => 0xAE,
        >= ' ' and <= 'ÿ' => c,   // Latin-1 supplement shares CP1252's upper half
        _ => '?',
    };

    private static string N(double value)
    {
        double rounded = Math.Round(value, 2);
        return rounded == Math.Floor(rounded)
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // Standard Helvetica metrics, per 1000 em, indexed by WinAnsi code from 32. Taken from
    // the published AFMs rather than typed from memory (space 278, "A" 667, "0" 556).
    private static readonly short[] HelveticaWidths =
    {
         278,  278,  355,  556,  556,  889,  667,  191,  333,  333,  389,  584,  278,  333,  278,  278,
         556,  556,  556,  556,  556,  556,  556,  556,  556,  556,  278,  278,  584,  584,  584,  556,
        1015,  667,  667,  722,  722,  667,  611,  778,  722,  278,  500,  667,  556,  833,  722,  778,
         667,  778,  722,  667,  611,  722,  667,  944,  667,  667,  611,  278,  278,  278,  469,  556,
         333,  556,  556,  500,  556,  556,  278,  556,  556,  222,  222,  500,  222,  833,  556,  556,
         556,  556,  333,  500,  278,  556,  500,  722,  500,  500,  500,  334,  260,  334,  584,  350,
         556,  350,  222,  556,  333, 1000,  556,  556,  333, 1000,  667,  333, 1000,  350,  611,  350,
         350,  222,  222,  333,  333,  350,  556, 1000,  333, 1000,  500,  333,  944,  350,  500,  667,
         278,  333,  556,  556,  556,  556,  260,  556,  333,  737,  370,  556,  584,  333,  737,  333,
         400,  584,  333,  333,  333,  556,  537,  278,  333,  333,  365,  556,  834,  834,  834,  611,
         667,  667,  667,  667,  667,  667, 1000,  722,  667,  667,  667,  667,  278,  278,  278,  278,
         722,  722,  778,  778,  778,  778,  778,  584,  778,  722,  722,  722,  722,  667,  667,  611,
         556,  556,  556,  556,  556,  556,  889,  500,  556,  556,  556,  556,  278,  278,  278,  278,
         556,  556,  556,  556,  556,  556,  556,  584,  611,  556,  556,  556,  556,  500,  556,  500,
    };

    private static readonly short[] HelveticaBoldWidths =
    {
         278,  333,  474,  556,  556,  889,  722,  238,  333,  333,  389,  584,  278,  333,  278,  278,
         556,  556,  556,  556,  556,  556,  556,  556,  556,  556,  333,  333,  584,  584,  584,  611,
         975,  722,  722,  722,  722,  667,  611,  778,  722,  278,  556,  722,  611,  833,  722,  778,
         667,  778,  722,  667,  611,  722,  667,  944,  667,  667,  611,  333,  278,  333,  584,  556,
         333,  556,  611,  556,  611,  556,  333,  611,  611,  278,  278,  556,  278,  889,  611,  611,
         611,  611,  389,  556,  333,  611,  556,  778,  556,  556,  500,  389,  280,  389,  584,  350,
         556,  350,  278,  556,  500, 1000,  556,  556,  333, 1000,  667,  333, 1000,  350,  611,  350,
         350,  278,  278,  500,  500,  350,  556, 1000,  333, 1000,  556,  333,  944,  350,  500,  667,
         278,  333,  556,  556,  556,  556,  280,  556,  333,  737,  370,  556,  584,  333,  737,  333,
         400,  584,  333,  333,  333,  611,  556,  278,  333,  333,  365,  556,  834,  834,  834,  611,
         722,  722,  722,  722,  722,  722, 1000,  722,  667,  667,  667,  667,  278,  278,  278,  278,
         722,  722,  778,  778,  778,  778,  778,  584,  778,  722,  722,  722,  722,  667,  667,  611,
         556,  556,  556,  556,  556,  556,  889,  556,  556,  556,  556,  556,  278,  278,  278,  278,
         611,  611,  611,  611,  611,  611,  611,  584,  611,  611,  611,  611,  611,  556,  611,  556,
    };
}
