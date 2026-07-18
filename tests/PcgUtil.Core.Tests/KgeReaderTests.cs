using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class KgeReaderTests
{
    private static byte[]? FindKge()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var filesDir = Path.Combine(dir.FullName, "files");
            if (Directory.Exists(filesDir))
            {
                var path = Directory.EnumerateFiles(filesDir, "*.KGE", SearchOption.AllDirectories).FirstOrDefault();
                return path is null ? null : File.ReadAllBytes(path);
            }
            dir = dir.Parent;
        }
        return null;
    }

    // The vendor pack's .KGE carries its MIDI-converted GEs in USER-A; its combi
    // "MONEY4FREE OPEN" selects flat GE id 2048+96, which the pack's own content
    // listing names — the byte-level PCG↔KGE link, now with the name attached.
    [Fact]
    public void Reads_vendor_pack_ge_names()
    {
        if (FindKge() is not { } bytes)
            return;

        var banks = KgeReader.Read(bytes);
        Assert.NotNull(banks);
        Assert.Equal(KgeReader.UserBankCount, banks!.Count);
        Assert.Equal(128, banks[0].Count); // USER-A
        Assert.Equal("DayDream Piano", banks[0][0]);
        Assert.NotEqual(string.Empty, banks[0][96]);

        Assert.Equal(banks[0][96], KgeReader.UserGeName(banks, Combi.KarmaUserGeBase + 96));
        Assert.Null(KgeReader.UserGeName(banks, 100));                          // preset id
        Assert.Null(KgeReader.UserGeName(banks, Combi.KarmaUserGeBase + 3 * 128)); // bank not carried
    }

    [Fact]
    public void Non_kge_bytes_are_rejected()
    {
        Assert.Null(KgeReader.Read(new byte[10]));
        Assert.Null(KgeReader.Read(Sample.Bytes())); // a .PCG is KORG-tagged but has no KGE1
    }
}
