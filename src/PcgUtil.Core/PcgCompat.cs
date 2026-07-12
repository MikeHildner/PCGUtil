using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>Which sections of two files have matching record layouts.</summary>
public sealed record PcgCompatResult(bool ProgramsMatch, bool CombisMatch, bool SetListsMatch)
{
    public bool AllMatch => ProgramsMatch && CombisMatch && SetListsMatch;

    /// <summary>Names of the sections that differ, for display ("Programs, Combis").</summary>
    public string MismatchSummary
    {
        get
        {
            var parts = new List<string>(3);
            if (!ProgramsMatch) parts.Add("Programs");
            if (!CombisMatch) parts.Add("Combis");
            if (!SetListsMatch) parts.Add("Set Lists");
            return string.Join(", ", parts);
        }
    }
}

/// <summary>
/// Same-model heuristic for cross-file operations: two files are treated as compatible when
/// every bank they <em>both</em> carry (paired by canonical index) has the same record count
/// and size, and their record sizes agree across the section. A bank only one file carries —
/// vendor packs ship a subset — doesn't block compatibility: copies address specific banks,
/// and <see cref="PcgEditor"/> re-checks record sizes per operation before landing raw bytes.
/// </summary>
public static class PcgCompat
{
    public static PcgCompatResult Compare(PcgFile a, PcgFile b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new PcgCompatResult(
            BankSectionMatches(a, b, "PRG1"),
            BankSectionMatches(a, b, "CMB1"),
            SetListsMatch(a, b));
    }

    private static bool BankSectionMatches(PcgFile a, PcgFile b, string sectionId)
    {
        var banksA = PcgBankIdentity.CanonicalBanks(a, sectionId);
        var banksB = PcgBankIdentity.CanonicalBanks(b, sectionId);
        if (banksA.Count == 0 || banksB.Count == 0)
            return banksA.Count == banksB.Count; // section absent from just one file → mismatch

        // Every record size in the section must agree between the files (same-model check even
        // when the files share no banks), and shared banks must have identical layouts.
        var sizesA = banksA.OfType<PcgChunk>().Select(c => Layout(a, c).RecordSize).Distinct().ToList();
        var sizesB = banksB.OfType<PcgChunk>().Select(c => Layout(b, c).RecordSize).Distinct().ToList();
        if (sizesA.Count != 1 || sizesB.Count != 1 || sizesA[0] != sizesB[0])
            return false;

        for (int i = 0; i < Math.Min(banksA.Count, banksB.Count); i++)
        {
            if (banksA[i] is not { } chunkA || banksB[i] is not { } chunkB)
                continue;
            if (Layout(a, chunkA) != Layout(b, chunkB))
                return false;
        }
        return true;
    }

    // Set lists live in a single SBK1 leaf whose data starts with the same count/record-size
    // sub-header as a bank.
    private static bool SetListsMatch(PcgFile a, PcgFile b)
    {
        var sbkA = a.FindFirst("SBK1");
        var sbkB = b.FindFirst("SBK1");
        if (sbkA is null || sbkB is null)
            return (sbkA is null) == (sbkB is null);
        return Layout(a, sbkA) == Layout(b, sbkB);
    }

    private static (int Count, int RecordSize) Layout(PcgFile pcg, PcgChunk bank)
    {
        long baseOffset = bank.DataOffset;
        if (baseOffset + 12 > pcg.Data.Length)
            return (-1, -1);
        return (
            (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset, 4)),
            (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset + 4, 4)));
    }
}
