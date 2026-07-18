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

    /// <summary>Engine type per program bank (parallel to <see cref="ProgramBanks"/>);
    /// null = bank absent from the file.</summary>
    public required IReadOnlyList<ProgramBankType?> ProgramBankTypes { get; init; }

    public required IReadOnlyList<IReadOnlyList<string>> CombiBanks { get; init; }

    /// <summary>Drum kit names per bank (<c>DBK1</c> under <c>DKT1</c>; same record layout).</summary>
    public required IReadOnlyList<IReadOnlyList<string>> DrumKitBanks { get; init; }

    /// <summary>Wave sequence names per bank (<c>WBK1</c> under <c>WSQ1</c>; same record layout).</summary>
    public required IReadOnlyList<IReadOnlyList<string>> WaveSequenceBanks { get; init; }

    /// <summary>The file's own category names from GLB1, or null when it carries none
    /// (sound packs) — the name methods then fall back to the factory tables.</summary>
    public required GlobalReader.GlobalInfo? Global { get; init; }

    /// <summary>Program category name as this file defines it (GLB1), falling back to the
    /// factory table — a renamed user category shows the owner's name.</summary>
    public string ProgramCategoryName(int category) =>
        Global is { } g && category >= 0 && category < g.ProgramCategoryNames.Count
        && g.ProgramCategoryNames[category].Length > 0
            ? g.ProgramCategoryNames[category]
            : ProgramCategories.Name(category);

    /// <summary>Combi category name as this file defines it (GLB1), with factory fallback.</summary>
    public string CombiCategoryName(int category) =>
        Global is { } g && category >= 0 && category < g.CombiCategoryNames.Count
        && g.CombiCategoryNames[category].Length > 0
            ? g.CombiCategoryNames[category]
            : CombiCategories.Name(category);

    public static PcgCatalog Build(PcgFile pcg)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var programChunks = PcgBankIdentity.CanonicalBanks(pcg, "PRG1");
        return new PcgCatalog
        {
            ProgramBanks = ReadSection(pcg, "PRG1"),
            ProgramBankTypes = programChunks.Select(c => PcgBankIdentity.TypeFromChunkId(c?.Id)).ToList(),
            CombiBanks = ReadSection(pcg, "CMB1"),
            DrumKitBanks = ReadSection(pcg, "DKT1"),
            WaveSequenceBanks = ReadSection(pcg, "WSQ1"),
            Global = GlobalReader.Read(pcg),
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
    // banks are I-A..I-F (PcgId 0..5), U-A..U-G (17..23), U-AA..U-GG (24..30), keyed at canonical
    // list indices 0..19 by PcgBankIdentity. Ids with no in-file bank (GM=6, gaps 7..16, virtual 31+)
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

    // Banks are keyed by canonical list index; a bank the file doesn't carry is an empty list.
    private static IReadOnlyList<IReadOnlyList<string>> ReadSection(PcgFile pcg, string sectionId)
    {
        var chunks = PcgBankIdentity.CanonicalBanks(pcg, sectionId);
        var banks = new List<IReadOnlyList<string>>(chunks.Count);
        foreach (var bank in chunks)
            banks.Add(bank is null ? Array.Empty<string>() : ReadBankNames(pcg.Data, bank));
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
