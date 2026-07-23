using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PcgUtil.Core;

/// <summary>One patch slot, by bank list index and number, with its display name.</summary>
public sealed record DuplicateMember(int Bank, int Index, string Name);

/// <summary>
/// A set of patches whose <em>sound data</em> is identical — record bytes compared with the
/// name field zeroed and the favorite bit masked, so a renamed or starred copy still counts.
/// </summary>
public sealed class SoundDuplicateGroup
{
    /// <summary>Members in bank, then slot order.</summary>
    public required IReadOnlyList<DuplicateMember> Members { get; init; }

    /// <summary>Distinct names across the members, in first-seen order.</summary>
    public required IReadOnlyList<string> Names { get; init; }

    /// <summary>True when every member's record bytes are identical including name and
    /// favorite — a fully redundant copy.</summary>
    public required bool AllExact { get; init; }

    /// <summary>True when the members differ <em>only</em> by the favorite bit — same sound,
    /// same name, one of them starred.</summary>
    public required bool FavoriteOnly { get; init; }

    public string Label => string.Join(" / ", Names);

    public int Count => Members.Count;
}

/// <summary>Patches that share a name but hold different sounds — easy to mix up.</summary>
public sealed class NameCollisionGroup
{
    public required string Name { get; init; }
    public required IReadOnlyList<DuplicateMember> Members { get; init; }

    /// <summary>How many distinct sounds hide behind the shared name (always ≥ 2).</summary>
    public required int DistinctSounds { get; init; }

    public int Count => Members.Count;
}

/// <summary>Everything the duplicate scan learned about one section (programs or combis).</summary>
public sealed class DuplicateReport
{
    /// <summary>Groups of ≥ 2 slots holding the same sound, largest first.</summary>
    public required IReadOnlyList<SoundDuplicateGroup> SoundDuplicates { get; init; }

    /// <summary>Names shared by ≥ 2 different sounds.</summary>
    public required IReadOnlyList<NameCollisionGroup> NameCollisions { get; init; }

    /// <summary>Slots holding factory-init sound data under a real name — they look like
    /// sounds but are empty shells. Bank, then slot order.</summary>
    public required IReadOnlyList<DuplicateMember> RenamedInits { get; init; }

    /// <summary>How many init/empty-named placeholder slots the scan skipped.</summary>
    public required int InitPlaceholderCount { get; init; }
}

/// <summary>
/// Finds duplicate patches in a PCG by content: programs (PRG1) or combis (CMB1) whose record
/// bytes match once the 24-byte name field and the favorite bit are masked out. Read-only.
/// Init placeholders are recognized the same way — any record whose masked bytes match a
/// name-flagged init/empty record is init content, even if it has been renamed. Detection is
/// informational only: the write paths (compact, free-slot search, deep-copy guards) keep
/// their conservative name-based predicates, so a renamed init is surfaced, never overwritten.
/// </summary>
public static class PcgDuplicates
{
    private const int SubHeaderSize = 12;
    private const int NameLength = 24;

    public static DuplicateReport Programs(PcgFile pcg) =>
        Find(pcg, "PRG1", PcgOrganizer.IsProgramPlaceholder,
             ProgramReader.FavoriteOffset, ProgramReader.FavoriteBit);

    public static DuplicateReport Combis(PcgFile pcg) =>
        Find(pcg, "CMB1", Combi.IsEmptyOrInitName,
             CombiReader.FavoriteOffset, CombiReader.FavoriteBit);

    private readonly record struct Entry(
        int Bank, int Index, string Name, long Offset, int Size, string Key, bool IsInitNamed);

    private static DuplicateReport Find(
        PcgFile pcg, string sectionId, Func<string, bool> isInitName, int favOffset, int favBit)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var banks = PcgBankIdentity.CanonicalBanks(pcg, sectionId);
        var data = pcg.Data;
        var entries = new List<Entry>();

        for (int b = 0; b < banks.Count; b++)
        {
            if (banks[b] is not { } chunk)
                continue; // bank not carried by this file
            long baseOffset = chunk.DataOffset;
            if (baseOffset + SubHeaderSize > data.Length)
                continue;
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
            int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));
            if (count <= 0 || recordSize <= 0)
                continue;
            long recordsStart = baseOffset + SubHeaderSize;
            byte[] masked = new byte[recordSize];

            for (int i = 0; i < count; i++)
            {
                long record = recordsStart + (long)i * recordSize;
                if (record + recordSize > data.Length)
                    break;
                string name = PcgText.ReadFixedString(data, record, NameLength);
                string key = PcgSoundKey.KeyOf(data, record, recordSize, chunk.Id, favOffset, favBit, masked);
                entries.Add(new Entry(b, i, name, record, recordSize, key, isInitName(name)));
            }
        }

        var initKeys = entries.Where(e => e.IsInitNamed).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var renamedInits = entries
            .Where(e => !e.IsInitNamed && initKeys.Contains(e.Key))
            .OrderBy(e => e.Bank).ThenBy(e => e.Index)
            .Select(e => new DuplicateMember(e.Bank, e.Index, e.Name))
            .ToList();
        var real = entries.Where(e => !e.IsInitNamed && !initKeys.Contains(e.Key)).ToList();

        var soundGroups = new List<SoundDuplicateGroup>();
        foreach (var group in real.GroupBy(e => e.Key, StringComparer.Ordinal))
        {
            var members = group.OrderBy(e => e.Bank).ThenBy(e => e.Index).ToList();
            if (members.Count < 2)
                continue;
            bool allExact = AllEqual(data, members, maskFavorite: false, favOffset, favBit);
            soundGroups.Add(new SoundDuplicateGroup
            {
                Members = members.Select(e => new DuplicateMember(e.Bank, e.Index, e.Name)).ToList(),
                Names = members.Select(e => e.Name).Distinct(StringComparer.Ordinal).ToList(),
                AllExact = allExact,
                // "Only the star differs" must be verified with the name bytes still in play —
                // inferring it from equal trimmed names would mislabel padding differences.
                FavoriteOnly = !allExact && AllEqual(data, members, maskFavorite: true, favOffset, favBit),
            });
        }

        var collisions = new List<NameCollisionGroup>();
        foreach (var group in real.GroupBy(e => e.Name, StringComparer.Ordinal))
        {
            int distinctSounds = group.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count();
            if (distinctSounds < 2)
                continue;
            collisions.Add(new NameCollisionGroup
            {
                Name = group.Key,
                Members = group.OrderBy(e => e.Bank).ThenBy(e => e.Index)
                    .Select(e => new DuplicateMember(e.Bank, e.Index, e.Name)).ToList(),
                DistinctSounds = distinctSounds,
            });
        }

        return new DuplicateReport
        {
            SoundDuplicates = soundGroups
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Label, StringComparer.Ordinal)
                .ToList(),
            NameCollisions = collisions
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Name, StringComparer.Ordinal)
                .ToList(),
            RenamedInits = renamedInits,
            InitPlaceholderCount = entries.Count(e => e.IsInitNamed),
        };
    }

    /// <summary>Byte-compares the members' full records, optionally treating the favorite bit
    /// as equal. Sizes always match within a group (the hash covers the whole record).</summary>
    private static bool AllEqual(byte[] data, List<Entry> members, bool maskFavorite, int favOffset, int favBit)
    {
        var first = members[0];
        var firstSpan = data.AsSpan((int)first.Offset, first.Size);
        for (int k = 1; k < members.Count; k++)
        {
            var m = members[k];
            if (m.Size != first.Size)
                return false;
            var span = data.AsSpan((int)m.Offset, m.Size);
            for (int j = 0; j < first.Size; j++)
            {
                byte a = firstSpan[j], c = span[j];
                if (maskFavorite && j == favOffset)
                {
                    a &= (byte)~favBit;
                    c &= (byte)~favBit;
                }
                if (a != c)
                    return false;
            }
        }
        return true;
    }
}
