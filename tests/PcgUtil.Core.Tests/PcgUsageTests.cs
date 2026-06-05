using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgUsageTests
{
    [Fact]
    public void CombiReferenceCounts_counts_set_list_slot_usage()
    {
        var counts = PcgUsage.CombiReferenceCounts(Sample.Parse());

        // "Let's Go Crazy" (combi 7/57) is used by at least Set List 0, slot 1.
        Assert.True(counts.GetValueOrDefault((7, 57)) >= 1);
    }

    [Fact]
    public void BuildUsageReport_lists_referenced_programs_with_sites()
    {
        var report = PcgUsage.BuildUsageReport(Sample.Parse());
        Assert.NotEmpty(report.Programs);
        var top = report.Programs[0];
        Assert.True(top.ReferenceCount > 0);
        Assert.All(top.Sites, s => Assert.False(string.IsNullOrEmpty(s.Description)));
    }

    [Fact]
    public void BuildUsageReport_program_is_used_by_a_combi_timbre()
    {
        // Combi 7/57 "Let's Go Crazy" layers Berlin Grand (I-A #0) across timbres.
        var report = PcgUsage.BuildUsageReport(Sample.Parse());
        var berlin = report.Programs.FirstOrDefault(p => p.BankIndex == 0 && p.Number == 0);
        Assert.NotNull(berlin);
        Assert.Contains(berlin!.Sites,
            s => s.Kind == UsageSiteKind.CombiTimbre && s.Description.Contains("Let's Go Crazy"));
    }

    [Fact]
    public void BuildUsageReport_does_not_mark_a_used_program_unreferenced()
    {
        var report = PcgUsage.BuildUsageReport(Sample.Parse());
        Assert.DoesNotContain(report.UnreferencedPrograms, p => p.BankIndex == 0 && p.Number == 0);
    }

    [Fact]
    public void BuildUsageReport_ignores_init_combis()
    {
        // "Init Combi" placeholders flood usage (all timbres default to I-A #0); exclude them.
        var report = PcgUsage.BuildUsageReport(Sample.Parse());
        Assert.DoesNotContain(
            report.Programs.SelectMany(p => p.Sites),
            s => s.Description.Contains("Init Combi"));
    }
}
