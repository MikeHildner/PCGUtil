namespace PcgUtil.Core;

/// <summary>
/// Human-readable KRONOS bank labels (as shown on the hardware), keyed by bank list index.
/// Programs: INT-A..INT-F, USER-A..USER-G, USER-AA..USER-GG (20 banks). Combis: INT-A..INT-G,
/// USER-A..USER-G (14 banks). Note the asymmetry — 6 internal program banks vs 7 combi banks.
/// </summary>
public static class PcgBankLabels
{
    /// <summary>Label for a program bank by its list index (matches <see cref="PcgCatalog.ProgramBanks"/>).</summary>
    public static string Program(int listIndex) => listIndex switch
    {
        >= 0 and <= 5 => $"INT-{(char)('A' + listIndex)}",        // INT-A..INT-F
        >= 6 and <= 12 => $"USER-{(char)('A' + listIndex - 6)}",  // USER-A..USER-G
        >= 13 and <= 19 => DoubledUser(listIndex - 13),           // USER-AA..USER-GG
        _ => Fallback(listIndex),
    };

    /// <summary>Label for a combi bank by its list index (matches <see cref="PcgCatalog.CombiBanks"/>).</summary>
    public static string Combi(int listIndex) => listIndex switch
    {
        >= 0 and <= 6 => $"INT-{(char)('A' + listIndex)}",        // INT-A..INT-G
        >= 7 and <= 13 => $"USER-{(char)('A' + listIndex - 7)}",  // USER-A..USER-G
        _ => Fallback(listIndex),
    };

    private static string DoubledUser(int i)
    {
        char c = (char)('A' + i);
        return $"USER-{c}{c}";
    }

    private static string Fallback(int listIndex) => listIndex >= 0 ? $"bank {listIndex:D2}" : "?";
}
