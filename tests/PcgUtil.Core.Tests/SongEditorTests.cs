using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The point of the whole exercise: when a program moves inside the .PCG, a song track that
/// played it must follow, or the song plays whatever landed in the old slot.
/// </summary>
public class SongEditorTests
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

    // Any track that actually points somewhere, so the assertions have a real reference.
    private static (int Song, int Timbre, int Bank, int Number) FirstReference(IReadOnlyList<Song> songs)
    {
        foreach (var song in songs)
            foreach (var t in song.Timbres)
            {
                int bank = PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId);
                if (bank >= 0)
                    return (song.Index, t.Index, bank, t.ProgramNumber);
            }
        throw new InvalidOperationException("No resolvable program reference in the sample .SNG.");
    }

    [Fact]
    public void A_swap_moves_the_song_track_with_the_program()
    {
        if (FindSng() is not { } sng)
            return;

        var (songIndex, timbreIndex, bank, number) = FirstReference(SongReader.Read(sng));
        int other = number == 0 ? 1 : 0;

        var edited = SongEditor.RetargetProgramSwap(sng, bank, number, bank, other);

        var after = SongReader.Read(PcgReader.Parse(edited))[songIndex].Timbres[timbreIndex];
        Assert.Equal(other, after.ProgramNumber);
        Assert.Equal(PcgCatalog.ProgramBankPcgIdForIndex(bank), after.ProgramBankPcgId);

        // Swapping back is the identity, byte for byte.
        var restored = SongEditor.RetargetProgramSwap(PcgReader.Parse(edited), bank, number, bank, other);
        Assert.Equal(sng.Data, restored);
    }

    [Fact]
    public void A_reorder_renumbers_every_track_that_pointed_into_the_moved_bank()
    {
        if (FindSng() is not { } sng)
            return;

        var before = SongReader.Read(sng);
        var (_, _, bank, _) = FirstReference(before);

        // Reverse the bank: program i ends up at count-1-i, so every reference must invert.
        const int count = 128;
        var newOrder = Enumerable.Range(0, count).Select(i => count - 1 - i).ToList();

        var edited = SongEditor.RetargetProgramReorder(sng, bank, newOrder);
        var after = SongReader.Read(PcgReader.Parse(edited));

        for (int s = 0; s < before.Count; s++)
            for (int t = 0; t < before[s].Timbres.Count; t++)
            {
                var b = before[s].Timbres[t];
                var a = after[s].Timbres[t];
                bool inMovedBank = PcgCatalog.ProgramBankIndexForPcgId(b.ProgramBankPcgId) == bank
                                   && b.ProgramNumber < count;
                Assert.Equal(inMovedBank ? count - 1 - b.ProgramNumber : b.ProgramNumber, a.ProgramNumber);
                Assert.Equal(b.ProgramBankPcgId, a.ProgramBankPcgId); // reorder stays within a bank
                Assert.Equal(b.Status, a.Status);                     // and touches nothing else
                Assert.Equal(b.MidiChannel, a.MidiChannel);
                Assert.Equal(b.Volume, a.Volume);
            }
    }

    [Fact]
    public void Retargeting_a_bank_no_song_uses_changes_nothing()
    {
        if (FindSng() is not { } sng)
            return;

        var used = SongReader.Read(sng)
            .SelectMany(s => s.Timbres)
            .Select(t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId))
            .ToHashSet();
        int unused = Enumerable.Range(0, 20).First(b => !used.Contains(b));

        Assert.Equal(0, SongEditor.CountReferences(sng, unused));
        var edited = SongEditor.RetargetProgramSwap(sng, unused, 0, unused, 1);
        Assert.Equal(sng.Data, edited);
    }

    [Fact]
    public void Reference_counting_matches_what_the_reader_sees()
    {
        if (FindSng() is not { } sng)
            return;

        var songs = SongReader.Read(sng);
        var (_, _, bank, number) = FirstReference(songs);

        int expectedBank = songs.SelectMany(s => s.Timbres)
            .Count(t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId) == bank);
        int expectedOne = songs.SelectMany(s => s.Timbres)
            .Count(t => PcgCatalog.ProgramBankIndexForPcgId(t.ProgramBankPcgId) == bank
                        && t.ProgramNumber == number);

        Assert.Equal(expectedBank, SongEditor.CountReferences(sng, bank));
        Assert.Equal(expectedOne, SongEditor.CountReferences(sng, bank, number));
        Assert.True(expectedOne > 0);
    }

    [Fact]
    public void The_edited_file_still_passes_its_own_checksums()
    {
        if (FindSng() is not { } sng)
            return;

        var (_, _, bank, number) = FirstReference(SongReader.Read(sng));
        var edited = SongEditor.RetargetProgramSwap(sng, bank, number, bank, number == 0 ? 1 : 0);

        foreach (var chunk in sng.EnumerateChunks())
        {
            if (chunk.HasChildren || chunk.DataOffset < 1 || chunk.Size <= 0) continue;
            if (chunk.DataEnd > sng.Data.Length) continue;
            byte stored = (byte)(chunk.Field & 0xFF);
            if (stored != PcgChecksum.Sum(sng.Data, chunk.DataOffset, chunk.Size)) continue;
            Assert.Equal(PcgChecksum.Sum(edited, chunk.DataOffset, chunk.Size), edited[chunk.DataOffset - 1]);
        }

        Assert.Equal(sng.Data.Length, edited.Length); // surgical: same size, same layout
    }
}
