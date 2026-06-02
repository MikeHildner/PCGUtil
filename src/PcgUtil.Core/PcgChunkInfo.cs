namespace PcgUtil.Core;

/// <summary>
/// Human-readable descriptions for known PCG chunk ids. These labels are best-effort,
/// inferred from the PCG chunk layout and observed files; unknown ids fall back
/// to "Unknown".
/// </summary>
public static class PcgChunkInfo
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PCG1"] = "PCG container (whole file)",
            ["DIV1"] = "Header / version block",
            ["SLS1"] = "Set Lists",
            ["SLD1"] = "Set List data",
            ["SDB1"] = "Set List slot database",
            ["STL1"] = "Set List table",
            ["SBK1"] = "Set List bank",
            ["PRG1"] = "Programs",
            ["PBK1"] = "Program bank",
            ["MBK1"] = "Program bank (M)",
            ["CMB1"] = "Combinations",
            ["CBK1"] = "Combination bank",
            ["DKT1"] = "Drum Kits",
            ["DBK1"] = "Drum Kit bank",
            ["WSQ1"] = "Wave Sequences",
            ["WBK1"] = "Wave Sequence bank",
            ["GLB1"] = "Global settings",
        };

    public static string Describe(string id) =>
        Descriptions.TryGetValue(id, out var description) ? description : "Unknown";

    public static bool IsKnown(string id) => Descriptions.ContainsKey(id);
}
