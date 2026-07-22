using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgHtmlReportTests
{
    [Fact]
    public void SetList_html_lists_songs_with_bank_labels()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var sl = SetListReader.Read(pcg)[0];

        var html = PcgHtmlReport.SetList(sl, catalog);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("TOM SAWYER", html);          // a song name (no special chars)
        Assert.Contains("USER-", html);               // a bank label in the Loads column
        Assert.Contains("Let&#39;s Go Crazy", html);  // apostrophe is HTML-escaped
    }

    [Fact]
    public void SetList_html_carries_transpose_and_hold_columns()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var sl = SetListReader.Read(pcg)[0];

        var html = PcgHtmlReport.SetList(sl, catalog);

        Assert.Contains("<th class=\"num\">Xpose</th>", html);
        Assert.Contains("<th class=\"num\">Hold</th>", html);
        // The gig list plays most songs at −1: the sheet must show it.
        Assert.Contains(">-1</td>", html);
        // Hold time renders as the hardware's label, not a raw index.
        Assert.Contains("sec", html);
    }

    [Fact]
    public void AllSetLists_html_page_breaks_between_lists()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);

        var html = PcgHtmlReport.AllSetLists(SetListReader.Read(pcg), catalog);

        Assert.Contains("page-break", html);
    }

    [Fact]
    public void Usage_html_includes_a_referenced_program()
    {
        var report = PcgUsage.BuildUsageReport(Sample.Parse());

        var html = PcgHtmlReport.Usage(report);

        Assert.Contains("Program usage", html);
        Assert.Contains("Berlin Grand", html);
    }
}
