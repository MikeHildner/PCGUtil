namespace PcgUtil.Web;

/// <summary>
/// Server-side session-restore cache: one entry per browser (localStorage "pcgBrowserId").
/// Home pushes the current session after every load and edit; when a circuit dies (eviction,
/// page reload, pause/resume) the fresh circuit finds the entry and offers a restore.
///
/// Entries hold <em>references</em> to the same byte[] the live circuit holds — PcgReader
/// wraps its input array and PcgEditor always clones before writing, so nothing ever mutates
/// these bytes in place. While the circuit lives the cache duplicates nothing; after the
/// circuit dies the cache keeps the only reference.
///
/// Memory-only by design: the UI promises nothing is ever written to disk.
/// </summary>
public sealed class UploadCache
{
    public sealed record Entry(
        string FileName,
        byte[] Bytes,
        bool Dirty,
        string? KgeFileName,
        IReadOnlyList<IReadOnlyList<string>>? KgeBanks,
        DateTimeOffset SavedAt);

    // 32-bit app pool: cap the cache's worst-case footprint. A typical session is ~47 MB;
    // 150 MB tolerates a few concurrent users without threatening the ~2 GB address space
    // (the CircuitOptions comment in Program.cs has the full budget).
    private const long MaxTotalBytes = 150L * 1024 * 1024;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(25);

    // One lock covers entries and the byte total together — cap eviction is check-then-act,
    // which lock-free structures can't keep consistent. Ops are rare (per load/edit, not per
    // keystroke) and O(entries), where entries ≈ concurrent browsers.
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private long _totalBytes;

    /// <summary>Stores (or replaces) the session for a browser. Keeps references; copies nothing.</summary>
    public void Store(string browserId, string fileName, byte[] bytes, bool dirty,
                      string? kgeFileName, IReadOnlyList<IReadOnlyList<string>>? kgeBanks)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            EvictExpired(now);
            RemoveLocked(browserId);

            if (bytes.LongLength > MaxTotalBytes)
                return; // larger than the whole cache: this session just isn't restorable

            while (_totalBytes + bytes.LongLength > MaxTotalBytes && _entries.Count > 0)
                RemoveLocked(_entries.MinBy(kv => kv.Value.SavedAt).Key); // oldest-first

            _entries[browserId] = new Entry(fileName, bytes, dirty, kgeFileName, kgeBanks, now);
            _totalBytes += bytes.LongLength;
        }
    }

    /// <summary>The live entry for a browser, or null. Lazily evicts anything expired.</summary>
    public Entry? TryGet(string browserId)
    {
        lock (_gate)
        {
            EvictExpired(DateTimeOffset.UtcNow);
            return _entries.GetValueOrDefault(browserId);
        }
    }

    private void EvictExpired(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(kv => now - kv.Value.SavedAt > Ttl)
                                    .Select(kv => kv.Key).ToList())
            RemoveLocked(key);
    }

    private void RemoveLocked(string browserId)
    {
        if (_entries.Remove(browserId, out var entry))
            _totalBytes -= entry.Bytes.LongLength;
    }
}
