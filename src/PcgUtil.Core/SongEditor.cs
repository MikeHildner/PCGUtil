namespace PcgUtil.Core;

/// <summary>
/// Outcome of retargeting a .SNG: the new bytes (the original array when nothing moved),
/// how many song track references followed a program, and how many pointed at a sound that
/// no longer exists anywhere in the backup.
/// </summary>
public sealed record SongRetargetResult(byte[] Data, int Moved, int Unresolved)
{
    public bool Changed => Moved > 0;
}

/// <summary>
/// Keeps a companion .SNG's songs pointing at the same sounds after a .PCG's programs move.
///
/// A song's tracks reference programs exactly the way a combi's timbres do — number at
/// timbre +0, bank PcgId at +1 — but they live in a different file, so a program reorder
/// inside the backup cannot reach them. Every method here takes the same arguments as the
/// <see cref="PcgEditor"/> call that moved the programs and derives the identical mapping,
/// so the two files stay in step. Callers that skip this get the long-standing limitation:
/// the .PCG is reference-safe and the songs quietly play the wrong patches.
/// </summary>
public static class SongEditor
{
    /// <summary>
    /// Follows every program from where it sat in <paramref name="before"/> to where it sits
    /// in <paramref name="after"/> and rewrites the song references to match.
    ///
    /// Programs are identified by <see cref="PcgSoundKey"/> — their sound content, ignoring
    /// name and favorite — rather than by the operation that moved them, so one call covers
    /// every path a program can move: a swap, a drag, a whole-bank sort or compact, or any
    /// sequence of them. It is also its own inverse: passing the two states the other way
    /// round is exactly what an undo needs, which is why this needs no history of its own.
    ///
    /// A reference is left alone when its program still sits where it was (so duplicates
    /// don't wander) or when its sound no longer exists anywhere — the latter is an
    /// overwrite, where continuing to point at that slot is what the musician asked for.
    /// </summary>
    public static SongRetargetResult RetargetToPcg(PcgFile sng, PcgFile before, PcgFile after)
    {
        ArgumentNullException.ThrowIfNull(sng);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        // What sound sat at each slot before and after, and where each sound can now be found.
        var wasAt = new Dictionary<(int Bank, int Index), string>();
        foreach (var e in PcgSoundKey.Keys(before, "PRG1"))
            wasAt[(e.Bank, e.Index)] = e.Key;

        var isAt = new Dictionary<(int Bank, int Index), string>();
        var nowAt = new Dictionary<string, (int Bank, int Index)>();
        foreach (var e in PcgSoundKey.Keys(after, "PRG1"))
        {
            isAt[(e.Bank, e.Index)] = e.Key;
            if (!nowAt.ContainsKey(e.Key))
                nowAt[e.Key] = (e.Bank, e.Index); // first occurrence wins among duplicates
        }

        int moved = 0, unresolved = 0;
        var data = (byte[])sng.Data.Clone();
        PcgEditor.ForEachTimbreProgramRef(data, SongReader.TimbreSetRegions(sng, data), tOff =>
        {
            var slot = (Bank: PcgCatalog.ProgramBankIndexForPcgId(data[tOff + 1]), Index: (int)data[tOff]);
            if (slot.Bank < 0 || !wasAt.TryGetValue(slot, out var key))
                return;                       // points outside this backup's banks

            // "Hasn't moved" has to be asked of this slot, not of the sound: a patch that also
            // exists elsewhere in the file would otherwise look stationary and strand every
            // reference to the copy that did move.
            if (isAt.TryGetValue(slot, out var stillHere) && stillHere == key)
                return;

            if (!nowAt.TryGetValue(key, out var dst))
            {
                unresolved++;                 // sound is gone: an overwrite, not a move
                return;
            }
            if (dst == slot)
                return;

            data[tOff] = (byte)dst.Index;
            data[tOff + 1] = (byte)PcgCatalog.ProgramBankPcgIdForIndex(dst.Bank);
            moved++;
        });

        if (moved == 0)
            return new SongRetargetResult(sng.Data, 0, unresolved);

        PcgChecksum.Recompute(sng, data);
        return new SongRetargetResult(data, moved, unresolved);
    }

    /// <summary>
    /// Applies the renumbering of <see cref="PcgEditor.ReorderPrograms"/> to a .SNG. Pass the
    /// same bank and <paramref name="newOrder"/>; references into other banks are untouched.
    /// Use when the caller knows the exact operation; <see cref="RetargetToPcg"/> covers the
    /// general case by comparing two states of the backup.
    /// </summary>
    public static byte[] RetargetProgramReorder(PcgFile sng, int bank, IReadOnlyList<int> newOrder)
    {
        ArgumentNullException.ThrowIfNull(sng);
        ArgumentNullException.ThrowIfNull(newOrder);
        var newIndexOfOld = PcgEditor.InverseOf(newOrder, newOrder.Count);

        var data = (byte[])sng.Data.Clone();
        PcgEditor.ForEachTimbreProgramRef(data, SongReader.TimbreSetRegions(sng, data), tOff =>
        {
            if (PcgCatalog.ProgramBankIndexForPcgId(data[tOff + 1]) != bank)
                return;
            int number = data[tOff];
            if (number < newIndexOfOld.Length && newIndexOfOld[number] != number)
                data[tOff] = (byte)newIndexOfOld[number];
        });

        PcgChecksum.Recompute(sng, data);
        return data;
    }

    /// <summary>
    /// Applies the exchange of <see cref="PcgEditor.SwapPrograms"/> to a .SNG. Banks are list
    /// indices, as in the editor; references store a PcgId, so both are mapped.
    /// </summary>
    public static byte[] RetargetProgramSwap(PcgFile sng, int bankA, int indexA, int bankB, int indexB)
    {
        ArgumentNullException.ThrowIfNull(sng);
        int pcgIdA = PcgCatalog.ProgramBankPcgIdForIndex(bankA);
        int pcgIdB = PcgCatalog.ProgramBankPcgIdForIndex(bankB);

        var data = (byte[])sng.Data.Clone();
        PcgEditor.ForEachTimbreProgramRef(data, SongReader.TimbreSetRegions(sng, data), tOff =>
        {
            int mapped = PcgCatalog.ProgramBankIndexForPcgId(data[tOff + 1]);
            int number = data[tOff];
            if (mapped == bankA && number == indexA)
            {
                data[tOff] = (byte)indexB;
                data[tOff + 1] = (byte)pcgIdB;
            }
            else if (mapped == bankB && number == indexB)
            {
                data[tOff] = (byte)indexA;
                data[tOff + 1] = (byte)pcgIdA;
            }
        });

        PcgChecksum.Recompute(sng, data);
        return data;
    }

    /// <summary>
    /// How many song tracks reference a program bank — or one program in it when
    /// <paramref name="index"/> is given. Lets the app say whether an edit will reach the
    /// songs before it makes it, and stay quiet when it won't.
    /// </summary>
    public static int CountReferences(PcgFile sng, int bank, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(sng);
        int hits = 0;
        PcgEditor.ForEachTimbreProgramRef(sng.Data, SongReader.TimbreSetRegions(sng, sng.Data), tOff =>
        {
            if (PcgCatalog.ProgramBankIndexForPcgId(sng.Data[tOff + 1]) != bank)
                return;
            if (index is null || sng.Data[tOff] == index)
                hits++;
        });
        return hits;
    }
}
