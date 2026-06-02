using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgReaderTests
{
    private static byte[] LoadSample() => File.ReadAllBytes(FindSampleFile());

    // Walk up from the test output directory to locate the checked-in sample PCG.
    private static string FindSampleFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "files", "20260602.PCG");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Sample PCG (files/20260602.PCG) not found walking up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Parse_recognizes_file_signature()
    {
        var pcg = PcgReader.Parse(LoadSample());
        Assert.Equal("KORG", pcg.Magic);
    }

    [Fact]
    public void Parse_yields_single_pcg1_container_spanning_the_file()
    {
        var pcg = PcgReader.Parse(LoadSample());
        var root = Assert.Single(pcg.TopLevel);
        Assert.Equal("PCG1", root.Id);
        Assert.Equal(pcg.Length, root.DataEnd);
    }

    [Theory]
    [InlineData("SLS1")]
    [InlineData("PRG1")]
    [InlineData("CMB1")]
    [InlineData("DKT1")]
    [InlineData("WSQ1")]
    [InlineData("GLB1")]
    public void Pcg1_contains_expected_sections(string id)
    {
        var pcg = PcgReader.Parse(LoadSample());
        Assert.Contains(pcg.EnumerateChunks(), c => c.Id == id);
    }

    [Fact]
    public void Children_stay_within_their_parent_bounds()
    {
        var pcg = PcgReader.Parse(LoadSample());
        foreach (var parent in pcg.EnumerateChunks())
            foreach (var child in parent.Children)
            {
                Assert.True(child.Offset >= parent.DataOffset, $"{child.Id} starts before {parent.Id} data");
                Assert.True(child.DataEnd <= parent.DataEnd, $"{child.Id} ends after {parent.Id} data");
            }
    }

    [Fact]
    public void Extract_finds_known_set_list_song_name()
    {
        var pcg = PcgReader.Parse(LoadSample());
        var sls = pcg.FindFirst("SLS1");
        Assert.NotNull(sls);

        var names = PcgStrings.Extract(pcg.Data, sls!, minLength: 3)
            .Select(s => s.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Let's Go Crazy", names);
    }
}
