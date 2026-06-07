using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Names of every Program and Combi in the file, indexed by bank and number, so
/// Set List slot references can be resolved to a name.
///
/// Each program/combi bank (<c>PBK1</c>/<c>MBK1</c> under <c>PRG1</c>, <c>CBK1</c>
/// under <c>CMB1</c>) is a 12-byte sub-header (count, record size) followed by
/// fixed-size records whose first 24 bytes are the name.
/// </summary>
public sealed class PcgCatalog
{
    public required IReadOnlyList<IReadOnlyList<string>> ProgramBanks { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> CombiBanks { get; init; }

    public static PcgCatalog Build(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        return new PcgCatalog
        {
            ProgramBanks = ReadSection(pcg, "PRG1"),
            CombiBanks = ReadSection(pcg, "CMB1"),
        };
    }

    /// <summary>Resolves a slot reference to its target name, or null if unresolved or empty.</summary>
    public string? Resolve(SetListReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.Kind switch
        {
            // Program banks are addressed by a hardware PcgId; combi banks by direct list index.
            PcgItemKind.Program => ResolveProgram(reference.Bank, reference.Index),
            PcgItemKind.Combi => Lookup(CombiBanks, reference.Bank, reference.Index),
            _ => null, // Song: lives in the sequencer, not in Program/Combi banks.
        };
    }

    /// <summary>
    /// Resolves a program by its raw bank <em>PcgId</em> (as stored in set-list slots and combi
    /// timbres) plus number, or null if the id maps to no in-file bank or the program is empty.
    /// </summary>
    public string? ResolveProgram(int bankPcgId, int number) =>
        Lookup(ProgramBanks, ProgramBankIndexForPcgId(bankPcgId), number);

    private static string? Lookup(IReadOnlyList<IReadOnlyList<string>> banks, int bankIndex, int number)
    {
        if (bankIndex < 0 || bankIndex >= banks.Count)
            return null;
        var bank = banks[bankIndex];
        if (number < 0 || number >= bank.Count)
            return null;
        var name = bank[number];
        return string.IsNullOrEmpty(name) ? null : name;
    }

    // Program-bank references store a hardware PcgId, not a list index. On this hardware the program
    // banks are I-A..I-F (PcgId 0..5), U-A..U-G (17..23), U-AA..U-GG (24..30), stored in the file
    // in that order (list indices 0..19). Ids with no in-file bank (GM=6, gaps 7..16, virtual 31+)
    // return -1. Verified against the sample: this resolves ~99.7% of combi timbres.
    public static int ProgramBankIndexForPcgId(int pcgId) => pcgId switch
    {
        >= 0 and <= 5 => pcgId,
        >= 17 and <= 30 => pcgId - 11,
        _ => -1,
    };

    /// <summary>Inverse of <see cref="ProgramBankIndexForPcgId"/>: program-bank list index → hardware PcgId.</summary>
    public static int ProgramBankPcgIdForIndex(int listIndex) => listIndex switch
    {
        >= 0 and <= 5 => listIndex,        // I-A..I-F
        >= 6 and <= 19 => listIndex + 11,  // U-A..U-G (6→17) .. U-AA..U-GG (13→24 .. 19→30)
        _ => -1,
    };

    private const int BankNameLength = 24;

    private static IReadOnlyList<IReadOnlyList<string>> ReadSection(PcgFile pcg, string sectionId)
    {
        var section = pcg.FindFirst(sectionId);
        if (section is null)
            return Array.Empty<IReadOnlyList<string>>();

        var banks = new List<IReadOnlyList<string>>(section.Children.Count);
        foreach (var bank in section.Children)
            banks.Add(ReadBankNames(pcg.Data, bank));
        return banks;
    }

    private static IReadOnlyList<string> ReadBankNames(byte[] data, PcgChunk bank)
    {
        long baseOffset = bank.DataOffset;
        if (baseOffset + 12 > data.Length)
            return Array.Empty<string>();

        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
        int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));
        long recordsStart = baseOffset + 12;
        if (count <= 0 || recordSize <= 0)
            return Array.Empty<string>();

        var names = new string[count];
        for (int i = 0; i < count; i++)
            names[i] = PcgText.ReadFixedString(data, recordsStart + (long)i * recordSize, BankNameLength);
        return names;
    }
}
