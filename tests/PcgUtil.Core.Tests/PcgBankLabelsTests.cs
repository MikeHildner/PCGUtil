using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgBankLabelsTests
{
    [Theory]
    [InlineData(0, "INT-A")]
    [InlineData(5, "INT-F")]
    [InlineData(6, "USER-A")]
    [InlineData(12, "USER-G")]
    [InlineData(13, "USER-AA")]
    [InlineData(19, "USER-GG")]
    public void Program_labels(int listIndex, string expected) =>
        Assert.Equal(expected, PcgBankLabels.Program(listIndex));

    [Theory]
    [InlineData(0, "INT-A")]
    [InlineData(6, "INT-G")]
    [InlineData(7, "USER-A")]
    [InlineData(13, "USER-G")]
    public void Combi_labels(int listIndex, string expected) =>
        Assert.Equal(expected, PcgBankLabels.Combi(listIndex));

    [Fact]
    public void Out_of_range_is_handled()
    {
        Assert.Equal("?", PcgBankLabels.Program(-1));
        Assert.Equal("bank 42", PcgBankLabels.Combi(42));
    }
}
