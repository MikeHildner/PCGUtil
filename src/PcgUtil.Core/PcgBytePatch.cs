namespace PcgUtil.Core;

/// <summary>
/// A sparse byte-level difference between two equal-length PCG images. Every writer
/// (PcgEditor / PcgOrganizer) returns a full new image of the same length with all leaf
/// checksums already recomputed, so a patch captures the finished bytes verbatim —
/// applying it in either direction needs no re-finalization.
/// </summary>
public sealed class PcgBytePatch
{
    /// <summary>One differing run. OldBytes and NewBytes are always the same length.</summary>
    public sealed record Segment(long Offset, byte[] OldBytes, byte[] NewBytes);

    /// <summary>Mismatch runs separated by fewer than this many equal bytes coalesce into
    /// one segment. PCG edits cluster inside fixed-size records (542-byte set-list slots,
    /// 24-byte names), so one logical edit merges to roughly one segment per touched record,
    /// while untouched neighbors a full record away stay separate.</summary>
    public const int DefaultMergeGap = 64;

    /// <summary>Runs in ascending offset order, non-overlapping.</summary>
    public IReadOnlyList<Segment> Segments { get; }

    /// <summary>Length both images must have; Apply* throws on anything else.</summary>
    public long ImageLength { get; }

    /// <summary>Retained payload: the sum of OldBytes + NewBytes lengths — the unit the
    /// undo history budgets by.</summary>
    public long ByteCost { get; }

    public bool IsEmpty => Segments.Count == 0;

    private PcgBytePatch(IReadOnlyList<Segment> segments, long imageLength)
    {
        Segments = segments;
        ImageLength = imageLength;
        ByteCost = segments.Sum(s => (long)(s.OldBytes.Length + s.NewBytes.Length));
    }

    /// <summary>Diffs two images of equal length in one linear pass.</summary>
    public static PcgBytePatch Compute(byte[] before, byte[] after, int mergeGap = DefaultMergeGap)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Length != after.Length)
            throw new ArgumentException(
                $"Images differ in length ({before.Length} vs {after.Length}); PCG edits never resize the image.");

        // Pass 1: raw mismatch runs [start, end).
        var runs = new List<(int Start, int End)>();
        int i = 0, n = before.Length;
        while (i < n)
        {
            if (before[i] == after[i]) { i++; continue; }
            int start = i;
            while (i < n && before[i] != after[i]) i++;
            runs.Add((start, i));
        }

        // Pass 2: merge runs whose equal gap is below the threshold (the gap bytes are
        // identical on both sides, so round-trip identity is unaffected).
        var segments = new List<Segment>(runs.Count);
        for (int r = 0; r < runs.Count; r++)
        {
            int start = runs[r].Start, end = runs[r].End;
            while (r + 1 < runs.Count && runs[r + 1].Start - end < mergeGap)
                end = runs[++r].End;
            int len = end - start;
            var oldBytes = new byte[len];
            var newBytes = new byte[len];
            Array.Copy(before, start, oldBytes, 0, len);
            Array.Copy(after, start, newBytes, 0, len);
            segments.Add(new Segment(start, oldBytes, newBytes));
        }

        return new PcgBytePatch(segments, n);
    }

    /// <summary>The undo direction: a copy of <paramref name="current"/> with every segment's
    /// OldBytes written. Precondition: current is the image this patch's NewBytes describe.</summary>
    public byte[] ApplyOld(byte[] current) => Apply(current, s => s.OldBytes);

    /// <summary>The redo direction: a copy of <paramref name="current"/> with NewBytes written.</summary>
    public byte[] ApplyNew(byte[] current) => Apply(current, s => s.NewBytes);

    private byte[] Apply(byte[] current, Func<Segment, byte[]> side)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Length != ImageLength)
            throw new ArgumentException($"Image length {current.Length} does not match the patch's {ImageLength}.");
        var result = (byte[])current.Clone();
        foreach (var segment in Segments)
        {
            var payload = side(segment);
            Array.Copy(payload, 0, result, segment.Offset, payload.Length);
        }
        return result;
    }
}
