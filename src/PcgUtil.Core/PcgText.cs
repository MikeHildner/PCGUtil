using System.Text;

namespace PcgUtil.Core;

/// <summary>Shared helpers for reading fixed-width text fields from PCG data.</summary>
internal static class PcgText
{
    /// <summary>
    /// Reads a fixed-length field as ASCII, stopping at the first NUL and trimming
    /// trailing whitespace. PCG name fields are either NUL-terminated or space-padded.
    /// </summary>
    public static string ReadFixedString(byte[] data, long offset, int maxLen)
    {
        if (offset < 0 || offset >= data.Length)
            return string.Empty;

        int start = (int)offset;
        int end = (int)Math.Min(offset + maxLen, data.Length);
        int len = 0;
        for (int i = start; i < end; i++)
        {
            if (data[i] == 0)
                break;
            len++;
        }
        return Encoding.ASCII.GetString(data, start, len).TrimEnd();
    }
}
