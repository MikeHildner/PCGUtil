using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PcgUtil.Core;

/// <summary>How a record differs between the two compared files.</summary>
public enum DiffKind
{
    Added,     // real content where the before-file had a placeholder
    Removed,   // before-file content gone (placeholder there now, or overwritten by a move-in)
    Moved,     // byte-identical record found at a different index (reorg/sort/compact)
    Renamed,   // only the 24-byte name field differs
    Edited,    // same name, other bytes differ (parameter/reference change)
    Replaced,  // a different patch lives at this index now
}

/// <summary>One difference. For <see cref="DiffKind.Moved"/> the indices differ; otherwise they match.
/// For set-list slots, <paramref name="Bank"/> is the set-list index.</summary>
public sealed record DiffEntry(DiffKind Kind, int Bank, int IndexBefore, int IndexAfter,
                               string NameBefore, string NameAfter, string? Detail);

/// <summary>Everything that differs between two files' patches and set lists.</summary>
public sealed class PcgDiffReport
{
    public required IReadOnlyList<DiffEntry> Programs { get; init; }
    public required IReadOnlyList<DiffEntry> Combis { get; init; }
    public required IReadOnlyList<DiffEntry> SetListSlots { get; init; }
    public required IReadOnlyList<DiffEntry> RenamedSetLists { get; init; }

    public bool IsEmpty =>
        Programs.Count == 0 && Combis.Count == 0 && SetListSlots.Count == 0 && RenamedSetLists.Count == 0;
}

/// <summary>
/// Byte-level comparison of two same-model files: which programs, combis, and set-list slots
/// changed, and how. Identical records at the same index are skipped; a record whose exact bytes
/// reappear at another index within the same bank is reported as <see cref="DiffKind.Moved"/>
/// (this is what makes reorgs/sorts legible); the rest classify by name vs body. Read-only.
/// </summary>
public static class PcgDiff
{
    private const int SubHeaderSize = 12;
    private const int NameLength = 24;

    /// <summary>Compares <paramref name="before"/> (e.g. an older backup) to <paramref name="after"/>.</summary>
    public static PcgDiffReport Compare(PcgFile before, PcgFile after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var compat = PcgCompat.Compare(before, after);
        if (!compat.AllMatch)
            throw new InvalidOperationException(
                $"Record layouts differ ({compat.MismatchSummary}) — the files don't appear to be from the same model.");

        var (slotEntries, renamedLists) = DiffSetLists(before, after);
        return new PcgDiffReport
        {
            Programs = DiffBanks(before, after, "PRG1", PcgOrganizer.IsProgramPlaceholder),
            Combis = DiffBanks(before, after, "CMB1", Combi.IsEmptyOrInitName),
            SetListSlots = slotEntries,
            RenamedSetLists = renamedLists,
        };
    }

    // ----- Program / combi banks -----

    private static List<DiffEntry> DiffBanks(PcgFile before, PcgFile after, string sectionId,
                                             Func<string, bool> isPlaceholder)
    {
        var entries = new List<DiffEntry>();
        var banksA = PcgBankIdentity.CanonicalBanks(before, sectionId);
        var banksB = PcgBankIdentity.CanonicalBanks(after, sectionId);

        // Compare like with like: banks pair by canonical index, and a bank only one file
        // carries has no counterpart to diff against.
        int banks = Math.Min(banksA.Count, banksB.Count);
        for (int bank = 0; bank < banks; bank++)
        {
            if (banksA[bank] is not { } chunkA || banksB[bank] is not { } chunkB)
                continue;
            long baseA = chunkA.DataOffset;
            long baseB = chunkB.DataOffset;
            if (baseA + SubHeaderSize > before.Data.Length || baseB + SubHeaderSize > after.Data.Length)
                continue;
            int count = ReadBe(before.Data, baseA);
            int recordSize = ReadBe(before.Data, baseA + 4);
            if (count <= 0 || recordSize <= 0)
                continue;

            DiffFixedRecords(entries, bank,
                before.Data, baseA + SubHeaderSize,
                after.Data, baseB + SubHeaderSize,
                count, recordSize, isPlaceholder, detailFor: null);
        }
        Sort(entries);
        return entries;
    }

    // ----- Set lists (slots + list names) -----

    private static (List<DiffEntry> Slots, List<DiffEntry> RenamedLists) DiffSetLists(PcgFile before, PcgFile after)
    {
        var slots = new List<DiffEntry>();
        var renamed = new List<DiffEntry>();
        var sbkA = before.FindFirst("SBK1");
        var sbkB = after.FindFirst("SBK1");
        if (sbkA is null || sbkB is null)
            return (slots, renamed);

        int count = ReadBe(before.Data, sbkA.DataOffset);
        int recordSize = ReadBe(before.Data, sbkA.DataOffset + 4);
        if (count <= 0 || recordSize <= 0)
            return (slots, renamed);
        int slotsPerList = (recordSize - SetListReader.RecordHeaderSize) / SetListReader.SlotSize;
        long recsA = sbkA.DataOffset + SetListReader.SubHeaderSize;
        long recsB = sbkB.DataOffset + SetListReader.SubHeaderSize;

        // Slot references resolve against each file's own catalog for the Edited/Replaced detail.
        var listsA = SetListReader.Read(before);
        var listsB = SetListReader.Read(after);
        var catA = PcgCatalog.Build(before);
        var catB = PcgCatalog.Build(after);

        for (int sl = 0; sl < count; sl++)
        {
            long recordA = recsA + (long)sl * recordSize;
            long recordB = recsB + (long)sl * recordSize;

            string listNameA = PcgText.ReadFixedString(before.Data, recordA, SetListReader.SetListNameLength);
            string listNameB = PcgText.ReadFixedString(after.Data, recordB, SetListReader.SetListNameLength);
            if (!string.Equals(listNameA, listNameB, StringComparison.Ordinal))
                renamed.Add(new DiffEntry(DiffKind.Renamed, sl, sl, sl, listNameA, listNameB, null));

            int listIndex = sl; // captured by the detail callback
            DiffFixedRecords(slots, sl,
                before.Data, recordA + SetListReader.RecordHeaderSize,
                after.Data, recordB + SetListReader.RecordHeaderSize,
                slotsPerList, SetListReader.SlotSize,
                isPlaceholder: static _ => false, // slot 0 is meaningfully unnamed — never skip slots
                detailFor: (kind, iBefore, iAfter) =>
                    SlotDetail(kind, listsA[listIndex].Slots[iBefore], listsB[listIndex].Slots[iAfter], catA, catB));
        }
        Sort(slots);
        return (slots, renamed);
    }

    // What an edited/replaced slot loads now vs before, when the reference itself changed.
    private static string? SlotDetail(DiffKind kind, SetListSlot before, SetListSlot after,
                                      PcgCatalog catBefore, PcgCatalog catAfter)
    {
        if (kind is not (DiffKind.Edited or DiffKind.Replaced))
            return null;
        var a = before.Reference;
        var b = after.Reference;
        if (a.Kind == b.Kind && a.Bank == b.Bank && a.Index == b.Index)
            return null;
        return $"loads {Describe(b, catAfter)} (was {Describe(a, catBefore)})";
    }

    private static string Describe(SetListReference reference, PcgCatalog catalog)
    {
        string label = reference.Kind switch
        {
            PcgItemKind.Program => PcgBankLabels.Program(PcgCatalog.ProgramBankIndexForPcgId(reference.Bank)),
            PcgItemKind.Combi => PcgBankLabels.Combi(reference.Bank),
            _ => "Song",
        };
        var name = catalog.Resolve(reference);
        return $"{reference.Kind} {label} #{reference.Index:D3}{(name is null ? "" : $" '{name}'")}";
    }

    // ----- Shared fixed-record differ -----

    // Compares count records of recordSize bytes on each side. Records whose bytes match at the
    // same index are skipped; byte-identical records found at another index pair as Moved; the
    // rest classify from names and placeholder state. Names sit in the first 24 bytes.
    private static void DiffFixedRecords(List<DiffEntry> entries, int bank,
        byte[] dataBefore, long startBefore, byte[] dataAfter, long startAfter,
        int count, int recordSize, Func<string, bool> isPlaceholder,
        Func<DiffKind, int, int, string?>? detailFor)
    {
        long endBefore = startBefore + (long)count * recordSize;
        long endAfter = startAfter + (long)count * recordSize;
        if (endBefore > dataBefore.Length || endAfter > dataAfter.Length)
            return;

        var differing = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (!RecordAt(dataBefore, startBefore, recordSize, i)
                    .SequenceEqual(RecordAt(dataAfter, startAfter, recordSize, i)))
                differing.Add(i);
        }
        if (differing.Count == 0)
            return;

        string NameBefore(int i) => PcgText.ReadFixedString(dataBefore, startBefore + (long)i * recordSize, NameLength);
        string NameAfter(int i) => PcgText.ReadFixedString(dataAfter, startAfter + (long)i * recordSize, NameLength);

        // Both sides factory placeholders → init-noise, ignore.
        var candidates = differing
            .Where(i => !(isPlaceholder(NameBefore(i)) && isPlaceholder(NameAfter(i))))
            .ToList();

        // Move pairing: exact-content matches across differing indices (per bank).
        var beforeByHash = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        foreach (var i in candidates)
        {
            if (isPlaceholder(NameBefore(i)))
                continue;
            var hash = Hash(dataBefore, startBefore, recordSize, i);
            if (!beforeByHash.TryGetValue(hash, out var queue))
                beforeByHash[hash] = queue = new Queue<int>();
            queue.Enqueue(i);
        }

        var movedBefore = new HashSet<int>();
        var movedAfter = new HashSet<int>();
        foreach (var j in candidates)
        {
            if (isPlaceholder(NameAfter(j)))
                continue;
            var hash = Hash(dataAfter, startAfter, recordSize, j);
            if (beforeByHash.TryGetValue(hash, out var queue) && queue.Count > 0)
            {
                int i = queue.Dequeue();
                entries.Add(new DiffEntry(DiffKind.Moved, bank, i, j, NameBefore(i), NameAfter(j),
                    detailFor?.Invoke(DiffKind.Moved, i, j)));
                movedBefore.Add(i);
                movedAfter.Add(j);
            }
        }

        // Leftovers: each side of an index needs explaining unless it's a placeholder or a move
        // already accounts for it.
        foreach (var i in candidates)
        {
            string nameA = NameBefore(i);
            string nameB = NameAfter(i);
            bool explainBefore = !isPlaceholder(nameA) && !movedBefore.Contains(i);
            bool explainAfter = !isPlaceholder(nameB) && !movedAfter.Contains(i);
            if (!explainBefore && !explainAfter)
                continue;

            DiffKind kind;
            if (explainBefore && explainAfter)
            {
                bool bodyEqual = RecordAt(dataBefore, startBefore, recordSize, i)[NameLength..]
                    .SequenceEqual(RecordAt(dataAfter, startAfter, recordSize, i)[NameLength..]);
                kind = bodyEqual ? DiffKind.Renamed
                    : string.Equals(nameA, nameB, StringComparison.Ordinal) ? DiffKind.Edited
                    : DiffKind.Replaced;
            }
            else
            {
                kind = explainAfter ? DiffKind.Added : DiffKind.Removed;
            }
            entries.Add(new DiffEntry(kind, bank, i, i, nameA, nameB, detailFor?.Invoke(kind, i, i)));
        }
    }

    private static ReadOnlySpan<byte> RecordAt(byte[] data, long start, int recordSize, int index) =>
        data.AsSpan((int)(start + (long)index * recordSize), recordSize);

    private static string Hash(byte[] data, long start, int recordSize, int index) =>
        Convert.ToHexString(SHA256.HashData(RecordAt(data, start, recordSize, index)));

    private static int ReadBe(byte[] data, long offset) =>
        (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset, 4));

    private static void Sort(List<DiffEntry> entries) =>
        entries.Sort((x, y) => x.Bank != y.Bank ? x.Bank.CompareTo(y.Bank)
            : x.IndexAfter != y.IndexAfter ? x.IndexAfter.CompareTo(y.IndexAfter)
            : x.Kind.CompareTo(y.Kind));
}
