using System.Buffers.Binary;
using System.Text;
using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The bank-identity word (bank sub-header offset +8) keys banks to canonical list indices,
/// so partial files — vendor packs shipping only a few USER banks — label and resolve
/// correctly. Discovered via a pack that ships programs in U-EE/U-FF and combis in U-E only.
/// </summary>
public class PcgBankIdentityTests
{
    // ----- Identity-word decoding -----

    [Theory]
    [InlineData("PRG1", 0u, 0)]            // INT-A
    [InlineData("PRG1", 4u, 4)]            // INT-E
    [InlineData("PRG1", 0x8000u, 5)]       // INT-F is the outlier value
    [InlineData("PRG1", 0x20000u, 6)]      // USER-A
    [InlineData("PRG1", 0x2000Bu, 17)]     // USER-EE (the vendor-pack case)
    [InlineData("PRG1", 0x2000Du, 19)]     // USER-GG
    [InlineData("PRG1", 5u, -1)]           // plain 5 is not how INT-F is written
    [InlineData("PRG1", 0x2000Eu, -1)]
    [InlineData("CMB1", 6u, 6)]            // INT-G
    [InlineData("CMB1", 0x20004u, 11)]     // USER-E (the vendor-pack case)
    [InlineData("CMB1", 0x20006u, 13)]     // USER-G
    [InlineData("CMB1", 7u, -1)]
    [InlineData("DKT1", 0u, 0)]            // INT
    [InlineData("DKT1", 0x2000Cu, 13)]     // USER-FF
    [InlineData("WSQ1", 0x2000Du, 14)]     // USER-GG
    [InlineData("GLB1", 0u, -1)]           // unmapped section
    public void CanonicalIndex_decodes_identity_words(string sectionId, uint identity, int expected) =>
        Assert.Equal(expected, PcgBankIdentity.CanonicalIndex(sectionId, identity));

    // ----- Partial files (synthetic) -----

    [Fact]
    public void Partial_file_banks_key_by_identity()
    {
        var pcg = PcgReader.Parse(BuildFile(
            Section("PRG1",
                Bank("MBK1", 0x2000B, "AUD-EE000"),   // U-EE
                Bank("PBK1", 0x2000C, "AUD-FF000")),  // U-FF
            Section("CMB1",
                Bank("CBK1", 0x20004, "AUD-COMBI"))));  // U-E

        var catalog = PcgCatalog.Build(pcg);

        Assert.Equal(20, catalog.ProgramBanks.Count);
        Assert.Equal("AUD-EE000", catalog.ProgramBanks[17][0]);
        Assert.Equal("AUD-FF000", catalog.ProgramBanks[18][0]);
        Assert.Empty(catalog.ProgramBanks[0]);

        Assert.Equal(14, catalog.CombiBanks.Count);
        Assert.Equal("AUD-COMBI", catalog.CombiBanks[11][0]);

        // Set-list/timbre references resolve through the same canonical indices:
        // program refs by hardware PcgId (U-EE = 28), combi refs by direct index (U-E = 11).
        Assert.Equal("AUD-EE000", catalog.ResolveProgram(28, 0));
        Assert.Equal("AUD-COMBI", catalog.Resolve(CombiRef(bank: 11, index: 0)));
    }

    [Fact]
    public void Unrecognized_identity_falls_back_to_positional_order()
    {
        var pcg = PcgReader.Parse(BuildFile(
            Section("PRG1",
                Bank("MBK1", 999_999, "FIRST"),
                Bank("MBK1", 0x2000B, "SECOND"))));

        var catalog = PcgCatalog.Build(pcg);

        Assert.Equal(2, catalog.ProgramBanks.Count);
        Assert.Equal("FIRST", catalog.ProgramBanks[0][0]);
        Assert.Equal("SECOND", catalog.ProgramBanks[1][0]);
    }

    [Fact]
    public void Duplicate_identities_fall_back_to_positional_order()
    {
        var pcg = PcgReader.Parse(BuildFile(
            Section("PRG1",
                Bank("MBK1", 0x20000, "FIRST"),
                Bank("MBK1", 0x20000, "SECOND"))));

        var catalog = PcgCatalog.Build(pcg);

        Assert.Equal(2, catalog.ProgramBanks.Count);
        Assert.Equal("FIRST", catalog.ProgramBanks[0][0]);
        Assert.Equal("SECOND", catalog.ProgramBanks[1][0]);
    }

    [Fact]
    public void Editor_addresses_partial_file_banks_canonically()
    {
        var pcg = PcgReader.Parse(BuildFile(
            Section("PRG1",
                Bank("MBK1", 0x2000B, "AUD-EE000", "AUD-EE001"))));

        var renamed = PcgReader.Parse(PcgEditor.RenameProgram(pcg, bank: 17, index: 1, "RENAMED"));
        Assert.Equal("RENAMED", PcgCatalog.Build(renamed).ProgramBanks[17][1]);

        // Banks the file doesn't carry can't be edited.
        Assert.Throws<InvalidOperationException>(() => PcgEditor.RenameProgram(pcg, bank: 0, index: 0, "X"));
    }

    [Fact]
    public void Compat_tolerates_disjoint_bank_sets_but_not_layouts()
    {
        var partial = PcgReader.Parse(BuildFile(
            Section("PRG1", Bank("MBK1", 0x2000B, "AUD-EE000"))));
        var fullish = PcgReader.Parse(BuildFile(
            Section("PRG1", Bank("MBK1", 0x00000, "INT-A000"), Bank("MBK1", 0x2000B, "OTHER"))));
        var otherModel = PcgReader.Parse(BuildFile(
            Section("PRG1", Bank("MBK1", 0x00000, "INT-A000", recordSize: 96))));

        Assert.True(PcgCompat.Compare(partial, fullish).ProgramsMatch);
        Assert.False(PcgCompat.Compare(partial, otherModel).ProgramsMatch);
    }

    // ----- Full dump (sample): canonical order equals stored order -----

    [Fact]
    public void Sample_full_dump_keys_banks_at_their_stored_positions()
    {
        var pcg = Sample.Parse();
        var catalog = PcgCatalog.Build(pcg);
        var prg = pcg.FindFirst("PRG1");
        Assert.NotNull(prg);

        Assert.Equal(prg!.Children.Count, catalog.ProgramBanks.Count);
        for (int i = 0; i < prg.Children.Count; i++)
        {
            var storedFirstName = FixedName(pcg.Data, prg.Children[i].DataOffset + 12);
            Assert.Equal(storedFirstName, catalog.ProgramBanks[i][0]);
        }
    }

    private static string FixedName(byte[] data, long offset)
    {
        var raw = Encoding.ASCII.GetString(data, (int)offset, 24);
        int nul = raw.IndexOf('\0');
        return (nul < 0 ? raw : raw[..nul]).TrimEnd();
    }

    // ----- Synthetic-file construction -----

    private const int RecordSize = 64;

    private static SetListReference CombiRef(int bank, int index) => new()
    {
        Kind = PcgItemKind.Combi,
        Bank = bank,
        Index = index,
        Raw = new byte[] { 0, (byte)bank, (byte)index },
    };

    private static byte[] Bank(string chunkId, uint identity, params string[] names) =>
        Bank(chunkId, identity, names, RecordSize);

    private static byte[] Bank(string chunkId, uint identity, string name, int recordSize) =>
        Bank(chunkId, identity, new[] { name }, recordSize);

    private static byte[] Bank(string chunkId, uint identity, string[] names, int recordSize)
    {
        var data = new byte[12 + names.Length * recordSize];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)names.Length);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)recordSize);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), identity);
        for (int i = 0; i < names.Length; i++)
            Encoding.ASCII.GetBytes(names[i]).CopyTo(data, 12 + i * recordSize);
        return Chunk(chunkId, data, checksummed: true);
    }

    private static byte[] Section(string sectionId, params byte[][] banks) =>
        Chunk(sectionId, Concat(banks), checksummed: false);

    private static byte[] BuildFile(params byte[][] sections)
    {
        var pcg1 = Chunk("PCG1", Concat(sections), checksummed: false);
        var file = new byte[PcgReader.FileHeaderSize + pcg1.Length];
        Encoding.ASCII.GetBytes("KORG").CopyTo(file, 0);
        pcg1.CopyTo(file, PcgReader.FileHeaderSize);
        return file;
    }

    // Chunk header is id(4) + big-endian size(4) + field(4); leaves carry an 8-bit additive
    // data checksum in the field's low byte, which edits recompute.
    private static byte[] Chunk(string id, byte[] data, bool checksummed)
    {
        var buf = new byte[PcgReader.ChunkHeaderSize + data.Length];
        Encoding.ASCII.GetBytes(id).CopyTo(buf, 0);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4), (uint)data.Length);
        if (checksummed)
        {
            uint sum = 0;
            foreach (var b in data)
                sum += b;
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8), sum & 0xFF);
        }
        data.CopyTo(buf, PcgReader.ChunkHeaderSize);
        return buf;
    }

    private static byte[] Concat(byte[][] parts)
    {
        var total = new byte[parts.Sum(p => p.Length)];
        int offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(total, offset);
            offset += part.Length;
        }
        return total;
    }
}
