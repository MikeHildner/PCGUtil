namespace PcgUtil.Core;

/// <summary>One insert effect's move: source IFX slot → destination IFX slot (both 0-based).</summary>
public sealed record FxCopyPlacement(int SourceIfx, int DestinationIfx);

/// <summary>
/// The dry run of a program→combi effect copy. Computed before anything is written so the
/// UI can show exactly what would happen — and refuse cleanly when it can't.
/// </summary>
public sealed class FxCopyPlan
{
    /// <summary>Every insert effect that travels, in slot order.</summary>
    public required IReadOnlyList<FxCopyPlacement> Placements { get; init; }

    /// <summary>Insert slots the program needs (real effects plus chain-interior slots).</summary>
    public required int NeededIfx { get; init; }

    /// <summary>The combi's structurally free insert slots (0-based): no effect loaded and
    /// not inside any chain.</summary>
    public required IReadOnlyList<int> FreeIfx { get; init; }

    /// <summary>True when every needed insert effect has a destination slot. The copy is
    /// all-or-nothing: half a chain is worse than none.</summary>
    public required bool Fits { get; init; }

    /// <summary>The program routes through a drum kit's own bus settings (byte 2565 bit 7);
    /// the timbre gets the same flag and its kit patch table is pointed at the copied slots.</summary>
    public required bool UsesDrumKitRouting { get; init; }
}

/// <summary>
/// Brings a program's effects along when the program is placed on a combi timbre — the
/// offline equivalent of the instrument's own <em>Copy From Program</em> command in its
/// "IFX-All used" mode (Parameter Guide p.515): only the insert effects the program actually
/// uses are copied, they are <em>packed into vacant slots</em> so the combi's existing
/// effects are untouched, and the timbre's routing is re-pointed to match.
///
/// This is possible as a plain block move because the effect region is byte-identical
/// between program and combi records — IFX1–12 as 74-byte slots at 88+74k, masters at
/// 976/1044/1116/1184 (vendor dump; verified over ~24k program slots with zero violations).
/// The only bytes that change in a copied slot are its chain link (slot numbers are
/// absolute, so packed chains are renumbered) — everything else, every parameter, travels
/// verbatim.
///
/// Master (MFX) and total (TFX) effects are shared by the whole combi, so copying them
/// always changes how every other timbre sounds. They are therefore opt-in flags, exactly
/// like the checkboxes in the instrument's dialog, and default to off.
/// </summary>
public static class ProgramFxCopy
{
    // ----- Record-layout facts beyond the shared effect region -----

    // The program's routing byte packs bus select (bits 0-4), FX control bus (bits 5-6) and
    // the drum-kit-routing flag (bit 7) — the identical packing the timbre uses at +29.
    private const int ProgramRoutingOffset = 2565;

    // OSC1/EXi1 output sends: the engines store them in different places.
    private const int Hd1SendOffset = 3196;  // Send1; Send2 follows
    private const int ExiSendOffset = 2864;

    // Timbre-relative routing fields (timbre base = record + 4802 + 188·t).
    private const int TimbreSendOffset = 15;      // Send1 @ +15, Send2 @ +16
    private const int TimbreDkitPatchOffset = 17; // 12 bus values, one per source IFX
    private const int TimbreRoutingOffset = 29;   // same packing as program byte 2565

    // Bus-select values 1..12 mean IFX1..12 (0 = L/R, 25 = Off — the 26-value enum).
    private const int BusIfxFirst = 1;
    private const int BusIfxLast = 12;
    private const int BusLR = 0;

    // Master regions, from the vendor dump's combi table: MFX spans both slots plus the
    // shared returns/chain bytes (976..1115); TFX spans 1116..1189 (TFX1's full block,
    // TFX2's header, master volume). TFX2's deep parameter bytes are not contiguously
    // mapped in the dump — hardware section 19 probes whether they travel.
    private const int MfxRegionStart = 976;
    private const int MfxRegionEnd = 1116;   // exclusive
    private const int TfxRegionStart = 1116;
    private const int TfxRegionEnd = 1190;   // exclusive

    /// <summary>
    /// Computes what copying <paramref name="programBank"/>/<paramref name="programIndex"/>'s
    /// effects into the combi would do, without writing anything.
    /// </summary>
    public static FxCopyPlan Plan(PcgFile pcg, int programBank, int programIndex,
                                  int combiBank, int combiIndex)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (progOffset, progSize) = PcgEditor.LocateProgram(pcg, programBank, programIndex);
        var (combiOffset, combiSize) = PcgEditor.LocateCombi(pcg, combiBank, combiIndex);

        var used = UsedIfxSlots(pcg.Data, progOffset, progSize);
        var free = FreeIfxSlots(pcg.Data, combiOffset, combiSize);

        var placements = new List<FxCopyPlacement>(used.Count);
        for (int i = 0; i < used.Count && i < free.Count; i++)
            placements.Add(new FxCopyPlacement(used[i], free[i]));

        return new FxCopyPlan
        {
            Placements = used.Count <= free.Count ? placements : Array.Empty<FxCopyPlacement>(),
            NeededIfx = used.Count,
            FreeIfx = free,
            Fits = used.Count <= free.Count,
            UsesDrumKitRouting = progSize > ProgramRoutingOffset
                && (pcg.Data[progOffset + ProgramRoutingOffset] & 0x80) != 0,
        };
    }

    /// <summary>
    /// Points the timbre at the program and packs the program's used insert effects into the
    /// combi's free slots, renumbering chain links and re-pointing the timbre's routing so
    /// the layer sounds as the program did in Program mode. All-or-nothing: throws when the
    /// plan doesn't fit. Masters (MFX) and totals (TFX) copy only when explicitly requested —
    /// they are shared by the whole combi.
    /// </summary>
    public static byte[] Apply(PcgFile pcg, int programBank, int programIndex,
                               int combiBank, int combiIndex, int timbre,
                               bool includeMfx = false, bool includeTfx = false)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        if (timbre is < 0 or >= CombiReader.TimbresPerCombi)
            throw new ArgumentOutOfRangeException(nameof(timbre), timbre, "Timbres are 0–15.");

        var plan = Plan(pcg, programBank, programIndex, combiBank, combiIndex);
        if (!plan.Fits)
            throw new InvalidOperationException(
                $"The program needs {plan.NeededIfx} insert slot(s) but the combi has only "
                + $"{plan.FreeIfx.Count} free — free some, or copy the program without effects.");

        var (progOffset, progSize) = PcgEditor.LocateProgram(pcg, programBank, programIndex);
        var (combiOffset, _) = PcgEditor.LocateCombi(pcg, combiBank, combiIndex);
        bool isExi = PcgBankIdentity.ProgramBankType(pcg, programBank) == ProgramBankType.Exi;

        var data = (byte[])pcg.Data.Clone();

        // 1. The timbre plays the program (same bytes SetTimbreProgram writes).
        long tOff = combiOffset + CombiReader.TimbresOffset + (long)timbre * CombiReader.TimbreStride;
        data[tOff] = (byte)programIndex;
        data[tOff + 1] = (byte)PcgCatalog.ProgramBankPcgIdForIndex(programBank);

        // 2. Pack the used insert effects into the free slots, verbatim blocks first.
        var newSlotOfOld = plan.Placements.ToDictionary(p => p.SourceIfx, p => p.DestinationIfx);
        foreach (var p in plan.Placements)
        {
            Array.Copy(data, progOffset + CombiReader.IfxBase + (long)p.SourceIfx * CombiReader.IfxStride,
                       data, combiOffset + CombiReader.IfxBase + (long)p.DestinationIfx * CombiReader.IfxStride,
                       CombiReader.IfxStride);
        }
        // Then renumber chain links — Chain To stores absolute 1-based slot numbers.
        foreach (var p in plan.Placements)
        {
            long slot = combiOffset + CombiReader.IfxBase + (long)p.DestinationIfx * CombiReader.IfxStride;
            if ((data[slot + 1] & CombiReader.FxChainBit) == 0)
                continue;
            int target = data[slot + CombiReader.FxChainToOffset] & 0x0F;
            if (target >= 1 && newSlotOfOld.TryGetValue(target - 1, out int newTarget))
                data[slot + CombiReader.FxChainToOffset] =
                    (byte)((data[slot + CombiReader.FxChainToOffset] & 0xF0) | (newTarget + 1));
        }

        // 3. Routing: the timbre inherits the program's routing byte with the bus remapped to
        // wherever its insert effect landed. A bus that pointed at an IFX the program doesn't
        // actually use has nowhere to go — L/R, as the instrument does for dangling routes.
        if (progSize > ProgramRoutingOffset)
        {
            byte routing = data[progOffset + ProgramRoutingOffset];
            int bus = routing & 0x1F;
            if (bus is >= BusIfxFirst and <= BusIfxLast)
                bus = newSlotOfOld.TryGetValue(bus - 1, out int newBus) ? newBus + 1 : BusLR;
            data[tOff + TimbreRoutingOffset] = (byte)((routing & 0xE0) | (bus & 0x1F));

            // Drum-kit routing: the kit's own per-instrument buses name source IFX numbers,
            // and the timbre's patch table is exactly the mechanism to redirect them to the
            // packed slots. Entries for uncopied slots stay identity.
            if ((routing & 0x80) != 0)
            {
                for (int k = 0; k < CombiReader.IfxCount; k++)
                    data[tOff + TimbreDkitPatchOffset + k] = (byte)(
                        newSlotOfOld.TryGetValue(k, out int moved) ? moved + 1 : k + 1);
            }
        }
        int sendOffset = isExi ? ExiSendOffset : Hd1SendOffset;
        if (progSize > sendOffset + 1)
        {
            data[tOff + TimbreSendOffset] = data[progOffset + sendOffset];
            data[tOff + TimbreSendOffset + 1] = data[progOffset + sendOffset + 1];
        }

        // 4. Shared master/total effects, only on request.
        if (includeMfx)
            Array.Copy(data, progOffset + MfxRegionStart,
                       data, combiOffset + MfxRegionStart, MfxRegionEnd - MfxRegionStart);
        if (includeTfx)
            Array.Copy(data, progOffset + TfxRegionStart,
                       data, combiOffset + TfxRegionStart, TfxRegionEnd - TfxRegionStart);

        return PcgEditor.Finalized(pcg, data);
    }

    /// <summary>
    /// Frees one of a combi's insert slots by writing the empty-slot pattern over it — the
    /// escape hatch when a copy doesn't fit. Guarded structurally: the slot must hold no
    /// chain link, sit inside no chain, and be fed by no timbre's bus or drum-kit patch,
    /// so clearing it cannot change how anything else sounds.
    /// </summary>
    public static byte[] ClearInsertEffect(PcgFile pcg, int combiBank, int combiIndex, int ifxSlot)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        if (ifxSlot is < 0 or >= 12)
            throw new ArgumentOutOfRangeException(nameof(ifxSlot), ifxSlot, "Insert slots are 0–11.");

        var (combiOffset, combiSize) = PcgEditor.LocateCombi(pcg, combiBank, combiIndex);
        var effects = CombiReader.ReadEffects(pcg.Data, combiOffset, combiSize);

        if (effects[ifxSlot].ChainOn)
            throw new InvalidOperationException(
                $"IFX{ifxSlot + 1} chains into IFX{effects[ifxSlot].ChainTo} — clearing it would break the chain.");
        foreach (var e in effects.Take(CombiReader.IfxCount))
        {
            if (e.ChainOn && e.ChainTo >= 1
                && ifxSlot >= (int)e.Slot && ifxSlot <= e.ChainTo - 1)
                throw new InvalidOperationException(
                    $"IFX{ifxSlot + 1} sits inside the IFX{(int)e.Slot + 1}→IFX{e.ChainTo} chain.");
        }
        for (int t = 0; t < CombiReader.TimbresPerCombi; t++)
        {
            long tOff = combiOffset + CombiReader.TimbresOffset + (long)t * CombiReader.TimbreStride;
            if ((pcg.Data[tOff + TimbreRoutingOffset] & 0x1F) == ifxSlot + BusIfxFirst)
                throw new InvalidOperationException($"Timbre {t + 1} plays through IFX{ifxSlot + 1}.");
            if ((pcg.Data[tOff + TimbreRoutingOffset] & 0x80) != 0)
                for (int k = 0; k < CombiReader.IfxCount; k++)
                    if ((pcg.Data[tOff + TimbreDkitPatchOffset + k] & 0x1F) == ifxSlot + BusIfxFirst)
                        throw new InvalidOperationException(
                            $"Timbre {t + 1}'s drum kit routes through IFX{ifxSlot + 1}.");
        }

        var data = (byte[])pcg.Data.Clone();
        long slotOffset = combiOffset + CombiReader.IfxBase + (long)ifxSlot * CombiReader.IfxStride;
        WriteEmptySlotPattern(pcg, data, slotOffset);
        return PcgEditor.Finalized(pcg, data);
    }

    // The empty pattern is taken from a real empty slot in the same file — the instrument's
    // own idea of "no effect here" — rather than synthesized. Fallback: zeros with the
    // observed 0x10 flag, matching the factory statistics.
    private static void WriteEmptySlotPattern(PcgFile pcg, byte[] data, long slotOffset)
    {
        foreach (var chunk in pcg.EnumerateChunks())
        {
            if (chunk.Id != "CBK1" || chunk.HasChildren || chunk.Size < 12)
                continue;
            long baseOffset = chunk.DataOffset;
            int count = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                pcg.Data.AsSpan((int)baseOffset, 4));
            int recordSize = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                pcg.Data.AsSpan((int)baseOffset + 4, 4));
            for (int i = 0; i < count; i++)
            {
                long record = baseOffset + 12 + (long)i * recordSize;
                if (record + recordSize > pcg.Data.Length)
                    break;
                for (int k = 0; k < CombiReader.IfxCount; k++)
                {
                    long src = record + CombiReader.IfxBase + (long)k * CombiReader.IfxStride;
                    if (pcg.Data[src] != 0 || src == slotOffset)
                        continue; // holds an effect, or is the very slot being cleared
                    Array.Copy(pcg.Data, src, data, slotOffset, CombiReader.IfxStride);
                    return;
                }
            }
        }
        // No empty slot anywhere (implausible in practice): synthesize the observed pattern.
        Array.Clear(data, (int)slotOffset, CombiReader.IfxStride);
        data[slotOffset + 1] = 0x10;
    }

    // A program's "used" insert slots: every slot holding a real effect, plus the empty
    // slots a chain passes over — the manual is explicit that those travel too, so chains
    // arrive structurally intact.
    private static IReadOnlyList<int> UsedIfxSlots(byte[] data, long record, int recordSize)
    {
        var effects = CombiReader.ReadEffects(data, record, recordSize);
        var used = new bool[CombiReader.IfxCount];
        for (int k = 0; k < CombiReader.IfxCount && k < effects.Count; k++)
        {
            if (effects[k].HasEffect)
                used[k] = true;
            if (effects[k].ChainOn && effects[k].ChainTo >= k + 2 && effects[k].ChainTo <= 12)
                for (int j = k; j < effects[k].ChainTo; j++)
                    used[j] = true;
        }
        return Enumerable.Range(0, used.Length).Where(k => used[k]).ToList();
    }

    // A combi's structurally free insert slots: empty and not inside any chain.
    private static IReadOnlyList<int> FreeIfxSlots(byte[] data, long record, int recordSize)
    {
        var occupied = UsedIfxSlots(data, record, recordSize).ToHashSet();
        return Enumerable.Range(0, CombiReader.IfxCount).Where(k => !occupied.Contains(k)).ToList();
    }
}
