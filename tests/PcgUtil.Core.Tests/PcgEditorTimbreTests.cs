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
