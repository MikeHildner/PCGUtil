using System.Text;

namespace PcgUtil.Core;

/// <summary>A printable text run found in the file, with its byte offset.</summary>
public readonly record struct PcgString(long Offset, string Value);

/// <summary>
/// Extracts runs of printable ASCII text from PCG data. Useful for surfacing names
/// (set lists, programs, combis, drum kits) before each record type is fully decoded.
/// </summary>
public static class PcgStrings
{
    public static IEnumerable<PcgString> Extract(byte[] data, long start, long end, int minLength = 3)
    {
        if (minLength < 1) minLength = 1;
        if (start < 0) start = 0;
        end = Math.Min(end, data.Length);

        int runStart = -1;
        for (long i = start; i < end; i++)
        {
            bool printable = data[i] is >= 0x20 and <= 0x7E;
            if (printable)
            {
                if (runStart < 0) runStart = (int)i;
            }
            else if (runStart >= 0)
            {
                if (TryMake(data, runStart, (int)(i - runStart), minLength, out var s))
                    yield return s;
                runStart = -1;
            }
        }

        if (runStart >= 0 && TryMake(data, runStart, (int)(end - runStart), minLength, out var tail))
            yield return tail;
    }

    public static IEnumerable<PcgString> Extract(byte[] data, PcgChunk chunk, int minLength = 3) =>
        Extract(data, chunk.DataOffset, chunk.DataEnd, minLength);

    private static bool TryMake(byte[] data, int start, int length, int minLength, out PcgString result)
    {
        result = default;
        if (length < minLength)
            return false;

        string value = Encoding.ASCII.GetString(data, start, length).Trim();
        if (value.Length < minLength)
            return false;

        result = new PcgString(start, value);
        return true;
    }
}
