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

    /// <summary>Resolves a slot reference to its target name, or null if out of range or empty.</summary>
    public string? Resolve(SetListReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var banks = reference.Kind == PcgItemKind.Program ? ProgramBanks : CombiBanks;
        if (reference.Bank < 0 || reference.Bank >= banks.Count)
            return null;
        var bank = banks[reference.Bank];
        if (reference.Index < 0 || reference.Index >= bank.Count)
            return null;
        var name = bank[reference.Index];
        return string.IsNullOrEmpty(name) ? null : name;
    }

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
