using System.Text;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The printed pages. The paging rule is the one worth pinning: a set list that plays the
/// same combi three times should hand back one page that names the repeats, not three
/// identical pages, and the pages must still arrive in the order the songs are played.
/// </summary>
public class GigSheetPdfTests
{
    private static (PcgFile Pcg, PcgCatalog Catalog, SetList List) Gig()
    {
        var pcg = GigFile.Parse() ?? Sample.Parse();
        return (pcg, PcgCatalog.Build(pcg), SetListReader.Read(pcg).First(l => l.NamedSlots.Any()));
    }

    private static string Content(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void Repeated_songs_share_one_page_placed_where_they_first_play()
    {
        if (GigFile.Parse() is not { } pcg) return;
        var catalog = PcgCatalog.Build(pcg);
        var list = SetListReader.Read(pcg)[0];
        Assert.Equal("PART TIME GENIUS REPRISE", list.DisplayName);

        var pages = GigSheet.BuildPages(pcg, catalog, list, list.NamedSlots.Select(s => s.Index));

        // 23 songs, but three of them are the same chorus and two the same verse.
        Assert.Equal(20, pages.Count);
        Assert.Equal(23, pages.Sum(p => 1 + p.AlsoAt.Count));

        var chorus = Assert.Single(pages, p => p.Slot.Name == "Footloose Chorus");
        Assert.Equal(18, chorus.Slot.Index);                                  // first played at 018
        Assert.Equal(new[] { 20, 22 }, chorus.AlsoAt.Select(s => s.Index));   // and again at 020, 022
        Assert.All(chorus.AlsoAt, s => Assert.Equal("FOOTLOOSE CHORUS!", catalog.Resolve(s.Reference)));

        // Set order is preserved, so a printed stack reads the way the gig runs.
        Assert.Equal(pages.Select(p => p.Slot.Index).OrderBy(i => i), pages.Select(p => p.Slot.Index));
        Assert.DoesNotContain(pages, p => p.Slot.Index is 19 or 20 or 22);    // folded into their firsts
    }

    [Fact]
    public void A_chosen_handful_prints_only_those_songs()
    {
        if (GigFile.Parse() is not { } pcg) return;
        var catalog = PcgCatalog.Build(pcg);
        var list = SetListReader.Read(pcg)[0];

        var pages = GigSheet.BuildPages(pcg, catalog, list, new[] { 16, 18, 20, 22 });
        Assert.Equal(2, pages.Count);                       // the opening, and the chorus once
        Assert.Equal(16, pages[0].Slot.Index);
        Assert.Equal(18, pages[1].Slot.Index);
        Assert.Equal(2, pages[1].AlsoAt.Count);

        var pdf = GigSheetPdf.Render(pages, "20260806a.PCG");
        Assert.Contains("/Type /Pages /Count 2", Content(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_page_is_one_landscape_sheet()
    {
        var (pcg, catalog, list) = Gig();
        var pages = GigSheet.BuildPages(pcg, catalog, list, list.NamedSlots.Take(4).Select(s => s.Index));
        string pdf = Content(GigSheetPdf.Render(pages, "sample.PCG"));

        Assert.StartsWith("%PDF-", pdf, StringComparison.Ordinal);
        Assert.Equal(pages.Count, Occurrences(pdf, "/MediaBox [0 0 792 612]"));
        Assert.EndsWith("%%EOF\n", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_carries_the_sheet_it_was_given()
    {
        if (GigFile.Parse() is not { } pcg) return;
        var catalog = PcgCatalog.Build(pcg);
        var list = SetListReader.Read(pcg)[0];
        var pages = GigSheet.BuildPages(pcg, catalog, list, new[] { 18 });
        string pdf = Content(GigSheetPdf.Render(pages, "20260806a.PCG"));

        // Uncompressed streams, so the text runs are readable right here.
        Assert.Contains("(Footloose Chorus)", pdf, StringComparison.Ordinal);
        Assert.Contains("(FOOTLOOSE CHORUS!)", pdf, StringComparison.Ordinal);
        Assert.Contains("(WHAT PLAYS WHERE)", pdf, StringComparison.Ordinal);
        Assert.Contains("(Rotary Speaker Pro CX Custom)", pdf, StringComparison.Ordinal);
        Assert.Contains("(Drawbars 8 8 8 8 7 8 3 4 8)", pdf, StringComparison.Ordinal);
        Assert.Contains("(Joystick up)", pdf, StringComparison.Ordinal);
        Assert.Contains("(20260806a.PCG)", pdf, StringComparison.Ordinal);
        Assert.Contains("(A0)", pdf, StringComparison.Ordinal);

        // 52 white keys drawn as filled-and-stroked rectangles, plus 36 black ones filled.
        Assert.True(Occurrences(pdf, " re B\n") >= 52, "the white keys should all be drawn");
        Assert.True(Occurrences(pdf, " re f\n") >= 36, "the black keys should all be drawn");
    }

    [Fact]
    public void Every_named_slot_of_every_set_list_renders()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var combis = CombiReader.Read(pcg);
        var programs = ProgramReader.Read(pcg);
        int rendered = 0;

        foreach (var list in SetListReader.Read(pcg).Where(l => l.NamedSlots.Any()))
        {
            var pages = GigSheet.BuildPages(pcg, catalog, list,
                list.NamedSlots.Take(6).Select(s => s.Index), combis, programs);
            var pdf = GigSheetPdf.Render(pages, "sample.PCG");
            Assert.StartsWith("%PDF-", Content(pdf), StringComparison.Ordinal);
            rendered += pages.Count;
        }
        Assert.True(rendered > 20, $"only {rendered} pages rendered");
    }

    [Fact]
    public void A_song_slot_still_gets_a_page_that_says_where_the_song_lives()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        foreach (var list in SetListReader.Read(pcg))
            foreach (var slot in list.NamedSlots.Where(s => s.Reference.Kind == PcgItemKind.Song))
            {
                var pages = GigSheet.BuildPages(pcg, catalog, list, new[] { slot.Index });
                string pdf = Content(GigSheetPdf.Render(pages, "sample.PCG"));
                Assert.Contains(".SNG", pdf, StringComparison.Ordinal);
                Assert.Contains("/MediaBox [0 0 792 612]", pdf, StringComparison.Ordinal);
                return;
            }
    }

    [Fact]
    public void The_same_songs_always_produce_the_same_file()
    {
        var (pcg, catalog, list) = Gig();
        var indices = list.NamedSlots.Take(3).Select(s => s.Index).ToList();
        byte[] Render() => GigSheetPdf.Render(
            GigSheet.BuildPages(pcg, catalog, list, indices), "sample.PCG");

        Assert.Equal(Render(), Render());
    }

    private static int Occurrences(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }
}
