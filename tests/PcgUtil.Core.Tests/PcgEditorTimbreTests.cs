using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgEditorTimbreTests
{
    // "Let's Go Crazy", combi USER-A #057 in the sample — a real gig combi with live timbres.
    private const int Bank = 7;
    private const int Index = 57;

    private static CombiTimbre TimbreAfter(byte[] edited, int timbre) =>
        CombiReader.Read(PcgReader.Parse(edited))
            .Single(c => c.Bank == Bank && c.Index == Index).Timbres[timbre];

    private static CombiTimbre TimbreBefore(PcgFile pcg, int timbre) =>
        CombiReader.Read(pcg).Single(c => c.Bank == Bank && c.Index == Index).Timbres[timbre];

    [Fact]
    public void SetTimbreKeyZone_round_trips_and_preserves_everything_else()
    {
        var pcg = Sample.Parse();
        var before = TimbreBefore(pcg, 0);

        var edited = PcgEditor.SetTimbreKeyZone(pcg, Bank, Index, timbre: 0, bottomKey: 54, topKey: 59);

        var after = TimbreAfter(edited, 0);
        Assert.Equal(54, after.BottomKey);
        Assert.Equal(59, after.TopKey);
        Assert.True(after.HasKeyZone);
        // Siblings preserved: the program reference, the OTHER zone pair, and the mix fields.
        Assert.Equal(before.ProgramNumber, after.ProgramNumber);
        Assert.Equal(before.ProgramBankPcgId, after.ProgramBankPcgId);
        Assert.Equal(before.BottomVelocity, after.BottomVelocity);
        Assert.Equal(before.TopVelocity, after.TopVelocity);
        Assert.Equal(before.Volume, after.Volume);
        Assert.Equal(before.Transpose, after.Transpose);
        Assert.Equal(before.Status, after.Status);
        AssertChecksumsValid(pcg, edited);
    }

    [Fact]
    public void SetTimbreKeyZone_writes_top_before_bottom_in_the_record()
    {
        // The byte order is the classic trap: +37 is the TOP key, +38 the bottom.
        var pcg = Sample.Parse();
        var edited = PcgEditor.SetTimbreKeyZone(pcg, Bank, Index, timbre: 0, bottomKey: 54, topKey: 59);

        var banks = PcgBankIdentity.CanonicalBanks(pcg, "CMB1");
        var chunk = banks[Bank]!;
        int recordSize = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            pcg.Data.AsSpan((int)chunk.DataOffset + 4, 4));
        long tOff = chunk.DataOffset + 12 + (long)Index * recordSize + CombiReader.TimbresOffset;
        Assert.Equal(59, edited[tOff + 37]); // top
        Assert.Equal(54, edited[tOff + 38]); // bottom
    }

    [Fact]
    public void SetTimbreVelocityZone_round_trips_and_preserves_the_key_zone()
    {
        var pcg = Sample.Parse();
        var before = TimbreBefore(pcg, 1);

        var edited = PcgEditor.SetTimbreVelocityZone(pcg, Bank, Index, timbre: 1, bottomVelocity: 89, topVelocity: 127);

        var after = TimbreAfter(edited, 1);
        Assert.Equal(89, after.BottomVelocity);
        Assert.Equal(127, after.TopVelocity);
        Assert.Equal(before.BottomKey, after.BottomKey);
        Assert.Equal(before.TopKey, after.TopKey);
        Assert.Equal(before.ProgramNumber, after.ProgramNumber);
        AssertChecksumsValid(pcg, edited);
    }

    [Fact]
    public void SetTimbreVolume_and_transpose_round_trip()
    {
        var pcg = Sample.Parse();
        var before = TimbreBefore(pcg, 0);

        var volEdited = PcgEditor.SetTimbreVolume(pcg, Bank, Index, timbre: 0, volume: 100);
        Assert.Equal(100, TimbreAfter(volEdited, 0).Volume);
        Assert.Equal(before.Transpose, TimbreAfter(volEdited, 0).Transpose);

        var xpEdited = PcgEditor.SetTimbreTranspose(pcg, Bank, Index, timbre: 0, semitones: -12);
        Assert.Equal(-12, TimbreAfter(xpEdited, 0).Transpose);
        Assert.Equal(before.Volume, TimbreAfter(xpEdited, 0).Volume);
        AssertChecksumsValid(pcg, xpEdited);
    }

    [Fact]
    public void SetTimbreProgram_repoints_the_layer_and_resolves_to_the_new_name()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var before = TimbreBefore(pcg, 0);

        // Point T1 at a named program in a USER bank, where the stored PcgId and the list
        // index differ — the classic way to get this wrong.
        const int dstBank = 8; // USER-C
        int dstIndex = Enumerable.Range(0, catalog.ProgramBanks[dstBank].Count)
            .First(i => !PcgOrganizer.IsProgramPlaceholder(catalog.ProgramBanks[dstBank][i]));
        string expected = catalog.ProgramBanks[dstBank][dstIndex];

        var edited = PcgEditor.SetTimbreProgram(pcg, Bank, Index, timbre: 0, dstBank, dstIndex);

        var after = TimbreAfter(edited, 0);
        Assert.Equal(dstIndex, after.ProgramNumber);
        Assert.Equal(PcgCatalog.ProgramBankPcgIdForIndex(dstBank), after.ProgramBankPcgId);
        Assert.NotEqual(dstBank, after.ProgramBankPcgId); // a PcgId, not the list index
        Assert.Equal(expected, PcgCatalog.Build(PcgReader.Parse(edited))
            .ResolveProgram(after.ProgramBankPcgId, after.ProgramNumber));

        // The rest of the timbre is untouched.
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.MidiChannel, after.MidiChannel);
        Assert.Equal(before.BottomKey, after.BottomKey);
        Assert.Equal(before.Volume, after.Volume);
        AssertChecksumsValid(pcg, edited);
    }

    [Fact]
    public void SetTimbreStatus_round_trips_every_value_and_keeps_the_midi_channel()
    {
        var pcg = Sample.Parse();
        var before = TimbreBefore(pcg, 0);

        foreach (var status in new[] { TimbreStatus.Off, TimbreStatus.Int, TimbreStatus.Both,
                                       TimbreStatus.Ext, TimbreStatus.Ex2 })
        {
            var edited = PcgEditor.SetTimbreStatus(pcg, Bank, Index, timbre: 0, status);
            var after = TimbreAfter(edited, 0);
            Assert.Equal(status, after.Status);
            // Status shares its byte with the MIDI channel — the whole point of masking.
            Assert.Equal(before.MidiChannel, after.MidiChannel);
            Assert.Equal(before.ProgramNumber, after.ProgramNumber);
            Assert.Equal(before.ProgramBankPcgId, after.ProgramBankPcgId);
        }

        AssertChecksumsValid(pcg, PcgEditor.SetTimbreStatus(pcg, Bank, Index, 0, TimbreStatus.Off));
    }

    [Fact]
    public void Silencing_a_timbre_and_waking_an_unused_one_are_both_expressible()
    {
        var pcg = Sample.Parse();

        // Turn a playing layer off.
        var silenced = PcgReader.Parse(PcgEditor.SetTimbreStatus(pcg, Bank, Index, 0, TimbreStatus.Off));
        var offTimbre = CombiReader.Read(silenced).Single(c => c.Bank == Bank && c.Index == Index).Timbres[0];
        Assert.Equal(TimbreStatus.Off, offTimbre.Status);
        Assert.False(offTimbre.UsesInternalProgram);

        // Find a combi with an Off timbre, wake it and give it a program: "add a layer".
        var target = CombiReader.Read(pcg)
            .First(c => !c.IsEmptyOrInit && c.Timbres.Any(t => t.Status == TimbreStatus.Off));
        int spare = target.Timbres.First(t => t.Status == TimbreStatus.Off).Index;

        var woken = PcgEditor.SetTimbreStatus(pcg, target.Bank, target.Index, spare, TimbreStatus.Int);
        var pointed = PcgEditor.SetTimbreProgram(PcgReader.Parse(woken), target.Bank, target.Index, spare, 0, 0);

        var added = CombiReader.Read(PcgReader.Parse(pointed))
            .Single(c => c.Bank == target.Bank && c.Index == target.Index).Timbres[spare];
        Assert.Equal(TimbreStatus.Int, added.Status);
        Assert.True(added.UsesInternalProgram);
        Assert.Equal(0, added.ProgramNumber);
        AssertChecksumsValid(pcg, pointed);
    }

    [Fact]
    public void Timbre_writers_reject_out_of_range_values()
    {
        var pcg = Sample.Parse();
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreKeyZone(pcg, Bank, Index, 0, -1, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreKeyZone(pcg, Bank, Index, 0, 0, 128));
        Assert.Throws<ArgumentException>(() => PcgEditor.SetTimbreKeyZone(pcg, Bank, Index, 0, 60, 54));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreVelocityZone(pcg, Bank, Index, 0, 0, 127));
        Assert.Throws<ArgumentException>(() => PcgEditor.SetTimbreVelocityZone(pcg, Bank, Index, 0, 90, 89));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreVolume(pcg, Bank, Index, 0, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreTranspose(pcg, Bank, Index, 0, 61));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreKeyZone(pcg, Bank, Index, 16, 0, 127));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreProgram(pcg, Bank, Index, 0, 0, 999));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreProgram(pcg, Bank, Index, 0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreProgram(pcg, Bank, Index, 0, 99, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcgEditor.SetTimbreStatus(pcg, Bank, Index, 0, (TimbreStatus)7));
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
}
