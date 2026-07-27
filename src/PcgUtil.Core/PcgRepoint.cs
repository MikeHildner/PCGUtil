using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// One re-point rule: references to programs <c>FromStart..FromEnd</c> (inclusive) of
/// <c>FromBank</c> move to <c>ToBank</c> starting at <c>ToStart</c>, offsets preserved —
/// a reference to <c>FromStart + k</c> lands on <c>ToStart + k</c>. Banks are canonical
/// list indices (as in <see cref="PcgCatalog.ProgramBanks"/>), not PcgIds.
/// </summary>
public sealed record RepointRule(int FromBank, int FromStart, int FromEnd, int ToBank, int ToStart)
{
    /// <summary>A single program to a single program.</summary>
    public static RepointRule Single(int fromBank, int fromIndex, int toBank, int toIndex) =>
        new(fromBank, fromIndex, fromIndex, toBank, toIndex);

    public int MappedEnd => ToStart + (FromEnd - FromStart);
}

public enum RepointSiteKind { CombiTimbre, SetListSlot, SongTrack }

/// <summary>
/// One reference a rule set would change. For a combi timbre, Outer = (combi bank, combi
/// index) and Inner = timbre; for a set-list slot, OuterIndex = set list and Inner = slot
/// (OuterBank is −1); for a song track, OuterIndex = song and Inner = track.
/// </summary>
public sealed record RepointSite(RepointSiteKind Kind, int OuterBank, int OuterIndex, int Inner,
                                 int FromBank, int FromNumber, int ToBank, int ToNumber);

/// <summary>The dry run: what a rule set would touch, before anything is written.</summary>
public sealed class RepointPlan
{
    /// <summary>Rule problems that block applying (absent bank, range past the bank's end…).</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>Things worth knowing that don't block: a rule matching nothing, a rule
    /// pointing at empty placeholder programs, overlapping rules.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public required int CombiTimbres { get; init; }
    public required int SetListSlots { get; init; }
    public required int SongTracks { get; init; }

    /// <summary>The first <see cref="MaxSites"/> affected references, for preview.</summary>
    public required IReadOnlyList<RepointSite> Sites { get; init; }
    public required bool SitesTruncated { get; init; }

    public int Total => CombiTimbres + SetListSlots + SongTracks;
    public bool IsValid => Errors.Count == 0;

    public const int MaxSites = 200;
}

/// <summary>
/// Bulk re-pointing of program references by rules — "everything that plays U-EE, play
/// U-FF instead". The same three reference graphs the reorg machinery retargets (combi
/// timbres, program-type set-list slots, song tracks in a companion .SNG), driven by
/// user-supplied mappings instead of derived permutations. Only references move; program
/// records themselves stay put, so there is no engine-type constraint and no record copy.
///
/// Semantics: the <em>first</em> matching rule wins, and every reference is matched against
/// its <em>original</em> value — rules never cascade, so <c>[A→B, B→C]</c> sends A-references
/// to B, not to C. Each reference changes at most once.
/// </summary>
public static class PcgRepoint
{
    /// <summary>
    /// Rule problems that block applying, in plain English. Empty means the rules are sound.
    /// </summary>
    public static IReadOnlyList<string> Validate(PcgFile pcg, IReadOnlyList<RepointRule> rules)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        ArgumentNullException.ThrowIfNull(rules);
        var catalog = PcgCatalog.Build(pcg);
        var errors = new List<string>();

        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            string label = $"Rule {i + 1}";
            if (r.FromBank < 0 || r.FromBank >= catalog.ProgramBanks.Count
                || catalog.ProgramBanks[r.FromBank].Count == 0)
            {
                errors.Add($"{label}: this file doesn't carry the from-bank.");
                continue;
            }
            if (r.ToBank < 0 || r.ToBank >= catalog.ProgramBanks.Count
                || catalog.ProgramBanks[r.ToBank].Count == 0)
            {
                errors.Add($"{label}: this file doesn't carry the to-bank.");
                continue;
            }
            if (r.FromEnd < r.FromStart)
            {
                errors.Add($"{label}: the from-range ends before it starts.");
                continue;
            }
            int fromCount = catalog.ProgramBanks[r.FromBank].Count;
            if (r.FromStart < 0 || r.FromEnd >= fromCount)
            {
                errors.Add($"{label}: {PcgBankLabels.Program(r.FromBank)} holds programs 000–{fromCount - 1:D3}.");
                continue;
            }
            int toCount = catalog.ProgramBanks[r.ToBank].Count;
            if (r.ToStart < 0 || r.MappedEnd >= toCount)
            {
                errors.Add($"{label}: the range doesn't fit — it would run to "
                    + $"{PcgBankLabels.Program(r.ToBank)} #{r.MappedEnd:D3}, past the bank's last program #{toCount - 1:D3}.");
            }
        }
        return errors;
    }

    /// <summary>
    /// Computes exactly what the rules would change, without writing anything. Counts are
    /// exact; the site list is capped at <see cref="RepointPlan.MaxSites"/> for display.
    /// Pass the companion .SNG to include song tracks; pass a prebuilt catalog to skip
    /// rebuilding it on every call (the UI previews on each keystroke).
    /// </summary>
    public static RepointPlan Plan(PcgFile pcg, PcgFile? sng, IReadOnlyList<RepointRule> rules,
                                   PcgCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var errors = Validate(pcg, rules);
        if (errors.Count > 0)
            return new RepointPlan
            {
                Errors = errors, Warnings = Array.Empty<string>(),
                CombiTimbres = 0, SetListSlots = 0, SongTracks = 0,
                Sites = Array.Empty<RepointSite>(), SitesTruncated = false,
            };

        catalog ??= PcgCatalog.Build(pcg);
        var sites = new List<RepointSite>();
        int timbres = 0, slots = 0, tracks = 0;
        var hitsPerRule = new int[rules.Count];

        foreach (var (site, _) in TimbreHits(pcg, rules, hitsPerRule))
        {
            timbres++;
            if (sites.Count < RepointPlan.MaxSites) sites.Add(site);
        }
        foreach (var (site, _) in SlotHits(pcg, rules, hitsPerRule))
        {
            slots++;
            if (sites.Count < RepointPlan.MaxSites) sites.Add(site);
        }
        if (sng is not null)
            foreach (var (site, _) in SongHits(sng, rules, hitsPerRule))
            {
                tracks++;
                if (sites.Count < RepointPlan.MaxSites) sites.Add(site);
            }

        var warnings = new List<string>();
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (hitsPerRule[i] == 0)
                warnings.Add($"Rule {i + 1} matches nothing — no reference points at "
                    + $"{PcgBankLabels.Program(r.FromBank)} #{r.FromStart:D3}"
                    + (r.FromEnd > r.FromStart ? $"–{r.FromEnd:D3}" : "") + ".");
            var toNames = catalog.ProgramBanks[r.ToBank];
            int placeholders = Enumerable.Range(r.ToStart, r.FromEnd - r.FromStart + 1)
                .Count(n => n < toNames.Count && PcgOrganizer.IsProgramPlaceholder(toNames[n]));
            if (placeholders > 0)
                warnings.Add($"Rule {i + 1} points at {placeholders} empty program"
                    + $"{(placeholders == 1 ? "" : "s")} in {PcgBankLabels.Program(r.ToBank)} — "
                    + "whatever referenced it would play nothing there.");
            for (int j = 0; j < i; j++)
            {
                var e = rules[j];
                if (e.FromBank == r.FromBank && e.FromStart <= r.FromEnd && r.FromStart <= e.FromEnd)
                {
                    warnings.Add($"Rule {i + 1} overlaps rule {j + 1} — where they overlap, rule {j + 1} wins.");
                    break;
                }
            }
        }

        return new RepointPlan
        {
            Errors = errors, Warnings = warnings,
            CombiTimbres = timbres, SetListSlots = slots, SongTracks = tracks,
            Sites = sites, SitesTruncated = timbres + slots + tracks > sites.Count,
        };
    }

    /// <summary>
    /// Applies the rules to the backup: every matching combi timbre and program-type
    /// set-list slot is re-pointed. Throws when <see cref="Validate"/> finds problems.
    /// </summary>
    public static byte[] Apply(PcgFile pcg, IReadOnlyList<RepointRule> rules)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        var errors = Validate(pcg, rules);
        if (errors.Count > 0)
            throw new InvalidOperationException(errors[0]);

        var data = (byte[])pcg.Data.Clone();
        // Hits are enumerated from the ORIGINAL bytes while writes go to the clone, which
        // is what makes "rules never cascade" true by construction.
        foreach (var (site, offset) in TimbreHits(pcg, rules, ruleHits: null))
        {
            data[offset] = (byte)site.ToNumber;
            data[offset + 1] = (byte)PcgCatalog.ProgramBankPcgIdForIndex(site.ToBank);
        }
        foreach (var (site, refOffset) in SlotHits(pcg, rules, ruleHits: null))
            PcgEditor.WriteSlotReference(data, refOffset,
                PcgCatalog.ProgramBankPcgIdForIndex(site.ToBank), site.ToNumber);

        return PcgEditor.Finalized(pcg, data);
    }

    /// <summary>
    /// Applies the same rules to a companion .SNG's song tracks. The caller pairs this with
    /// <see cref="Apply"/> so both files keep telling the same story.
    /// </summary>
    public static byte[] ApplySng(PcgFile sng, IReadOnlyList<RepointRule> rules)
    {
        ArgumentNullException.ThrowIfNull(sng);
        var data = (byte[])sng.Data.Clone();
        foreach (var (site, offset) in SongHits(sng, rules, ruleHits: null))
        {
            data[offset] = (byte)site.ToNumber;
            data[offset + 1] = (byte)PcgCatalog.ProgramBankPcgIdForIndex(site.ToBank);
        }
        PcgChecksum.Recompute(sng, data);
        return data;
    }

    // First matching rule, matched against the reference's original value.
    private static (int RuleIndex, int ToBank, int ToNumber)? Map(
        IReadOnlyList<RepointRule> rules, int bank, int number)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (bank == r.FromBank && number >= r.FromStart && number <= r.FromEnd)
                return (i, r.ToBank, r.ToStart + (number - r.FromStart));
        }
        return null;
    }

    // Every combi timbre a rule matches. Offset = the timbre's program-number byte.
    private static IEnumerable<(RepointSite Site, long Offset)> TimbreHits(
        PcgFile pcg, IReadOnlyList<RepointRule> rules, int[]? ruleHits)
    {
        var banks = PcgBankIdentity.CanonicalBanks(pcg, "CMB1");
        var data = pcg.Data;
        for (int bank = 0; bank < banks.Count; bank++)
        {
            if (banks[bank] is not { } chunk) continue;
            long baseOffset = chunk.DataOffset;
            if (baseOffset + 12 > data.Length) continue;
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset, 4));
            int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)baseOffset + 4, 4));

            for (int i = 0; i < count; i++)
            {
                long record = baseOffset + 12 + (long)i * recordSize;
                if (record + recordSize > data.Length) break;
                for (int t = 0; t < CombiReader.TimbresPerCombi; t++)
                {
                    long tOff = record + CombiReader.TimbresOffset + (long)t * CombiReader.TimbreStride;
                    if (tOff + 1 >= data.Length) break;
                    int refBank = PcgCatalog.ProgramBankIndexForPcgId(data[tOff + 1]);
                    if (refBank < 0) continue;
                    if (Map(rules, refBank, data[tOff]) is not { } m) continue;
                    if (ruleHits is not null) ruleHits[m.RuleIndex]++;
                    yield return (new RepointSite(RepointSiteKind.CombiTimbre, bank, i, t,
                        refBank, data[tOff], m.ToBank, m.ToNumber), tOff);
                }
            }
        }
    }

    // Every program-type set-list slot a rule matches. Offset = the slot's reference bytes.
    private static IEnumerable<(RepointSite Site, long Offset)> SlotHits(
        PcgFile pcg, IReadOnlyList<RepointRule> rules, int[]? ruleHits)
    {
        if (pcg.FindFirst("SBK1") is null) yield break;
        var layout = PcgEditor.GetLayout(pcg);
        var data = pcg.Data;
        for (int setList = 0; setList < layout.Count; setList++)
        {
            long record = layout.RecordsStart + (long)setList * layout.RecordSize;
            for (int slot = 0; slot < layout.SlotsPerList; slot++)
            {
                long refOffset = record + SetListReader.RecordHeaderSize
                    + (long)slot * SetListReader.SlotSize + SetListReader.SlotRefOffset;
                if (refOffset + 2 >= data.Length) continue;
                if ((data[refOffset] & 0x03) != 1) continue; // Program-type slots only
                int refBank = PcgCatalog.ProgramBankIndexForPcgId(data[refOffset + 1] & 0x1F);
                if (refBank < 0) continue;
                int number = data[refOffset + 2] & 0x7F;
                if (Map(rules, refBank, number) is not { } m) continue;
                if (ruleHits is not null) ruleHits[m.RuleIndex]++;
                yield return (new RepointSite(RepointSiteKind.SetListSlot, -1, setList, slot,
                    refBank, number, m.ToBank, m.ToNumber), refOffset);
            }
        }
    }

    // Every song track a rule matches. Offset = the track timbre's program-number byte.
    private static IEnumerable<(RepointSite Site, long Offset)> SongHits(
        PcgFile sng, IReadOnlyList<RepointRule> rules, int[]? ruleHits)
    {
        var data = sng.Data;
        int song = 0;
        foreach (var region in SongReader.TimbreSetRegions(sng, data))
        {
            for (int i = 0; i < region.Count; i++, song++)
            {
                long record = region.RecordsStart + (long)i * region.RecordSize;
                if (record + region.RecordSize > data.Length) break;
                for (int t = 0; t < CombiReader.TimbresPerCombi; t++)
                {
                    long tOff = record + CombiReader.TimbresOffset + (long)t * CombiReader.TimbreStride;
                    if (tOff + 1 >= data.Length) break;
                    int refBank = PcgCatalog.ProgramBankIndexForPcgId(data[tOff + 1]);
                    if (refBank < 0) continue;
                    if (Map(rules, refBank, data[tOff]) is not { } m) continue;
                    if (ruleHits is not null) ruleHits[m.RuleIndex]++;
                    yield return (new RepointSite(RepointSiteKind.SongTrack, -1, song, t,
                        refBank, data[tOff], m.ToBank, m.ToNumber), tOff);
                }
            }
        }
    }
}
