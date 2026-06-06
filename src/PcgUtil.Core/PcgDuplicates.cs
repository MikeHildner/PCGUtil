using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>One member of a <see cref="DuplicateGroup"/>, by bank list index and number.</summary>
public sealed record DuplicateMember(int Bank, int Index);

/// <summary>A set of programs (or combis) that share a name.</summary>
public sealed class DuplicateGroup
{
    public required string Name { get; init; }
    public required IReadOnlyList<DuplicateMember> Members { get; init; }

    /// <summary>True when every member's record bytes are identical — a true redundant copy,
    /// as opposed to same name but a different sound.</summary>
    public required bool AllByteIdentical { get; init; }

    public int Count => Members.Count;
}

/// <summary>
/// Finds duplicate patches in a PCG: programs (PRG1) or combis (CMB1) that share a name.
/// Read-only. Each group also reports whether its members are byte-identical records.
/// </summary>
public static class PcgDuplicates
{
    private const int SubHeaderSize = 12;
    private const int NameLength = 24;

    public static IReadOnlyList<DuplicateGroup> Programs(PcgFile pcg) => Find(pcg, "PRG1");

    public static IReadOnlyList<DuplicateGroup> Combis(PcgFile pcg) => Find(pcg, "CMB1");

    private static IReadOnlyList<DuplicateGroup> Find(PcgFile pcg, string sectionId)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var section = pcg.FindFirst(sectionId);
        if (section is null)
            return Array.Empty<DuplicateGroup>();

        var data = pcg.Data;
        var byName = new Dictionary<string, List<(int Bank, int Index, long Offset, int Size)>>(StringComparer.Ordinal);

        for (int b = 0; b < section.Children.Count; b++)
        {
            long baseOffset = section.Children[b].DataOffset;
            if (baseOffset + SubHeaderSize > data.Length)
                continue;
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
            int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));
            if (count <= 0 || recordSize <= 0)
                continue;
            long recordsStart = baseOffset + SubHeaderSize;

            for (int i = 0; i < count; i++)
            {
                long record = recordsStart + (long)i * recordSize;
                if (record + recordSize > data.Length)
                    break;
                string name = PcgText.ReadFixedString(data, record, NameLength);
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!byName.TryGetValue(name, out var list))
                    byName[name] = list = new List<(int, int, long, int)>();
                list.Add((b, i, record, recordSize));
            }
        }

        var groups = new List<DuplicateGroup>();
        foreach (var (name, members) in byName)
        {
            if (members.Count < 2)
                continue;
            groups.Add(new DuplicateGroup
            {
                Name = name,
                Members = members.Select(m => new DuplicateMember(m.Bank, m.Index)).ToList(),
                AllByteIdentical = AllIdentical(data, members),
            });
        }

        return groups
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool AllIdentical(byte[] data, List<(int Bank, int Index, long Offset, int Size)> members)
    {
        var first = members[0];
        var firstSpan = data.AsSpan((int)first.Offset, first.Size);
        for (int k = 1; k < members.Count; k++)
        {
            var m = members[k];
            if (m.Size != first.Size)
                return false;
            if (!firstSpan.SequenceEqual(data.AsSpan((int)m.Offset, m.Size)))
                return false;
        }
        return true;
    }
}
