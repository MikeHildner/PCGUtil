using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgEditHistoryTests
{
    private static byte[] Mutate(byte[] source, int offset, byte value)
    {
        var result = (byte[])source.Clone();
        result[offset] = value;
        return result;
    }

    [Fact]
    public void Push_returns_false_and_records_nothing_for_identical_images()
    {
        var history = new PcgEditHistory();
        var image = new byte[] { 1, 2, 3 };

        Assert.False(history.Push(image, (byte[])image.Clone(), "no-op"));
        Assert.False(history.CanUndo);
        Assert.Equal(0, history.ByteCost);
    }

    [Fact]
    public void Undo_returns_the_previous_image_and_redo_restores_the_edit()
    {
        var history = new PcgEditHistory();
        var v0 = new byte[] { 1, 2, 3, 4 };
        var v1 = Mutate(v0, 2, 9);

        Assert.True(history.Push(v0, v1, "edit"));
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        var undone = history.Undo(v1);
        Assert.Equal(v0, undone);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        var redone = history.Redo(undone);
        Assert.Equal(v1, redone);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Rename_then_sort_then_two_undos_equals_the_original_bytes_exactly()
    {
        var original = Sample.Bytes();
        var history = new PcgEditHistory();

        var renamed = PcgEditor.RenameSetListSlot(PcgReader.Parse(original), 0, 1, "Undo Me");
        Assert.True(history.Push(original, renamed, "Rename slot"));

        var sorted = PcgOrganizer.SortProgramBankByName(PcgReader.Parse(renamed), 6);
        Assert.NotNull(sorted);
        Assert.True(history.Push(renamed, sorted!, "Sort USER-A A–Z"));

        var afterFirstUndo = history.Undo(sorted!);
        Assert.True(afterFirstUndo.AsSpan().SequenceEqual(renamed));

        var afterSecondUndo = history.Undo(afterFirstUndo);
        Assert.True(afterSecondUndo.AsSpan().SequenceEqual(original));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void New_push_clears_the_redo_stack()
    {
        var history = new PcgEditHistory();
        var v0 = new byte[] { 0, 0 };
        var v1 = Mutate(v0, 0, 1);

        history.Push(v0, v1, "first");
        var undone = history.Undo(v1);
        Assert.True(history.CanRedo);

        var v2 = Mutate(undone, 1, 5);
        history.Push(undone, v2, "second");
        Assert.False(history.CanRedo);
        Assert.Equal("second", history.UndoLabel);
    }

    [Fact]
    public void Labels_follow_the_edits_across_undo_and_redo()
    {
        var history = new PcgEditHistory();
        var v0 = new byte[] { 0 };
        var v1 = Mutate(v0, 0, 1);
        var v2 = Mutate(v1, 0, 2);

        history.Push(v0, v1, "one");
        history.Push(v1, v2, "two");
        Assert.Equal("two", history.UndoLabel);
        Assert.Null(history.RedoLabel);

        history.Undo(v2);
        Assert.Equal("one", history.UndoLabel);
        Assert.Equal("two", history.RedoLabel);
    }

    [Fact]
    public void Undo_throws_when_nothing_to_undo()
    {
        var history = new PcgEditHistory();
        Assert.Throws<InvalidOperationException>(() => history.Undo(new byte[1]));
        Assert.Throws<InvalidOperationException>(() => history.Redo(new byte[1]));
    }

    [Fact]
    public void Eviction_drops_oldest_entries_and_sets_Trimmed()
    {
        // Each single-byte edit costs 2 bytes; budget of 5 holds at most two entries.
        var history = new PcgEditHistory(maxByteCost: 5);
        var v0 = new byte[] { 0, 0, 0 };
        var v1 = Mutate(v0, 0, 1);
        var v2 = Mutate(v1, 1, 1);
        var v3 = Mutate(v2, 2, 1);

        history.Push(v0, v1, "a");
        history.Push(v1, v2, "b");
        Assert.False(history.Trimmed);

        history.Push(v2, v3, "c");
        Assert.True(history.Trimmed);
        Assert.Equal(2, history.UndoCount);
        Assert.Equal("c", history.UndoLabel);
    }

    [Fact]
    public void Undo_after_eviction_stops_at_the_trimmed_boundary()
    {
        var history = new PcgEditHistory(maxByteCost: 5);
        var v0 = new byte[] { 0, 0, 0 };
        var v1 = Mutate(v0, 0, 1);
        var v2 = Mutate(v1, 1, 1);
        var v3 = Mutate(v2, 2, 1);

        history.Push(v0, v1, "a");
        history.Push(v1, v2, "b");
        history.Push(v2, v3, "c");

        var afterC = history.Undo(v3);
        Assert.Equal(v2, afterC);
        var afterB = history.Undo(afterC);
        Assert.Equal(v1, afterB);
        Assert.False(history.CanUndo); // "a" was evicted — v0 is unreachable
        Assert.True(history.Trimmed);
    }

    [Fact]
    public void The_newest_entry_survives_even_when_over_budget()
    {
        var history = new PcgEditHistory(maxByteCost: 1);
        var v0 = new byte[] { 0, 0, 0, 0 };
        var v1 = new byte[] { 1, 1, 1, 1 }; // 8-byte patch, over the 1-byte budget

        Assert.True(history.Push(v0, v1, "big"));
        Assert.True(history.CanUndo);
        Assert.Equal(v0, history.Undo(v1));
    }

    [Fact]
    public void Clear_empties_both_stacks_and_resets_Trimmed()
    {
        var history = new PcgEditHistory(maxByteCost: 5);
        var v0 = new byte[] { 0, 0, 0 };
        var v1 = Mutate(v0, 0, 1);
        var v2 = Mutate(v1, 1, 1);
        var v3 = Mutate(v2, 2, 1);

        history.Push(v0, v1, "a");
        history.Push(v1, v2, "b");
        history.Push(v2, v3, "c");
        history.Undo(v3);
        Assert.True(history.Trimmed);

        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.Trimmed);
        Assert.Equal(0, history.ByteCost);
        Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void UndoCount_and_ByteCost_track_the_stacks()
    {
        var history = new PcgEditHistory();
        var v0 = new byte[] { 0, 0 };
        var v1 = Mutate(v0, 0, 1);
        var v2 = Mutate(v1, 1, 1);

        history.Push(v0, v1, "a");
        history.Push(v1, v2, "b");
        Assert.Equal(2, history.UndoCount);
        Assert.Equal(4, history.ByteCost); // two single-byte patches, each 1 old + 1 new

        history.Undo(v2);
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(4, history.ByteCost); // moving to redo keeps the payload retained
    }
}
