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
        // Notes print at the size chosen on the instrument.
        Assert.Contains("n-xl{font-size:1.6em", html);
        Assert.Contains("class=\"notes n-", html);
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

    /// <summary>
    /// The scanner guard. The host rejects the uploaded assembly when the inline-background
    /// style attribute appears in it as one contiguous run — a deterministic post-transfer
    /// 550, diagnosed 2026-07-18 — which is the whole reason <c>SwatchStyleAttr</c> assembles
    /// that literal at runtime. Nothing can be allowed to quietly put it back.
    /// </summary>
    [Fact]
    public void The_compiled_assembly_carries_no_inline_background_literal()
    {
        var assembly = File.ReadAllBytes(typeof(PcgHtmlReport).Assembly.Location);
        var forbidden = System.Text.Encoding.ASCII.GetBytes("style=\"background:");

        for (int i = 0; i + forbidden.Length <= assembly.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < forbidden.Length && hit; j++)
                if (assembly[i + j] != forbidden[j]) hit = false;
            Assert.False(hit, $"the inline-background literal is back, at byte {i}");
        }

        // And the emitted HTML still carries it, split or not — the swatch must stay coloured.
        var pcg = Sample.Parse();
        string html = PcgHtmlReport.SetList(SetListReader.Read(pcg)[0], PcgCatalog.Build(pcg));
        Assert.Contains("style=\"background:", html, StringComparison.Ordinal);
    }
}
