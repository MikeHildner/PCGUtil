using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgDuplicatesTests
{
    [Fact]
    public void Finds_duplicate_combis_by_name()
    {
        var groups = PcgDuplicates.Combis(Sample.Parse());

        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.True(g.Count >= 2));
        Assert.All(groups, g => Assert.False(string.IsNullOrEmpty(g.Name)));
        // The factory "Init Combi" placeholders are the same name many times over.
        Assert.Contains(groups, g => g.Name.Contains("Init"));
    }

    [Fact]
    public void Groups_are_ordered_by_count_descending()
    {
        var groups = PcgDuplicates.Combis(Sample.Parse());
        for (int i = 1; i < groups.Count; i++)
            Assert.True(groups[i - 1].Count >= groups[i].Count);
    }

    [Fact]
    public void Programs_runs_and_groups_are_well_formed()
    {
        // Program names may all be unique (no groups) — just assert the contract holds.
        var groups = PcgDuplicates.Programs(Sample.Parse());
        Assert.All(groups, g =>
        {
            Assert.True(g.Count >= 2);
            Assert.False(string.IsNullOrEmpty(g.Name));
        });
    }
}
