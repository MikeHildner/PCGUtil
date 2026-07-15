using System.Buffers.Binary;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgEditorCrossFileTests
{
    [Fact]
    public void CopyProgramAcross_lands_the_source_record_and_touches_nothing_else()
    {
        // Two independent files (same model by construction); give the source a unique name.
        var source = PcgReader.Parse(PcgEditor.RenameProgram(Sample.Parse(), 0, 0, "Xfile Prog Zz9"));
        var destination = Sample.Parse();

        // INT-A is EXi, so the destination must be an EXi bank too (USER-CC = 15).
        var edited = PcgEditor.CopyProgramAcross(source, 0, 0, destination, 15, 5);
        var catalog = PcgCatalog.Build(PcgReader.Parse(edited));

        Assert.Equal("Xfile Prog Zz9", catalog.ProgramBanks[15][5]);          // landed
        Assert.Equal("Berlin Grand SW2 U.C.", catalog.ProgramBanks[0][0]);    // destination (0,0) untouched
        Assert.Equal("Xfile Prog Zz9",
            PcgCatalog.Build(source).ProgramBanks[0][0]);                     // source unchanged
    }

    [Fact]
    public void CopyCombiAcross_moves_the_whole_record_bytes()
    {
        var source = PcgReader.Parse(PcgEditor.RenameCombi(Sample.Parse(), 7, 57, "Xfile Combi Zz9"));
        var destination = Sample.Parse();

        var edited = PcgEditor.CopyCombiAcross(source, 7, 57, destination, 0, 0);
        var pcg2 = PcgReader.Parse(edited);

        Assert.Equal("Xfile Combi Zz9", PcgCatalog.Build(pcg2).CombiBanks[0][0]);

        // The destination record is byte-identical to the source record (timbres traveled).
        var srcRecord = RecordBytes(source, "CMB1", 7, 57);
        var dstRecord = RecordBytes(pcg2, "CMB1", 0, 0);
        Assert.Equal(srcRecord, dstRecord);
    }

    [Fact]
    public void CopySetListSlotAcross_carries_name_and_reference()
    {
        var source = Sample.Parse();
        var destination = Sample.Parse();
        var srcSlot = SetListReader.Read(source)[0].Slots[1]; // "Let's Go Crazy" → Combi USER-A #057

        var edited = PcgEditor.CopySetListSlotAcross(source, 0, 1, destination, 1, 0);
        var dstSlot = SetListReader.Read(PcgReader.Parse(edited))[1].Slots[0];

        Assert.Equal(srcSlot.Name, dstSlot.Name);
        Assert.Equal(srcSlot.Reference.Kind, dstSlot.Reference.Kind);
        Assert.Equal(srcSlot.Reference.Bank, dstSlot.Reference.Bank);
        Assert.Equal(srcSlot.Reference.Index, dstSlot.Reference.Index);
    }

    [Fact]
    public void CopyAcross_keeps_every_leaf_checksum_valid()
    {
        var source = PcgReader.Parse(PcgEditor.RenameProgram(Sample.Parse(), 0, 0, "Xfile Sum Zz9"));
        var destination = Sample.Parse();
        var edited = PcgEditor.CopyProgramAcross(source, 0, 0, destination, 15, 5); // EXi → EXi

        foreach (var chunk in destination.EnumerateChunks())
        {
            if (chunk.HasChildren || chunk.DataOffset < 1 || chunk.Size <= 0) continue;
            if (chunk.DataEnd > destination.Data.Length) continue;

            byte stored = destination.Data[chunk.DataOffset - 1];
            if (stored != PcgChecksum.Sum(destination.Data, chunk.DataOffset, chunk.Size)) continue;

            Assert.Equal(PcgChecksum.Sum(edited, chunk.DataOffset, chunk.Size), edited[chunk.DataOffset - 1]);
        }
    }

    [Fact]
    public void CopyProgramAcross_rejects_mismatched_record_layouts()
    {
        var pristine = Sample.Parse();
        var patched = ParseWithPatchedProgramRecordSize(pristine);

        Assert.False(PcgCompat.Compare(pristine, patched).ProgramsMatch);
        Assert.Throws<InvalidOperationException>(
            () => PcgEditor.CopyProgramAcross(patched, 0, 0, pristine, 0, 1));
    }

    [Fact]
    public void PcgCompat_reports_same_file_as_full_match()
    {
        var result = PcgCompat.Compare(Sample.Parse(), Sample.Parse());
        Assert.True(result.AllMatch);
        Assert.Equal(string.Empty, result.MismatchSummary);
    }

    // A copy of the sample whose PRG1 bank-0 sub-header claims a smaller record size —
    // parses fine (the chunk tree is untouched) but no longer matches the pristine layout.
    private static PcgFile ParseWithPatchedProgramRecordSize(PcgFile pristine)
    {
        var bank0 = pristine.FindFirst("PRG1")!.Children[0];
        var bytes = (byte[])pristine.Data.Clone();
        int sizeOffset = (int)bank0.DataOffset + 4;
        uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sizeOffset, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(sizeOffset, 4), size - 4);
        return PcgReader.Parse(bytes);
    }

    // Raw bytes of one bank record (12-byte sub-header, fixed-size records).
    private static byte[] RecordBytes(PcgFile pcg, string sectionId, int bank, int index)
    {
        var bankChunk = pcg.FindFirst(sectionId)!.Children[bank];
        long baseOffset = bankChunk.DataOffset;
        int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset + 4, 4));
        long start = baseOffset + 12 + (long)index * recordSize;
        return pcg.Data.AsSpan((int)start, recordSize).ToArray();
    }
}
