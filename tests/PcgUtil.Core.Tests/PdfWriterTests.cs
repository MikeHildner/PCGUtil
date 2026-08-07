using System.Text;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The PDF writer. A malformed cross-reference table produces a file that opens in one
/// reader and fails in another, so the structural tests here matter more than what any page
/// happens to look like: every offset must land on the object it claims, and the same input
/// must always produce the same bytes.
/// </summary>
public class PdfWriterTests
{
    private static string Ascii(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static byte[] Sample()
    {
        var pdf = new PdfWriter();
        pdf.BeginPage();
        pdf.FillRect(10, 10, 100, 50, "b5651d");
        pdf.Line(0, 0, 100, 100, "c0392b", 1.2, new double[] { 4, 3 });
        pdf.Text("Footloose Chorus", 40, 500, PdfFont.HelveticaBold, 20);
        pdf.TextRight("C-1–A4 · 120 BPM", 700, 480, PdfFont.Helvetica, 9, "666666");
        pdf.BeginPage();
        pdf.TextCentre("A0", 100, 100, PdfFont.Helvetica, 7.5);
        return pdf.ToBytes();
    }

    [Fact]
    public void It_is_a_well_formed_pdf_file()
    {
        var pdf = Sample();
        string text = Ascii(pdf);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        Assert.Contains("/Type /Catalog", text, StringComparison.Ordinal);
        Assert.Contains("/Type /Pages /Count 2", text, StringComparison.Ordinal);
        Assert.Contains("/MediaBox [0 0 792 612]", text, StringComparison.Ordinal);   // Letter landscape
        Assert.Contains("/BaseFont /Helvetica /Encoding /WinAnsiEncoding", text, StringComparison.Ordinal);
        Assert.Contains("/BaseFont /Helvetica-Bold", text, StringComparison.Ordinal);
        // Nothing that varies run to run: no creation date, no file id.
        Assert.DoesNotContain("/CreationDate", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/ID", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The load-bearing one: every entry in the cross-reference table must point at the byte
    /// where its object actually starts, and startxref must point at the table.
    /// </summary>
    [Fact]
    public void Every_cross_reference_offset_lands_on_its_object()
    {
        var pdf = Sample();
        string text = Ascii(pdf);

        int startxrefAt = text.LastIndexOf("startxref", StringComparison.Ordinal);
        int xref = int.Parse(text[(startxrefAt + 9)..].Trim().Split('\n')[0]);
        Assert.Equal("xref", text.Substring(xref, 4));

        var lines = text[xref..].Split('\n');
        int count = int.Parse(lines[1].Split(' ')[1]);
        Assert.Equal("0000000000 65535 f ", lines[2]);   // the mandatory free entry

        for (int i = 1; i < count; i++)
        {
            long offset = long.Parse(lines[2 + i][..10]);
            Assert.EndsWith(" 00000 n ", lines[2 + i], StringComparison.Ordinal);
            Assert.StartsWith($"{i} 0 obj", text[(int)offset..], StringComparison.Ordinal);
        }
        Assert.Contains($"/Size {count}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_page_always_produces_the_same_bytes()
    {
        Assert.Equal(Sample(), Sample());
    }

    /// <summary>
    /// The characters the sheets actually emit have to survive: the en dash in a key range
    /// and the middle dot between header fields. WinAnsi carries both, which is why no font
    /// has to be embedded.
    /// </summary>
    [Fact]
    public void Text_is_encoded_as_winansi()
    {
        var pdf = new PdfWriter();
        pdf.BeginPage();
        pdf.Text("C-1–A4 · x", 0, 0, PdfFont.Helvetica, 9);
        var bytes = pdf.ToBytes();

        Assert.Contains((byte)0x96, bytes);   // en dash
        Assert.Contains((byte)0xB7, bytes);   // middle dot
        Assert.DoesNotContain("â€“", Ascii(bytes), StringComparison.Ordinal);   // not raw UTF-8
    }

    [Fact]
    public void Delimiters_are_escaped_and_unknown_characters_degrade()
    {
        var pdf = new PdfWriter();
        pdf.BeginPage();
        pdf.Text(@"a(b)c\d", 0, 0, PdfFont.Helvetica, 9);
        pdf.Text("♯ japanese: 日本", 0, 20, PdfFont.Helvetica, 9);
        string text = Ascii(pdf.ToBytes());

        Assert.Contains(@"(a\(b\)c\\d)", text, StringComparison.Ordinal);
        // No glyph in this encoding: a question mark, never a broken stream.
        Assert.Contains("(? japanese: ??)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Widths_match_the_published_font_metrics()
    {
        // Helvetica, per 1000 em: space 278, "A" 667, "0" 556. At 10 pt those are points.
        Assert.Equal(2.78, PdfWriter.Measure(" ", PdfFont.Helvetica, 10), 3);
        Assert.Equal(6.67, PdfWriter.Measure("A", PdfFont.Helvetica, 10), 3);
        Assert.Equal(5.56, PdfWriter.Measure("0", PdfFont.Helvetica, 10), 3);
        Assert.Equal(7.22, PdfWriter.Measure("A", PdfFont.HelveticaBold, 10), 3);
        Assert.Equal(5.56, PdfWriter.Measure("–", PdfFont.Helvetica, 10), 3);   // en dash
        Assert.Equal(2.78, PdfWriter.Measure("·", PdfFont.Helvetica, 10), 3);   // middle dot

        // Bold is wider than regular for the same words, and width scales with size.
        Assert.True(PdfWriter.Measure("Footloose", PdfFont.HelveticaBold, 10)
                  > PdfWriter.Measure("Footloose", PdfFont.Helvetica, 10));
        Assert.Equal(PdfWriter.Measure("Footloose", PdfFont.Helvetica, 20),
                     PdfWriter.Measure("Footloose", PdfFont.Helvetica, 10) * 2, 3);
    }

    [Fact]
    public void Truncation_fits_the_width_it_is_given()
    {
        const string name = "FOOTLOOSE-DIRTYORGAN";
        double full = PdfWriter.Measure(name, PdfFont.Helvetica, 9);

        Assert.Equal(name, PdfWriter.Truncate(name, PdfFont.Helvetica, 9, full + 1));
        string cut = PdfWriter.Truncate(name, PdfFont.Helvetica, 9, full / 2);
        Assert.True(PdfWriter.Measure(cut, PdfFont.Helvetica, 9) <= full / 2);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
        Assert.StartsWith("FOOT", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_document_still_produces_one_valid_page()
    {
        string text = Ascii(new PdfWriter().ToBytes());
        Assert.Contains("/Type /Pages /Count 1", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }
}
