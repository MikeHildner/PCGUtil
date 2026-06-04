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
}
