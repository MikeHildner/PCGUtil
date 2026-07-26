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

    // The backup a .SNG was saved beside, when Save All wrote them as a pair — its programs
    // are the ones the songs actually reference. Falls back to the general sample.
    private static PcgFile FindPcgFor(string sngStem)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var filesDir = Path.Combine(dir.FullName, "files");
            if (Directory.Exists(filesDir))
            {
                var match = Directory.EnumerateFiles(filesDir, "*.PCG", SearchOption.AllDirectories)
                    .FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), sngStem,
                                                       StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return PcgReader.Parse(File.ReadAllBytes(match));
                break;
            }
            dir = dir.Parent;
        }
        return Sample.Parse();
    }

    private static PcgFile PairedPcg() => FindPcgFor("save-all");

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

    // ----- The general driver: follow programs by sound between two states of the backup -----

    [Fact]
    public void Following_by_sound_agrees_with_the_operation_that_moved_them()
    {
        if (FindSng() is not { } sng)
            return;
        var pcg = Sample.Parse();
        var (_, _, bank, number) = FirstReference(SongReader.Read(sng));
        int other = number == 0 ? 1 : 0;

        // Same move, described two ways: by the operation, and by comparing before/after.
        var byOperation = SongEditor.RetargetProgramSwap(sng, bank, number, bank, other);
        var moved = PcgReader.Parse(PcgEditor.SwapPrograms(pcg, bank, number, bank, other));
        var bySound = SongEditor.RetargetToPcg(sng, pcg, moved);

        Assert.True(bySound.Changed);
        Assert.Equal(byOperation, bySound.Data);
    }

    [Fact]
    public void Reversing_the_two_states_undoes_the_retarget()
    {
        if (FindSng() is not { } sng)
            return;
        var pcg = Sample.Parse();
        var (_, _, bank, number) = FirstReference(SongReader.Read(sng));

        var moved = PcgReader.Parse(PcgEditor.SwapPrograms(pcg, bank, number, bank, number == 0 ? 1 : 0));
        var forward = SongEditor.RetargetToPcg(sng, pcg, moved);
        Assert.True(forward.Changed);

        // This is exactly what an undo does — no separate history required.
        var back = SongEditor.RetargetToPcg(PcgReader.Parse(forward.Data), moved, pcg);
        Assert.Equal(sng.Data, back.Data);
    }

    [Fact]
    public void A_whole_bank_sort_carries_the_songs_with_it()
    {
        if (FindSng() is not { } sng)
            return;
        var pcg = PairedPcg();

        // Sort whichever bank the songs actually use, so the assertion has real work to do.
        var (_, _, bank, _) = FirstReference(SongReader.Read(sng));
        if (PcgOrganizer.SortProgramBankByName(pcg, bank) is not { } sortedBytes)
            return; // already alphabetical
        var sorted = PcgReader.Parse(sortedBytes);

        var result = SongEditor.RetargetToPcg(sng, pcg, sorted);

        // Not vacuous: a sort that reordered the bank the songs use must move something.
        // (This is the case that caught a real bug — see the duplicate-sound test below.)
        Assert.True(result.Moved > 0,
            $"sorting {PcgBankLabels.Program(bank)} reordered it, so song tracks should have followed");

        // And every track still resolves to the program name it resolved to before the sort.
        var beforeCatalog = PcgCatalog.Build(pcg);
        var afterCatalog = PcgCatalog.Build(sorted);
        var before = SongReader.Read(sng);
        var after = SongReader.Read(PcgReader.Parse(result.Data));

        for (int s = 0; s < before.Count; s++)
            for (int t = 0; t < before[s].Timbres.Count; t++)
            {
                var b = before[s].Timbres[t];
                var a = after[s].Timbres[t];
                Assert.Equal(beforeCatalog.ResolveProgram(b.ProgramBankPcgId, b.ProgramNumber),
                             afterCatalog.ResolveProgram(a.ProgramBankPcgId, a.ProgramNumber));
            }
    }

    [Fact]
    public void A_sound_that_also_exists_elsewhere_still_follows_the_copy_that_moved()
    {
        if (FindSng() is not { } sng)
            return;
        var pcg = PairedPcg();
        var (songIndex, timbreIndex, bank, number) = FirstReference(SongReader.Read(sng));

        // Plant a second copy of the referenced program in a slot that will NOT move, then
        // move the original. Asking "has this sound moved?" globally would see the stationary
        // twin and strand the track on its old slot; the question has to be asked per slot.
        int twin = Enumerable.Range(0, 128).First(i => i != number && i != (number == 0 ? 1 : 0));
        var withTwin = PcgReader.Parse(PcgEditor.CopyProgram(pcg, bank, number, bank, twin));

        int destination = number == 0 ? 1 : 0;
        var moved = PcgReader.Parse(
            PcgEditor.SwapPrograms(withTwin, bank, number, bank, destination));

        var result = SongEditor.RetargetToPcg(sng, withTwin, moved);

        Assert.True(result.Changed);
        var after = SongReader.Read(PcgReader.Parse(result.Data))[songIndex].Timbres[timbreIndex];
        Assert.Equal(destination, after.ProgramNumber);
    }

    [Fact]
    public void An_edit_that_moves_no_program_leaves_the_songs_alone()
    {
        if (FindSng() is not { } sng)
            return;
        var pcg = Sample.Parse();

        // Renaming changes program bytes but not sounds, so nothing should follow.
        var renamed = PcgReader.Parse(PcgEditor.RenameProgram(pcg, 0, 0, "SONG RETARGET PROBE"));
        var result = SongEditor.RetargetToPcg(sng, pcg, renamed);

        Assert.False(result.Changed);
        Assert.Equal(sng.Data, result.Data);
    }

    [Fact]
    public void Overwriting_a_program_leaves_its_references_pointing_at_the_slot()
    {
        if (FindSng() is not { } sng)
            return;
        var pcg = Sample.Parse();
        var (songIndex, timbreIndex, bank, number) = FirstReference(SongReader.Read(sng));

        // Overwrite the very program a track uses with a different one: the sound that was
        // there is gone, so the track keeps its slot and now plays whatever landed on it —
        // the same thing the instrument would do, and what the musician asked for.
        int source = number == 0 ? 1 : 0;
        var pasted = PcgReader.Parse(PcgEditor.CopyProgram(pcg, bank, source, bank, number));
        var result = SongEditor.RetargetToPcg(sng, pcg, pasted);

        var after = SongReader.Read(PcgReader.Parse(result.Data))[songIndex].Timbres[timbreIndex];
        Assert.Equal(number, after.ProgramNumber);
        Assert.Equal(PcgCatalog.ProgramBankPcgIdForIndex(bank), after.ProgramBankPcgId);
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
