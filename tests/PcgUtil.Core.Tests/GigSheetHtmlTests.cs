using System.Text;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The printed page itself: that it is a whole document, that the keyboard really has 88
/// keys on it, and — the one that matters operationally — that it never emits an inline
/// background style. The host's content scanner rejects the uploaded assembly when that byte
/// pattern appears contiguously (a deterministic post-transfer 550, diagnosed 2026-07-18),
/// which is why <c>SwatchStyleAttr</c> exists and why the gig sheet colours everything
/// through classes instead.
/// </summary>
public class GigSheetHtmlTests
{
    private static string SheetHtml(out GigSheet sheet)
    {
        var pcg = GigFile.Parse() ?? Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var list = SetListReader.Read(pcg).First(l => l.NamedSlots.Any());
        sheet = GigSheet.Build(pcg, catalog, list, list.NamedSlots.First());
        return PcgHtmlReport.GigSheet(sheet, "sample.PCG");
    }

    [Fact]
    public void The_sheet_is_a_complete_landscape_document()
    {
        string html = SheetHtml(out var sheet);

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>\n", html, StringComparison.Ordinal);
        Assert.Contains("size:letter landscape", html, StringComparison.Ordinal);
        Assert.Contains("<svg class=\"kb\"", html, StringComparison.Ordinal);
        // Escaped, not raw: slot names carry apostrophes ("Let's Go Crazy").
        Assert.Contains(System.Net.WebUtility.HtmlEncode(sheet.Slot.Name), html, StringComparison.Ordinal);
        Assert.Contains("sample.PCG", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_keyboard_has_all_88_keys()
    {
        string html = SheetHtml(out _);
        Assert.Equal(52, Occurrences(html, "class=\"wk\""));
        Assert.Equal(36, Occurrences(html, "class=\"bk\""));
    }

    [Fact]
    public void A_lane_is_drawn_and_labelled_for_every_sounding_layer()
    {
        string html = SheetHtml(out var sheet);
        int lanes = Math.Min(sheet.Layers.Count, 8);
        Assert.Equal(lanes, Occurrences(html, "class=\"lane l"));
        // Print drops background colour by default, so a lane must never rely on its fill
        // alone — each carries a label naming the timbre it belongs to.
        foreach (var layer in sheet.Layers.Take(lanes))
            Assert.Contains($"T{layer.Number} ", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scanner guard. Two halves: the emitted page carries no style attribute at all,
    /// and the compiled assembly contains no contiguous inline-background literal that a
    /// future edit might have reintroduced.
    /// </summary>
    [Fact]
    public void Nothing_emits_an_inline_style_attribute()
    {
        string html = SheetHtml(out _);
        Assert.DoesNotContain("style=", html, StringComparison.Ordinal);

        var assembly = File.ReadAllBytes(typeof(PcgHtmlReport).Assembly.Location);
        var forbidden = Encoding.ASCII.GetBytes("style=\"background:");
        Assert.Equal(-1, IndexOf(assembly, forbidden));
    }

    [Fact]
    public void Numbers_are_written_invariantly()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // A comma decimal separator would emit coordinates no browser can parse.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            string html = SheetHtml(out _);
            int svg = html.IndexOf("<svg", StringComparison.Ordinal);
            int end = html.IndexOf("</svg>", StringComparison.Ordinal);
            string markup = html[svg..end];
            Assert.DoesNotContain(",", markup.Replace("stroke-dasharray:", "", StringComparison.Ordinal),
                StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_whole_set_list_prints_one_sheet_per_page()
    {
        var pcg = GigFile.Parse() ?? Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var list = SetListReader.Read(pcg).First(l => l.NamedSlots.Count() > 3);
        var sheets = list.NamedSlots.Take(4)
            .Select(s => GigSheet.Build(pcg, catalog, list, s)).ToList();

        string html = PcgHtmlReport.GigSheets(sheets, "sample.PCG");
        Assert.Equal(4, Occurrences(html, "<section class=\"gig"));
        // Between sheets, never before the first. Counted on the section itself — the string
        // "page-break" also appears in the stylesheet that defines the class.
        Assert.Equal(3, Occurrences(html, "<section class=\"gig page-break\""));
        Assert.DoesNotContain("style=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_with_markup_characters_are_escaped()
    {
        string html = SheetHtml(out _);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        // The sample carries slots like "Let's Go Crazy"; apostrophes must survive encoded.
        if (html.Contains("Let", StringComparison.Ordinal))
            Assert.DoesNotContain("Let's", html, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < needle.Length && hit; j++)
                if (haystack[i + j] != needle[j]) hit = false;
            if (hit) return i;
        }
        return -1;
    }
}
