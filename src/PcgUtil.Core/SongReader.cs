using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Reads sequencer songs from a companion .SNG file.
///
/// A .SNG uses the same KORG chunk container as a .PCG behind a longer file header (see
/// <see cref="PcgReader.FindFirstChunkOffset"/>), so <see cref="PcgReader.Parse"/> handles
/// it unchanged. Inside, the tree observed on hardware runs
/// <c>SNG1 → SDK1</c> (the song directory, one 64-byte record per song, name at +0) and
/// <c>SNG1 → SGS1 → SDT1 → { SPR1, BMT1, BMT2, TRK1… }</c>, where <c>SPR1</c> holds the
/// 5264-byte control block and <c>BMT1</c> the 7810-byte timbre set — the same record size a
/// combi uses, which is what makes the timbres decode and retarget with the combi machinery.
/// </summary>
public static class SongReader
{
    /// <summary>Chunk holding songs' timbre sets, one record per song.</summary>
    public const string TimbreSetChunkId = "BMT1";

    /// <summary>Chunk holding the song directory: one fixed record per song, name at +0.</summary>
    public const string DirectoryChunkId = "SDK1";

    /// <summary>Song names are the same fixed width as program and combi names.</summary>
    public const int NameLength = 24;

    private const int SubHeaderSize = 12;

    /// <summary>True when this file looks like a .SNG rather than a .PCG.</summary>
    public static bool IsSongFile(PcgFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.FindFirst(TimbreSetChunkId) is not null;
    }

    /// <summary>
    /// Decodes every song in the file. Names come from the directory chunk and timbres from
    /// the timbre-set chunks; a song with no matching directory entry still appears, so a
    /// file whose two counts disagree is read rather than rejected.
    /// </summary>
    public static IReadOnlyList<Song> Read(PcgFile sng)
    {
        ArgumentNullException.ThrowIfNull(sng);
        var names = ReadNames(sng);

        var songs = new List<Song>();
        foreach (var region in TimbreSetRegions(sng, sng.Data))
        {
            for (int i = 0; i < region.Count; i++)
            {
                long record = region.RecordsStart + (long)i * region.RecordSize;
                if (record + region.RecordSize > sng.Data.Length)
                    break;
                int index = songs.Count;
                songs.Add(new Song
                {
                    Index = index,
                    Name = index < names.Count ? names[index] : string.Empty,
                    Timbres = CombiReader.ReadTimbres(sng.Data, record, region.RecordSize),
                });
            }
        }
        return songs;
    }

    /// <summary>Song names from the directory chunk, in file order.</summary>
    public static IReadOnlyList<string> ReadNames(PcgFile sng)
    {
        ArgumentNullException.ThrowIfNull(sng);
        var names = new List<string>();
        foreach (var chunk in sng.EnumerateChunks())
        {
            if (chunk.Id != DirectoryChunkId || chunk.HasChildren || chunk.Size < SubHeaderSize)
                continue;
            long baseOffset = chunk.DataOffset;
            if (baseOffset + SubHeaderSize > sng.Data.Length)
                continue;

            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(sng.Data.AsSpan((int)baseOffset, 4));
            int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(sng.Data.AsSpan((int)baseOffset + 4, 4));
            if (count <= 0 || recordSize < NameLength)
                continue;

            long records = baseOffset + SubHeaderSize;
            for (int i = 0; i < count; i++)
            {
                long record = records + (long)i * recordSize;
                if (record + recordSize > sng.Data.Length)
                    break;
                names.Add(PcgText.ReadFixedString(sng.Data, record, NameLength));
            }
        }
        return names;
    }

    /// <summary>
    /// The song timbre sets as regions the shared retargeting walk understands. Taking the
    /// byte array separately lets a caller retarget an edited copy without re-parsing.
    /// </summary>
    internal static IEnumerable<PcgEditor.TimbreSetRegion> TimbreSetRegions(PcgFile sng, byte[] data)
    {
        foreach (var chunk in sng.EnumerateChunks())
        {
            if (chunk.Id != TimbreSetChunkId || chunk.HasChildren || chunk.Size < SubHeaderSize)
                continue;
            long baseOffset = chunk.DataOffset;
            if (baseOffset + SubHeaderSize > data.Length)
                continue;

            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
            int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));

            // Guard against an empty companion chunk: BMT2 sits beside BMT1 with a zero count
            // and a nonsense record size, and must not be walked.
            if (count <= 0 || recordSize < CombiReader.TimbresOffset
                           + CombiReader.TimbresPerCombi * CombiReader.TimbreStride)
                continue;
            if ((long)count * recordSize + SubHeaderSize > chunk.Size)
                continue;

            yield return new PcgEditor.TimbreSetRegion(baseOffset + SubHeaderSize, recordSize, count);
        }
    }
}
