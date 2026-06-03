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

    [Fact]
    public void First_set_list_name_and_slot_names_decode()
    {
        var sl = SetListReader.Read(Sample.Parse())[0];
        Assert.Equal("PART TIME GENIUS REPRISE", sl.Name);
        Assert.Equal("Let's Go Crazy", sl.Slots[0].Name);
        Assert.Equal("Freeze Frame", sl.Slots[1].Name);
        Assert.Equal("TOM SAWYER", sl.Slots[12].Name);
        Assert.Equal(13, sl.NamedSlots.Count());
    }

    [Fact]
    public void Second_set_list_decodes()
    {
        var sl = SetListReader.Read(Sample.Parse())[1];
        Assert.Equal("Part-Time Genius BAK", sl.Name);
        Assert.Equal("Easy", sl.Slots[0].Name);
    }

    [Fact]
    public void Empty_slots_have_no_name()
    {
        var sl = SetListReader.Read(Sample.Parse())[0];
        Assert.True(sl.Slots[100].IsEmpty);
    }

    [Fact]
    public void Slot_reference_bytes_are_captured()
    {
        var sl = SetListReader.Read(Sample.Parse())[0];
        // Reference field is 6 bytes; observed records end with the constant 06 7F pair.
        Assert.Equal(6, sl.Slots[1].Reference.Count);
        Assert.Equal(0x06, sl.Slots[1].Reference[3]);
        Assert.Equal(0x7F, sl.Slots[1].Reference[4]);
    }
}
