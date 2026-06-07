using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgChecksumTests
{
    [Fact]
    public void Sample_starts_with_valid_leaf_checksums() =>
        AssertChecksumsValid(Sample.Bytes(), Sample.Bytes());

    [Fact]
    public void Rename_set_list_slot_keeps_checksums_valid()
    {
        var pcg = Sample.Parse();
        AssertChecksumsValid(pcg.Data, PcgEditor.RenameSetListSlot(pcg, 0, 1, "CHECKSUM TEST"));
    }

    [Fact]
    public void Rename_combi_keeps_checksums_valid()
    {
        var pcg = Sample.Parse();
        AssertChecksumsValid(pcg.Data, PcgEditor.RenameCombi(pcg, 7, 57, "Renamed"));
    }

    [Fact]
    public void Combi_swap_keeps_checksums_valid()
    {
        var pcg = Sample.Parse();
        AssertChecksumsValid(pcg.Data, PcgEditor.SwapCombis(pcg, 7, 57, 7, 58));
    }

    [Fact]
    public void Program_swap_keeps_checksums_valid()
    {
        var pcg = Sample.Parse();
        AssertChecksumsValid(pcg.Data, PcgEditor.SwapPrograms(pcg, 0, 0, 0, 1));
    }

    [Fact]
    public void Copy_combi_keeps_checksums_valid()
    {
        var pcg = Sample.Parse();
        AssertChecksumsValid(pcg.Data, PcgEditor.CopyCombi(pcg, 7, 57, 0, 0));
    }

    // For every leaf chunk checksummed in the original, the edited file's stored checksum byte
    // must equal the sum of its (possibly changed) data.
    private static void AssertChecksumsValid(byte[] originalData, byte[] editedData)
    {
        var orig = PcgReader.Parse(originalData);
        int verified = 0;
        foreach (var c in orig.EnumerateChunks())
        {
            if (c.HasChildren || c.DataOffset < 1 || c.Size <= 0 || c.DataEnd > originalData.Length) continue;
            byte stored = (byte)(c.Field & 0xFF);
            if (stored != PcgChecksum.Sum(originalData, c.DataOffset, c.Size)) continue; // not checksummed
            verified++;
            byte editedStored = editedData[c.DataOffset - 1];
            byte editedSum = PcgChecksum.Sum(editedData, c.DataOffset, c.Size);
            Assert.True(editedStored == editedSum,
                $"{c.Id} @0x{c.DataOffset:X}: stored=0x{editedStored:X2} dataSum=0x{editedSum:X2}");
        }
        Assert.True(verified > 0, "expected some checksummed chunks");
    }
}
