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
    public void SetColor_round_trips_and_preserves_everything_else()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0].Slots[1];

        var edited = PcgEditor.SetSetListSlotColor(pcg, 0, 1, 11);
        var slot = SetListReader.Read(PcgReader.Parse(edited))[0].Slots[1];

        Assert.Equal(11, slot.Color);
        Assert.Equal(before.Reference.Kind, slot.Reference.Kind);
        Assert.Equal(before.Reference.Bank, slot.Reference.Bank);
        Assert.Equal(before.Reference.Index, slot.Reference.Index);
        Assert.Equal(before.Name, slot.Name);
        Assert.Equal(before.Description, slot.Description);
        Assert.Equal(before.Volume, slot.Volume);
        Assert.Equal(before.Transpose, slot.Transpose);

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
    public void SetColor_rejects_out_of_range_colors()
    {
        var pcg = Sample.Parse();
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetSetListSlotColor(pcg, 0, 1, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetSetListSlotColor(pcg, 0, 1, -1));
    }

    [Fact]
    public void SetVolume_round_trips_and_preserves_everything_else()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0].Slots[1];

        var edited = PcgEditor.SetSetListSlotVolume(pcg, 0, 1, 100);
        var slot = SetListReader.Read(PcgReader.Parse(edited))[0].Slots[1];

        Assert.Equal(100, slot.Volume);
        Assert.Equal(before.Reference.Kind, slot.Reference.Kind);
        Assert.Equal(before.Reference.Bank, slot.Reference.Bank);
        Assert.Equal(before.Reference.Index, slot.Reference.Index);
        Assert.Equal(before.Name, slot.Name);
        Assert.Equal(before.Color, slot.Color);
        Assert.Equal(before.Transpose, slot.Transpose);
        Assert.Equal(before.HoldTimeIndex, slot.HoldTimeIndex);
        AssertChecksumsValid(pcg, edited);
    }

    // Transpose is the split field: semitones ×32 across byte 5 (low) and the bank byte's
    // top 3 bits — the probe stored +8 as B1 0x20 / B5 0x00 and −1 as 0xE0 / 0xE0.
    [Fact]
    public void SetTranspose_round_trips_and_writes_the_probe_byte_layout()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0].Slots[1]; // Combi USER-A (bank 7)

        var plus8 = SetListReader.Read(PcgReader.Parse(
            PcgEditor.SetSetListSlotTranspose(pcg, 0, 1, 8)))[0].Slots[1];
        Assert.Equal(8, plus8.Transpose);
        Assert.Equal(0x20, plus8.Reference.Raw[1] & 0xE0); // high bits in the bank byte
        Assert.Equal(0x00, plus8.Reference.Raw[5]);        // low byte zero
        Assert.Equal(before.Reference.Bank, plus8.Reference.Bank); // bank preserved

        var minus1 = SetListReader.Read(PcgReader.Parse(
            PcgEditor.SetSetListSlotTranspose(pcg, 0, 1, -1)))[0].Slots[1];
        Assert.Equal(-1, minus1.Transpose);
        Assert.Equal(0xE0, minus1.Reference.Raw[1] & 0xE0);
        Assert.Equal(0xE0, minus1.Reference.Raw[5]);
        Assert.Equal(before.Reference.Bank, minus1.Reference.Bank);

        var zero = SetListReader.Read(PcgReader.Parse(
            PcgEditor.SetSetListSlotTranspose(pcg, 0, 1, 0)))[0].Slots[1];
        Assert.Equal(0, zero.Transpose);
        Assert.Equal(before.Reference.Bank, zero.Reference.Bank);
        Assert.Equal(before.Color, zero.Color);
        Assert.Equal(before.Volume, zero.Volume);
        Assert.Equal(before.HoldTimeIndex, zero.HoldTimeIndex);

        AssertChecksumsValid(pcg, PcgEditor.SetSetListSlotTranspose(pcg, 0, 1, -12));
    }

    [Fact]
    public void SetHoldTime_round_trips_with_its_label()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0].Slots[1];

        var edited = PcgEditor.SetSetListSlotHoldTime(pcg, 0, 1, 18);
        var slot = SetListReader.Read(PcgReader.Parse(edited))[0].Slots[1];

        Assert.Equal(18, slot.HoldTimeIndex);
        Assert.Equal("30 sec", slot.HoldTimeLabel);
        Assert.Equal(before.Color, slot.Color);
        Assert.Equal(before.Volume, slot.Volume);
        Assert.Equal(before.Transpose, slot.Transpose);
        AssertChecksumsValid(pcg, edited);
    }

    [Fact]
    public void Slot_setting_writers_reject_out_of_range_values()
    {
        var pcg = Sample.Parse();
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetSetListSlotVolume(pcg, 0, 1, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetSetListSlotVolume(pcg, 0, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetSetListSlotHoldTime(pcg, 0, 1, 23));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetSetListSlotHoldTime(pcg, 0, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetSetListSlotTranspose(pcg, 0, 1, 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetSetListSlotTranspose(pcg, 0, 1, -13));
    }

    private static void AssertChecksumsValid(PcgFile pcg, byte[] edited)
    {
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
    public void Repoint_keeps_the_slot_color()
    {
        var pcg = Sample.Parse();
        var before = SetListReader.Read(pcg)[0].Slots[1];

        var edited = PcgEditor.RepointSetListSlot(pcg, 0, 1, PcgItemKind.Combi, 7, 58);
        var slot = SetListReader.Read(PcgReader.Parse(edited))[0].Slots[1];

        Assert.Equal(before.Color, slot.Color); // color rides B0 bits 2–5; repoint masks them off
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
