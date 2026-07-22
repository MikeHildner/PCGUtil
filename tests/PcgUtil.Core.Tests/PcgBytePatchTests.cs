using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgBytePatchTests
{
    [Fact]
    public void Compute_returns_empty_patch_for_identical_images()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var patch = PcgBytePatch.Compute(bytes, (byte[])bytes.Clone());
        Assert.True(patch.IsEmpty);
        Assert.Equal(0, patch.ByteCost);
    }

    [Fact]
    public void Compute_throws_on_length_mismatch()
    {
        Assert.Throws<ArgumentException>(() => PcgBytePatch.Compute(new byte[4], new byte[5]));
    }

    [Fact]
    public void Compute_finds_a_single_run_and_round_trips()
    {
        var before = new byte[200];
        var after = (byte[])before.Clone();
        after[100] = 7;
        after[101] = 9;

        var patch = PcgBytePatch.Compute(before, after);
        var segment = Assert.Single(patch.Segments);
        Assert.Equal(100, segment.Offset);
        Assert.Equal(new byte[] { 0, 0 }, segment.OldBytes);
        Assert.Equal(new byte[] { 7, 9 }, segment.NewBytes);
        Assert.Equal(after, patch.ApplyNew(before));
        Assert.Equal(before, patch.ApplyOld(after));
    }

    [Fact]
    public void Compute_merges_runs_separated_by_less_than_the_gap()
    {
        var before = new byte[500];
        var after = (byte[])before.Clone();
        after[100] = 1;
        after[100 + 1 + 63] = 1; // 63 equal bytes between the runs — below the 64 gap

        var patch = PcgBytePatch.Compute(before, after);
        var segment = Assert.Single(patch.Segments);
        Assert.Equal(65, segment.OldBytes.Length); // both runs plus the gap
        Assert.Equal(before, patch.ApplyOld(after));
        Assert.Equal(after, patch.ApplyNew(before));
    }

    [Fact]
    public void Compute_keeps_runs_separated_by_the_gap_or_more_apart()
    {
        var before = new byte[500];
        var after = (byte[])before.Clone();
        after[100] = 1;
        after[100 + 1 + 64] = 1; // exactly 64 equal bytes — at the threshold, stays split

        var patch = PcgBytePatch.Compute(before, after);
        Assert.Equal(2, patch.Segments.Count);
        Assert.Equal(before, patch.ApplyOld(after));
    }

    [Fact]
    public void Compute_handles_runs_touching_both_ends_of_the_image()
    {
        var before = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var after = new byte[] { 9, 2, 3, 4, 5, 6, 7, 10 };

        var patch = PcgBytePatch.Compute(before, after, mergeGap: 4);
        Assert.Equal(2, patch.Segments.Count);
        Assert.Equal(0, patch.Segments[0].Offset);
        Assert.Equal(7, patch.Segments[1].Offset);
        Assert.Equal(before, patch.ApplyOld(after));
        Assert.Equal(after, patch.ApplyNew(before));
    }

    [Fact]
    public void ByteCost_counts_old_and_new_payload()
    {
        var before = new byte[100];
        var after = (byte[])before.Clone();
        after[10] = 1;
        after[11] = 2;
        after[12] = 3;

        var patch = PcgBytePatch.Compute(before, after);
        Assert.Equal(6, patch.ByteCost); // 3 old + 3 new
    }

    [Fact]
    public void Apply_does_not_mutate_its_input()
    {
        var before = new byte[] { 1, 2, 3 };
        var after = new byte[] { 1, 9, 3 };
        var patch = PcgBytePatch.Compute(before, after);

        var input = (byte[])after.Clone();
        patch.ApplyOld(input);
        Assert.Equal(after, input);
    }

    [Fact]
    public void Random_mutations_round_trip()
    {
        var rng = new Random(42);
        var before = new byte[300_000];
        rng.NextBytes(before);
        var after = (byte[])before.Clone();
        for (int k = 0; k < 500; k++)
            after[rng.Next(after.Length)] ^= (byte)(1 + rng.Next(255));

        var patch = PcgBytePatch.Compute(before, after);
        Assert.Equal(after, patch.ApplyNew(before));
        Assert.Equal(before, patch.ApplyOld(after));
    }

    [Fact]
    public void Real_file_rename_round_trips_and_stays_small()
    {
        var original = Sample.Bytes();
        var edited = PcgEditor.RenameSetListSlot(Sample.Parse(), 0, 1, "Patch Round Trip");

        var patch = PcgBytePatch.Compute(original, edited);
        Assert.False(patch.IsEmpty);
        Assert.True(patch.ByteCost < 8_192, $"rename patch unexpectedly large: {patch.ByteCost}");
        Assert.True(patch.ApplyOld(edited).AsSpan().SequenceEqual(original));
        Assert.True(patch.ApplyNew(original).AsSpan().SequenceEqual(edited));
    }

    [Fact]
    public void Real_file_sort_round_trips()
    {
        var original = Sample.Bytes();
        var sorted = PcgOrganizer.SortProgramBankByName(Sample.Parse(), 6);
        Assert.NotNull(sorted); // USER-A is not already sorted in the sample

        var patch = PcgBytePatch.Compute(original, sorted!);
        Assert.True(patch.ApplyOld(sorted!).AsSpan().SequenceEqual(original));
    }
}
