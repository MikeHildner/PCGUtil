using System.Buffers.Binary;
using System.Text;

namespace PcgUtil.Core;

/// <summary>Parses the chunk structure of a PCG file.</summary>
public static class PcgReader
{
    /// <summary>Bytes before the first chunk: a 4-byte signature plus a 12-byte file header.</summary>
    public const int FileHeaderSize = 16;

    /// <summary>Per-chunk header: id(4) + big-endian size(4) + field(4).</summary>
    public const int ChunkHeaderSize = 12;

    private const int MaxDepth = 8;

    public static bool LooksLikePcg(ReadOnlySpan<byte> data) =>
        data.Length >= 4 &&
        data[0] == (byte)'K' && data[1] == (byte)'O' &&
        data[2] == (byte)'R' && data[3] == (byte)'G';

    public static PcgFile Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < FileHeaderSize)
            throw new InvalidDataException("File is too small to be a PCG file.");
        if (!LooksLikePcg(data))
            throw new InvalidDataException(
                $"This does not look like a PCG file (signature '{SafeAscii(data, 0, 4)}').");

        var topLevel = new List<PcgChunk>();
        ParseChunks(data, FileHeaderSize, data.Length, depth: 0, into: topLevel);

        return new PcgFile
        {
            Data = data,
            Magic = Encoding.ASCII.GetString(data, 0, 4),
            TopLevel = topLevel,
        };
    }

    private static void ParseChunks(byte[] data, long start, long end, int depth, List<PcgChunk> into)
    {
        long offset = start;
        while (offset + ChunkHeaderSize <= end)
        {
            if (!IsChunkId(data, offset))
                break;

            int o = (int)offset;
            string id = Encoding.ASCII.GetString(data, o, 4);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 4, 4));
            uint field = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 8, 4));
            long dataOffset = offset + ChunkHeaderSize;

            // A chunk whose data would run past the parent region isn't a real chunk.
            if (dataOffset + size > end)
                break;

            var chunk = new PcgChunk
            {
                Id = id,
                Offset = offset,
                Size = size,
                Field = field,
                DataOffset = dataOffset,
            };

            // Container chunks (PCG1, SLS1, PRG1, ...) begin with a nested chunk id;
            // leaf chunks (banks, global) begin with binary data, so we stop there.
            if (depth < MaxDepth && size >= ChunkHeaderSize && IsChunkId(data, dataOffset))
                ParseChunks(data, dataOffset, dataOffset + size, depth + 1, chunk.Children);

            into.Add(chunk);
            offset = dataOffset + size;
        }
    }

    // Observed chunk ids are four characters drawn from A-Z and 0-9 (e.g. "PCG1").
    private static bool IsChunkId(byte[] data, long offset)
    {
        if (offset + 4 > data.Length)
            return false;
        for (int i = 0; i < 4; i++)
        {
            byte b = data[offset + i];
            bool ok = (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'0' && b <= (byte)'9');
            if (!ok)
                return false;
        }
        return true;
    }

    private static string SafeAscii(byte[] data, int offset, int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length && offset + i < data.Length; i++)
        {
            byte b = data[offset + i];
            sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
        }
        return sb.ToString();
    }
}
