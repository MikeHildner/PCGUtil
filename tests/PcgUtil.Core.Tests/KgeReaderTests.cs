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

    // A .KGE carries the user GE names that combi KARMA modules select by flat id. The
    // assertions stay sample-robust on purpose: whichever .KGE the files/ directory happens
    // to hold first is fine, so adding a backup can't turn this red (it did once, when a
    // save-all .KGE sorted ahead of the vendor pack it had been pinned to). What matters is
    // the structure and the id→name round trip, not one pack's first patch name.
    [Fact]
    public void Reads_user_ge_names()
    {
        if (FindKge() is not { } bytes)
            return;

        var banks = KgeReader.Read(bytes);
        Assert.NotNull(banks);
        Assert.Equal(KgeReader.UserBankCount, banks!.Count);
        Assert.Equal(128, banks[0].Count); // USER-A

        // Real names, not garbage: printable and plausibly sized.
        string first = banks[0][0];
        Assert.NotEqual(string.Empty, first);
        Assert.InRange(first.Length, 1, 24);
        Assert.All(first, c => Assert.InRange(c, ' ', '~'));

        // The link the app actually relies on: a flat user GE id resolves to that slot.
        Assert.Equal(banks[0][96], KgeReader.UserGeName(banks, Combi.KarmaUserGeBase + 96));
        Assert.Null(KgeReader.UserGeName(banks, 100));  // preset id, below the user range
        // Past the last bank this file carries — how many that is varies by pack.
        Assert.Null(KgeReader.UserGeName(banks, Combi.KarmaUserGeBase + banks.Count * 128));
    }

    [Fact]
    public void Non_kge_bytes_are_rejected()
    {
        Assert.Null(KgeReader.Read(new byte[10]));
        Assert.Null(KgeReader.Read(Sample.Bytes())); // a .PCG is KORG-tagged but has no KGE1
    }
}
