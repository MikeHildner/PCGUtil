namespace PcgUtil.Core;

/// <summary>
/// A single chunk in a PCG file. Each chunk header is 12 bytes on disk:
/// a 4-character ASCII id, a 4-byte big-endian data size, and a 4-byte field
/// whose meaning is type-specific (treated as opaque for now).
/// </summary>
public sealed class PcgChunk
{
    /// <summary>Four-character chunk id, e.g. "PCG1", "SLS1", "PBK1".</summary>
    public required string Id { get; init; }

    /// <summary>Byte offset of this chunk's header (the id) within the file.</summary>
    public long Offset { get; init; }

    /// <summary>Size in bytes of this chunk's data (excludes the 12-byte header).</summary>
    public uint Size { get; init; }

    /// <summary>Third header word; meaning is type-specific and not yet decoded.</summary>
    public uint Field { get; init; }

    /// <summary>Byte offset where this chunk's data begins (<see cref="Offset"/> + 12).</summary>
    public long DataOffset { get; init; }

    /// <summary>Byte offset just past this chunk's data.</summary>
    public long DataEnd => DataOffset + Size;

    /// <summary>Nested chunks, for container chunks such as PCG1 and SLS1.</summary>
    public List<PcgChunk> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    public override string ToString() => $"{Id} ({Size} bytes) @0x{Offset:X}";
}
