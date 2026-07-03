using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class SetListReaderTests
{
    [Fact]
    public void Reads_128_set_lists()
    {
        var setLists = SetListReader.Read(Sample.Parse());
        Assert.Equal(128, setLists.Count);
    }

    [Fact]
    public void Each_set_list_has_128_slots_indexed_0_to_127()
    {
        var sl = SetListReader.Read(Sample.Parse())[0];
        Assert.Equal(128, sl.Slots.Count);
        Assert.Equal(0, sl.Slots[0].Index);
        Assert.Equal(127, sl.Slots[127].Index);
    }

    // Slot numbering matches the hardware: songs start at slot 1; slot 0 is the
    // unnamed Berlin Grand. (Confirmed against a hardware screenshot.)
    [Fact]
    public void First_set_list_name_and_slot_names_decode()
    {
        var sl = SetListReader.Read(Sample.Parse())[0];
        Assert.Equal("PART TIME GENIUS REPRISE", sl.Name);
        Assert.True(sl.Slots[0].IsEmpty);
        Assert.Equal("Let's Go Crazy", sl.Slots[1].Name);
        Assert.Equal("Freeze Frame", sl.Slots[2].Name);
        Assert.Equal("TOM SAWYER", sl.Slots[13].Name);
        Assert.Equal(15, sl.NamedSlots.Count()); // 20260702 sample: two songs added vs June
    }

    [Fact]
    public void Second_set_list_decodes()
    {
        var sl = SetListReader.Read(Sample.Parse())[1];
        Assert.Equal("Part-Time Genius BAK", sl.Name);
        Assert.Equal("Easy", sl.Slots[1].Name);
    }

    [Fact]
    public void Empty_slots_have_no_name()
    {
        var sl = SetListReader.Read(Sample.Parse())[0];
        Assert.True(sl.Slots[100].IsEmpty);
    }

    [Fact]
    public void Slot_references_match_the_hardware()
    {
        var sl = SetListReader.Read(Sample.Parse())[0];

        // Slot 0: Program INT-A #000 (Berlin Grand), no song name.
        var r0 = sl.Slots[0].Reference;
        Assert.Equal(PcgItemKind.Program, r0.Kind);
        Assert.Equal(0, r0.Bank);
        Assert.Equal(0, r0.Index);

        // Slot 1 "Let's Go Crazy": Combi USER-A (bank 7) #057.
        var r1 = sl.Slots[1].Reference;
        Assert.Equal(PcgItemKind.Combi, r1.Kind);
        Assert.Equal(7, r1.Bank);
        Assert.Equal(57, r1.Index);
        Assert.Equal(6, r1.Raw.Count);
        Assert.Equal(0x06, r1.Raw[3]);
        Assert.Equal(0x7F, r1.Raw[4]);

        // Slot 13 "TOM SAWYER": Combi USER-F (bank 12) #008.
        var r13 = sl.Slots[13].Reference;
        Assert.Equal(PcgItemKind.Combi, r13.Kind);
        Assert.Equal(12, r13.Bank);
        Assert.Equal(8, r13.Index);
    }

    [Fact]
    public void Program_reference_decodes_as_program()
    {
        // SL1 slot 59 "Power of Synth" loads a Program (bank 3, #55).
        var slot = SetListReader.Read(Sample.Parse())[1].Slots[59];
        Assert.Equal("Power of Synth", slot.Name);
        Assert.Equal(PcgItemKind.Program, slot.Reference.Kind);
        Assert.Equal(3, slot.Reference.Bank);
        Assert.Equal(55, slot.Reference.Index);
    }

    [Fact]
    public void Song_type_slot_decodes_as_song()
    {
        // Set List 15, slot 31 "Sequence" is a Song-type slot (type bits = 2), the one
        // such slot in the sample. It must not be mistaken for a Combi.
        var slot = SetListReader.Read(Sample.Parse())[15].Slots[31];
        Assert.Equal("Sequence", slot.Name);
        Assert.Equal(PcgItemKind.Song, slot.Reference.Kind);
    }
}
