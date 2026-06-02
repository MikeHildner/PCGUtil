using System.Linq;

namespace PcgUtil.Core;

/// <summary>A parsed PCG file: the raw bytes plus the decoded chunk tree.</summary>
public sealed class PcgFile
{
    public required byte[] Data { get; init; }

    /// <summary>The 4-byte signature found at the start of the file.</summary>
    public required string Magic { get; init; }

    /// <summary>Top-level chunks (typically a single "PCG1" container).</summary>
    public required IReadOnlyList<PcgChunk> TopLevel { get; init; }

    public long Length => Data.LongLength;

    /// <summary>Depth-first walk over every chunk in the tree.</summary>
    public IEnumerable<PcgChunk> EnumerateChunks()
    {
        foreach (var (chunk, _) in Flatten())
            yield return chunk;
    }

    /// <summary>Depth-first walk that also reports each chunk's depth (0 = top level).</summary>
    public IEnumerable<(PcgChunk Chunk, int Depth)> Flatten()
    {
        foreach (var top in TopLevel)
            foreach (var item in Walk(top, 0))
                yield return item;

        static IEnumerable<(PcgChunk, int)> Walk(PcgChunk chunk, int depth)
        {
            yield return (chunk, depth);
            foreach (var child in chunk.Children)
                foreach (var item in Walk(child, depth + 1))
                    yield return item;
        }
    }

    /// <summary>First chunk anywhere in the tree with the given id, or null.</summary>
    public PcgChunk? FindFirst(string id) =>
        EnumerateChunks().FirstOrDefault(c => c.Id == id);
}
