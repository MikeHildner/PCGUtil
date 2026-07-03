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
/// each section has the same banks with the same record counts and sizes. Cross-file copies
/// land raw bytes at computed offsets, so anything else must be refused.
/// </summary>
public static class PcgCompat
{
    public static PcgCompatResult Compare(PcgFile a, PcgFile b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new PcgCompatResult(
            SectionMatches(a, b, "PRG1"),
            SectionMatches(a, b, "CMB1"),
            SectionMatches(a, b, "SBK1"));
    }

    private static bool SectionMatches(PcgFile a, PcgFile b, string sectionId) =>
        Summarize(a, sectionId).SequenceEqual(Summarize(b, sectionId));

    // (count, recordSize) per bank under the section; empty when the section is absent.
    private static IEnumerable<(int Count, int RecordSize)> Summarize(PcgFile pcg, string sectionId)
    {
        var section = pcg.FindFirst(sectionId);
        if (section is null)
            yield break;

        foreach (var bank in section.Children)
        {
            long baseOffset = bank.DataOffset;
            if (baseOffset + 12 > pcg.Data.Length)
            {
                yield return (-1, -1);
                continue;
            }
            yield return (
                (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset, 4)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(pcg.Data.AsSpan((int)baseOffset + 4, 4)));
        }
    }
}
