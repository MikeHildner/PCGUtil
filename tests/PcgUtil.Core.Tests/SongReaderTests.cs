using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// Songs live in the companion .SNG, never in the .PCG — a chunk-tree scan of a full backup
/// carries no sequencer data at all. These read a real Save All .SNG when one is present.
/// </summary>
public class SongReaderTests
{
    private static PcgFile? FindSng()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var filesDir = Path.Combine(dir.FullName, "files");
            if (Directory.Exists(filesDir))
            {
                var path = Directory.EnumerateFiles(filesDir, "*.SNG", SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                return path is null ? null : PcgReader.Parse(File.ReadAllBytes(path));
            }
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void A_sng_parses_with_the_pcg_container_reader()
    {
        if (FindSng() is not { } sng)
            return;

        // Same KORG container, longer file header — the reader finds the tree either way.
        Assert.Equal("KORG", sng.Magic);
        Assert.NotEmpty(sng.TopLevel);
        Assert.True(SongReader.IsSongFile(sng));
        Assert.Null(sng.FindFirst("CMB1")); // and it is emphatically not a .PCG
        Assert.Null(sng.FindFirst("PRG1"));
    }

    [Fact]
    public void A_pcg_is_not_mistaken_for_a_song_file()
    {
        var pcg = Sample.Parse();
        Assert.False(SongReader.IsSongFile(pcg));
        Assert.Empty(SongReader.Read(pcg));
    }

    [Fact]
    public void Songs_decode_with_names_and_sixteen_track_timbres()
    {
        if (FindSng() is not { } sng)
            return;

        var songs = SongReader.Read(sng);
        Assert.NotEmpty(songs);

        foreach (var song in songs)
        {
            Assert.Equal(CombiReader.TimbresPerCombi, song.Timbres.Count);
            Assert.NotEqual(string.Empty, song.DisplayName);
            for (int t = 0; t < song.Timbres.Count; t++)
                Assert.Equal(t, song.Timbres[t].Index);
        }

        // Every timbre decodes to values the format allows — the same ranges a combi uses.
        foreach (var t in songs.SelectMany(s => s.Timbres))
        {
            Assert.InRange(t.ProgramNumber, 0, 255);
            Assert.InRange((int)t.Status, 0, (int)TimbreStatus.Ex2);
            Assert.InRange(t.MidiChannel, 0, 16);
            Assert.InRange(t.Volume, 0, 127);
            Assert.InRange(t.Transpose, -60, 60);
            Assert.InRange(t.BottomKey, 0, 127);
            Assert.InRange(t.TopKey, 0, 127);
        }
    }

    [Fact]
    public void The_empty_companion_timbre_chunk_is_not_walked()
    {
        if (FindSng() is not { } sng)
            return;

        // BMT2 sits beside BMT1 carrying a zero count and a nonsense record size. If the walk
        // took it for a timbre set the songs would outnumber the directory entries, so this
        // equality is the guard against reading phantom songs out of an empty chunk.
        Assert.Equal(SongReader.ReadNames(sng).Count, SongReader.Read(sng).Count);
    }
}
