namespace PcgUtil.Core;

/// <summary>One documented section of a record with its decoded settings.</summary>
public sealed record RecordSection(string Title, IReadOnlyList<ParamValue> Values);

/// <summary>
/// Reads a program's synthesis parameters — the filter cutoffs, envelope stages, LFO rates,
/// oscillator zones and engine-specific controls that decide what a sound actually does.
///
/// Unlike an effect slot's settings, these sit at plain record offsets: the documentation's
/// program tables address the record directly (Category at 2568 and the EXi engine ids at
/// 2857/3909, both long since hardware-verified, appear there exactly where every other
/// reader in this project already finds them). Only the effect areas are packed, and those
/// belong to <see cref="EffectParams"/>.
///
/// An HD-1 program is one 3706-byte record. An EXi program is 4960 bytes: a common part
/// shaped like the HD-1's, plus two payload regions holding whichever engine each slot
/// runs — a CX-3's drawbars, an AL-1's filters — read against that engine's own table.
/// </summary>
public static class RecordParams
{
    /// <summary>Where each EXi slot's engine payload begins inside an EXi program record.</summary>
    public static readonly int[] ExiPayloadOffsets = { 2908, 3960 };

    /// <summary>The engine id byte for each EXi slot ("Algorithm Type").</summary>
    private static readonly int[] ExiEngineOffsets = { 2857, 3909 };

    /// <summary>
    /// Every documented parameter of one program, grouped into the sections the
    /// documentation (and the instrument's own edit pages) use. Effect slots and Tone
    /// Adjust are left out — the effect chips and the ✎ readout already own those.
    /// </summary>
    public static IReadOnlyList<RecordSection> ReadProgram(PcgFile pcg, int bank, int index)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var (record, recordSize) = PcgEditor.LocateProgram(pcg, bank, index);
        bool exi = PcgBankIdentity.ProgramBankType(pcg, bank) != ProgramBankType.Hd1;

        var sections = new List<RecordSection>();
        foreach (var table in ParamTables.ProgramSections(exi))
        {
            if (Excluded(table.Name)) continue;
            var values = Decode(pcg.Data, record, recordSize, table);
            if (values.Count > 0) sections.Add(new RecordSection(table.Name, values));
        }

        if (exi)
            for (int slot = 0; slot < ExiPayloadOffsets.Length; slot++)
            {
                long engineAt = record + ExiEngineOffsets[slot];
                if (engineAt >= pcg.Data.Length || ExiEngineOffsets[slot] >= recordSize) continue;
                int engineId = pcg.Data[engineAt];
                if (engineId == 0) continue;                  // slot off
                string engine = ExiEngines.Name(engineId);
                long payload = record + ExiPayloadOffsets[slot];
                int room = recordSize - ExiPayloadOffsets[slot];
                foreach (var table in ParamTables.EngineSections(engine))
                {
                    var values = Decode(pcg.Data, payload, room, table);
                    if (values.Count > 0)
                        sections.Add(new RecordSection($"EXi{slot + 1} {engine}: {table.Name}", values));
                }
            }

        // KARMA's two sections are more than half of a program's documented fields and the
        // least useful answer to "what does this sound like", so they sort to the end;
        // everything else keeps the documentation's order.
        return sections.OrderBy(s => s.Title.StartsWith("KARMA", StringComparison.Ordinal) ? 1 : 0)
                       .ToList();
    }

    /// <summary>The effect areas and Tone Adjust, which other readers present in full.</summary>
    private static bool Excluded(string section) =>
        section.StartsWith("Insert Effect", StringComparison.Ordinal)
        || section.StartsWith("Master Effect", StringComparison.Ordinal)
        || section.StartsWith("Total Effect", StringComparison.Ordinal)
        || section == "Tone Adjust";

    private static List<ParamValue> Decode(byte[] data, long baseOffset, int room, ParamTable table)
    {
        var values = new List<ParamValue>(table.Fields.Count);
        foreach (var field in table.Fields)
        {
            if (!Fits(field, data, baseOffset, room)) continue;
            long raw = field.Read(data, baseOffset);
            values.Add(new ParamValue(field, raw, Format(field, raw)));
        }
        return values;
    }

    private static bool Fits(ParamField field, byte[] data, long baseOffset, int room)
    {
        foreach (var span in field.Spans)
            if (span.Byte >= room || baseOffset + span.Byte >= data.Length)
                return false;
        return true;
    }

    /// <summary>
    /// Display text. Fields the documentation writes as "00(fixed)" are shown as the number
    /// actually stored, not as the constant: its own notes warn that modulation-source rows
    /// written that way really carry a model-specific list, so the stored byte is the honest
    /// reading. Effect-slot conventions (Wet/Dry ratios, the effect modulation-source names)
    /// deliberately do not apply here — a program's sources are a different list.
    /// </summary>
    private static string Format(ParamField field, long raw) =>
        field.Fixed is not null ? raw.ToString() : ParamFormat.Number(field, raw);
}
