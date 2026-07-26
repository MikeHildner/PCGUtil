namespace PcgUtil.Core;

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
    /// Applies the renumbering of <see cref="PcgEditor.ReorderPrograms"/> to a .SNG. Pass the
    /// same bank and <paramref name="newOrder"/>; references into other banks are untouched.
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
