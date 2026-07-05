using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgEditorSlotFieldsTests
{
    [Fact]
    public void Reader_decodes_the_factory_demo_descriptions()
    {
        // Set List 15 is the factory demo list; its slots carry long engine descriptions.
        var slot = SetListReader.Read(Sample.Parse())[15].Slots[0];
        Assert.StartsWith("The SGX-2 engine provides", slot.Description);
        Assert.True(slot.Description.Length > 100);
    }

    [Fact]
    public void SetDescription_round_trips_including_line_breaks()
    {
        var pcg = Sample.Parse();
        var text = "Capo 3\nWatch the bridge\nCount-in: drums";
        var edited = PcgEditor.SetSetListSlotDescription(pcg, 0, 1, text);

        var slot = SetListReader.Read(PcgReader.Parse(edited))[0].Slots[1];
        Assert.Equal(text, slot.Description);
        Assert.Equal("Let's Go Crazy", slot.Name);                    // name untouched
        Assert.Equal(PcgItemKind.Combi, slot.Reference.Kind);         // reference untouched
        Assert.Equal(7, slot.Reference.Bank);
        Assert.Equal(57, slot.Reference.Index);
    }

    [Fact]
    public void SetDescription_truncates_to_the_field_length()
    {
        var pcg = Sample.Parse();
        var text = new string('x', 600);
        var edited = PcgEditor.SetSetListSlotDescription(pcg, 0, 1, text);

        var slot = SetListReader.Read(PcgReader.Parse(edited))[0].Slots[1];
        Assert.Equal(SetListReader.SlotDescriptionLength, slot.Description.Length);
    }

    [Fact]
    public void SetDescription_keeps_every_leaf_checksum_valid()
    {
        var pcg = Sample.Parse();
        var edited = PcgEditor.SetSetListSlotDescription(pcg, 0, 1, "checksum probe");

        foreach (var chunk in pcg.EnumerateChunks())
        {
            if (chunk.HasChildren || chunk.DataOffset < 1 || chunk.Size <= 0) continue;
            if (chunk.DataEnd > pcg.Data.Length) continue;
            byte stored = pcg.Data[chunk.DataOffset - 1];
            if (stored != PcgChecksum.Sum(pcg.Data, chunk.DataOffset, chunk.Size)) continue;
            Assert.Equal(PcgChecksum.Sum(edited, chunk.DataOffset, chunk.Size), edited[chunk.DataOffset - 1]);
        }
    }

    [Fact]
    public void Repoint_to_another_combi_changes_only_the_reference()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0].Slots[1];

        var edited = PcgEditor.RepointSetListSlot(pcg, 0, 1, PcgItemKind.Combi, 7, 58);
        var pcg2 = PcgReader.Parse(edited);
        var slot = SetListReader.Read(pcg2)[0].Slots[1];

        Assert.Equal(PcgItemKind.Combi, slot.Reference.Kind);
        Assert.Equal(7, slot.Reference.Bank);
        Assert.Equal(58, slot.Reference.Index);
        Assert.Equal("Freeze Frame", PcgCatalog.Build(pcg2).Resolve(slot.Reference));
        Assert.Equal(before.Name, slot.Name);
        Assert.Equal(before.Description, slot.Description);
        Assert.Equal(before.Reference.Raw[3], slot.Reference.Raw[3]); // non-reference slot bytes
        Assert.Equal(before.Reference.Raw[4], slot.Reference.Raw[4]); // (color/volume) preserved
    }

    [Fact]
    public void Repoint_to_a_user_bank_program_maps_the_PcgId()
    {
        var pcg = Sample.Parse();
        // USER-C is program bank list index 8 → hardware PcgId 19.
        var edited = PcgEditor.RepointSetListSlot(pcg, 0, 1, PcgItemKind.Program, 8, 0);
        var pcg2 = PcgReader.Parse(edited);
        var slot = SetListReader.Read(pcg2)[0].Slots[1];

        Assert.Equal(PcgItemKind.Program, slot.Reference.Kind);
        Assert.Equal(19, slot.Reference.Bank); // raw PcgId as stored
        Assert.Equal(0, slot.Reference.Index);
        var catalog = PcgCatalog.Build(pcg2);
        Assert.Equal(catalog.ProgramBanks[8][0], catalog.Resolve(slot.Reference));
    }

    [Fact]
    public void Repoint_rejects_song_kind_and_bad_targets()
    {
        var pcg = Sample.Parse();
        Assert.Throws<ArgumentException>(() => PcgEditor.RepointSetListSlot(pcg, 0, 1, PcgItemKind.Song, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.RepointSetListSlot(pcg, 0, 1, PcgItemKind.Combi, 99, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.RepointSetListSlot(pcg, 0, 1, PcgItemKind.Program, 0, 999));
    }
}
