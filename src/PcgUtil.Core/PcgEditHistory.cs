namespace PcgUtil.Core;

/// <summary>
/// Bounded undo/redo history over PCG byte images, held as sparse patches
/// (<see cref="PcgBytePatch"/>) rather than full ~47 MB snapshots. The caller owns the
/// current image and passes it in; Undo/Redo return a fresh image to reparse. When the
/// retained payload exceeds the budget the oldest entries are dropped and
/// <see cref="Trimmed"/> latches true — the newest entry is always kept, so one level of
/// undo never disappears.
/// </summary>
public sealed class PcgEditHistory
{
    /// <summary>Default retained-payload budget. Surgical edits patch to ~KB and whole-bank
    /// sorts to a few MB, so this is effectively unlimited depth in real sessions while
    /// staying small next to the host's 32-bit address space.</summary>
    public const long DefaultMaxByteCost = 64L * 1024 * 1024;

    private readonly long _maxByteCost;
    private readonly List<(PcgBytePatch Patch, string Label, DateTimeOffset At)> _undo = new();
    private readonly List<(PcgBytePatch Patch, string Label, DateTimeOffset At)> _redo = new();

    public PcgEditHistory(long maxByteCost = DefaultMaxByteCost) => _maxByteCost = maxByteCost;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Label of the edit Undo would revert (null when nothing to undo).</summary>
    public string? UndoLabel => CanUndo ? _undo[^1].Label : null;

    /// <summary>Label of the edit Redo would re-apply (null when nothing to redo).</summary>
    public string? RedoLabel => CanRedo ? _redo[^1].Label : null;

    /// <summary>Edits between the current image and the oldest retained state.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>True once eviction has dropped entries: undo can no longer reach the
    /// original baseline. Reset only by <see cref="Clear"/>.</summary>
    public bool Trimmed { get; private set; }

    /// <summary>Retained payload across both stacks, in bytes.</summary>
    public long ByteCost => _undo.Sum(e => e.Patch.ByteCost) + _redo.Sum(e => e.Patch.ByteCost);

    /// <summary>Records an edit: diffs before→after, pushes the patch with its label,
    /// clears the redo stack, and evicts oldest entries over budget. Returns false —
    /// recording nothing — when the images are byte-identical.</summary>
    public bool Push(byte[] before, byte[] after, string label)
    {
        var patch = PcgBytePatch.Compute(before, after);
        if (patch.IsEmpty)
            return false;

        _redo.Clear();
        _undo.Add((patch, label, DateTimeOffset.Now));

        while (ByteCost > _maxByteCost && _undo.Count > 1)
        {
            _undo.RemoveAt(0);
            Trimmed = true;
        }
        return true;
    }

    /// <summary>Reverts the newest edit: returns the previous image as a new array and
    /// moves the entry to the redo stack.</summary>
    public byte[] Undo(byte[] current)
    {
        if (!CanUndo)
            throw new InvalidOperationException("Nothing to undo.");
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        return entry.Patch.ApplyOld(current);
    }

    /// <summary>Re-applies the most recently undone edit.</summary>
    public byte[] Redo(byte[] current)
    {
        if (!CanRedo)
            throw new InvalidOperationException("Nothing to redo.");
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        return entry.Patch.ApplyNew(current);
    }

    /// <summary>Empties both stacks and resets <see cref="Trimmed"/> (a new file was
    /// loaded or restored).</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Trimmed = false;
    }
}
