using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgEditorTests
{
    [Fact]
    public void SwapSetListSlots_swaps_slots_and_changes_only_those_blocks()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0];
        Assert.Equal("Let's Go Crazy", before.Slots[1].Name);
        Assert.Equal("Freeze Frame", before.Slots[2].Name);

        var edited = PcgEditor.SwapSetListSlots(pcg, setListIndex: 0, slotA: 1, slotB: 2);

        // Original is untouched and the file length is unchanged.
        Assert.NotSame(pcg.Data, edited);
        Assert.Equal(pcg.Data.Length, edited.Length);

        // Re-decoding the edited bytes shows the swap, with references travelling along.
        var after = SetListReader.Read(PcgReader.Parse(edited))[0];
        Assert.Equal("Freeze Frame", after.Slots[1].Name);
        Assert.Equal("Let's Go Crazy", after.Slots[2].Name);
        Assert.Equal(58, after.Slots[1].Reference.Index);
        Assert.Equal(57, after.Slots[2].Reference.Index);

        // Surgical: at most the two 542-byte slot blocks differ from the original.
        Assert.True(DiffCount(pcg.Data, edited) <= 2 * SetListReader.SlotSize);
    }

    [Fact]
    public void CopySetListSlot_overwrites_destination_with_source()
    {
        var pcg = Sample.Parse();

        // Copy SL0 slot 1 ("Let's Go Crazy", Combi #57) onto an empty slot 50.
        var edited = PcgEditor.CopySetListSlot(pcg, srcSetList: 0, srcSlot: 1, dstSetList: 0, dstSlot: 50);

        var after = SetListReader.Read(PcgReader.Parse(edited))[0];
        Assert.Equal("Let's Go Crazy", after.Slots[50].Name);
        Assert.Equal(PcgItemKind.Combi, after.Slots[50].Reference.Kind);
        Assert.Equal(57, after.Slots[50].Reference.Index);

        // Source is unchanged; only one slot block differs.
        Assert.Equal("Let's Go Crazy", after.Slots[1].Name);
        Assert.True(DiffCount(pcg.Data, edited) <= SetListReader.SlotSize);
    }

    [Fact]
    public void RenameSetListSlot_writes_the_name()
    {
        var pcg = Sample.Parse();
        var edited = PcgEditor.RenameSetListSlot(pcg, setListIndex: 0, slot: 1, name: "My Song");

        var after = SetListReader.Read(PcgReader.Parse(edited))[0];
        Assert.Equal("My Song", after.Slots[1].Name);
        // Reference is untouched by a rename.
        Assert.Equal(57, after.Slots[1].Reference.Index);
    }

    [Fact]
    public void RenameSetList_writes_the_name_and_truncates_to_24()
    {
        var pcg = Sample.Parse();
        var edited = PcgEditor.RenameSetList(pcg, setListIndex: 0, name: "This Name Is Way Too Long To Fit");

        var after = SetListReader.Read(PcgReader.Parse(edited))[0];
        Assert.Equal("This Name Is Way Too Lon", after.Name); // 24 chars
    }

    [Fact]
    public void SwapSetListSlots_rejects_out_of_range_slots()
    {
        var pcg = Sample.Parse();
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SwapSetListSlots(pcg, 0, 1, 999));
    }

    [Fact]
    public void ReorderSetListSlots_moves_whole_blocks_and_nothing_else()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0];
        Assert.Equal("Let's Go Crazy", before.Slots[1].Name);
        string slot2Name = before.Slots[2].Name;
        string slot3Name = before.Slots[3].Name;

        // Slot 1 moves to position 3; 2 and 3 shift up (newOrder[newPos] = oldIndex).
        var newOrder = Enumerable.Range(0, before.Slots.Count).ToArray();
        newOrder[1] = 2;
        newOrder[2] = 3;
        newOrder[3] = 1;
        var edited = PcgEditor.ReorderSetListSlots(pcg, 0, newOrder);

        var after = SetListReader.Read(PcgReader.Parse(edited))[0];
        Assert.Equal(slot2Name, after.Slots[1].Name);
        Assert.Equal(slot3Name, after.Slots[2].Name);
        Assert.Equal("Let's Go Crazy", after.Slots[3].Name);
        // The whole block travelled: the reference (and everything else) came along.
        Assert.Equal(57, after.Slots[3].Reference.Index);
        Assert.Equal(before.Slots[1].Color, after.Slots[3].Color);
        Assert.Equal(before.Slots[1].Description, after.Slots[3].Description);

        // Surgical: a 3-block rotation touches at most 3 slot blocks; the chunk checksum
        // byte is unchanged because a permutation preserves the byte sum.
        Assert.True(DiffCount(pcg.Data, edited) <= 3 * SetListReader.SlotSize);
    }

    [Fact]
    public void ReorderSetListSlots_rejects_non_permutations()
    {
        var pcg = Sample.Parse();
        int count = SetListReader.Read(pcg)[0].Slots.Count;

        var duplicate = Enumerable.Range(0, count).ToArray();
        duplicate[1] = 0; // 0 appears twice
        Assert.Throws<ArgumentException>(() => PcgEditor.ReorderSetListSlots(pcg, 0, duplicate));
        Assert.Throws<ArgumentException>(() => PcgEditor.ReorderSetListSlots(pcg, 0, new[] { 0, 1, 2 }));
    }

    [Fact]
    public void ClearSetListSlot_blanks_name_and_notes_but_keeps_the_reference_bytes()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0].Slots[1];
        Assert.False(before.IsEmpty);

        var edited = PcgEditor.ClearSetListSlot(pcg, setListIndex: 0, slot: 1);

        var after = SetListReader.Read(PcgReader.Parse(edited))[0].Slots[1];
        Assert.True(after.IsEmpty);
        Assert.Equal(string.Empty, after.Description);
        // The reference and settings bytes are untouched — empty is name-only.
        Assert.Equal(before.Reference.Kind, after.Reference.Kind);
        Assert.Equal(57, after.Reference.Index);
        Assert.Equal(before.Color, after.Color);
        Assert.Equal(before.Volume, after.Volume);
        Assert.Equal(before.Transpose, after.Transpose);
        Assert.Equal(before.HoldTimeIndex, after.HoldTimeIndex);

        // Surgical: name + description fields plus the one chunk checksum byte.
        Assert.True(DiffCount(pcg.Data, edited)
            <= SetListReader.SlotNameLength + SetListReader.SlotDescriptionLength + 1);
    }

    private static int DiffCount(byte[] a, byte[] b)
    {
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                diff++;
        return diff;
    }
}
