namespace PcgUtil.Core;

/// <summary>
/// Each <em>leaf</em> chunk stores an 8-bit additive checksum of its data — the sum of its data
/// bytes, modulo 256 — in the low byte of its third header word (read big-endian, so that byte is
/// the one immediately before the data, at <c>DataOffset - 1</c>). Container chunks and a few
/// header chunks carry a type flag there instead, not a checksum.
///
/// The hardware refuses to load a file ("File unavailable") when a leaf's checksum no longer
/// matches its data, so any edit that changes byte <em>values</em> must recompute the checksums of
/// the chunks it touched. (A pure swap of equal-size blocks leaves the sum unchanged, which is why
/// swaps loaded before this was understood.)
/// </summary>
public static class PcgChecksum
{
    /// <summary>
    /// Recomputes the data checksum of every checksummed leaf chunk in <paramref name="edited"/>.
    /// A leaf is treated as checksummed only when its stored byte already matched its data in
    /// <paramref name="original"/> (which came from the hardware and is therefore valid), so
    /// flag-bearing chunks are left untouched. Unchanged chunks recompute to the same value, so
    /// this is safe to call after any edit.
    /// </summary>
    public static void Recompute(PcgFile original, byte[] edited)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edited);

        foreach (var chunk in original.EnumerateChunks())
        {
            if (chunk.HasChildren) continue;                       // containers hold a flag here
            if (chunk.DataOffset < 1 || chunk.Size <= 0) continue;
            if (chunk.DataEnd > original.Data.Length || chunk.DataEnd > edited.Length) continue;

            byte stored = (byte)(chunk.Field & 0xFF);
            if (stored != Sum(original.Data, chunk.DataOffset, chunk.Size))
                continue; // this leaf doesn't use the low-byte checksum convention

            edited[chunk.DataOffset - 1] = Sum(edited, chunk.DataOffset, chunk.Size);
        }
    }

    /// <summary>Sum of <paramref name="size"/> bytes starting at <paramref name="start"/>, modulo 256.</summary>
    public static byte Sum(byte[] data, long start, long size)
    {
        byte sum = 0;
        long end = start + size;
        for (long i = start; i < end; i++)
            sum += data[i];
        return sum;
    }
}
