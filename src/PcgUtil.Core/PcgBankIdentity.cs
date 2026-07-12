using System.Buffers.Binary;

namespace PcgUtil.Core;

/// <summary>
/// Identifies which hardware bank each bank chunk holds. A bank chunk's data begins with a
/// 12-byte sub-header — record count, record size, then a big-endian <em>bank-identity word</em>
/// at offset +8. Encoding (verified against a full dump and a vendor pack that ships only
/// U-EE/U-FF/U-E banks): <c>0x20000 | n</c> = USER bank n; small integers = INT banks in order;
/// the INT-F <em>program</em> bank is the special value <c>0x8000</c>.
///
/// Bank lists throughout the library are keyed by <em>canonical</em> list index — the full-dump
/// bank order that <see cref="PcgBankLabels"/> and reference resolution assume. Full dumps store
/// banks in canonical order, so position equals canonical index there; partial files (vendor
/// packs) ship an arbitrary subset, so their chunks must be placed by identity. When any
/// identity word is unrecognized or duplicated, the whole section falls back to positional
/// order — the behavior before identity words were understood.
/// </summary>
public static class PcgBankIdentity
{
    private const int BankIdOffset = 8;
    private const uint UserFlag = 0x20000;
    private const uint ProgramIntF = 0x8000;

    /// <summary>Number of canonical bank slots for a section (0 for unmapped sections).</summary>
    public static int CanonicalBankCount(string sectionId) => sectionId switch
    {
        "PRG1" => 20, // INT-A..INT-F, USER-A..USER-G, USER-AA..USER-GG
        "CMB1" => 14, // INT-A..INT-G, USER-A..USER-G
        "DKT1" => 15, // INT, USER-A..USER-G, USER-AA..USER-GG
        "WSQ1" => 15,
        _ => 0,
    };

    /// <summary>Canonical list index for a bank-identity word, or -1 when unrecognized.</summary>
    public static int CanonicalIndex(string sectionId, uint identity) => sectionId switch
    {
        // Programs: INT-A..E are 0..4; INT-F is the outlier 0x8000 (bank type is carried by the
        // chunk id MBK1/PBK1 instead, so this is not an EXi flag); USER-A..GG are 0x20000+0..13.
        "PRG1" => identity switch
        {
            <= 4 => (int)identity,
            ProgramIntF => 5,
            >= UserFlag and <= UserFlag + 13 => 6 + (int)(identity - UserFlag),
            _ => -1,
        },
        "CMB1" => identity switch
        {
            <= 6 => (int)identity,
            >= UserFlag and <= UserFlag + 6 => 7 + (int)(identity - UserFlag),
            _ => -1,
        },
        "DKT1" or "WSQ1" => identity switch
        {
            0 => 0,
            >= UserFlag and <= UserFlag + 13 => 1 + (int)(identity - UserFlag),
            _ => -1,
        },
        _ => -1,
    };

    /// <summary>
    /// The section's bank chunks keyed by canonical list index (null = that bank is absent from
    /// the file). Falls back to positional order when identities can't be trusted; an absent
    /// section yields an empty list.
    /// </summary>
    public static IReadOnlyList<PcgChunk?> CanonicalBanks(PcgFile pcg, string sectionId)
    {
        ArgumentNullException.ThrowIfNull(pcg);
        return CanonicalBanks(pcg.Data, pcg.FindFirst(sectionId), sectionId);
    }

    internal static IReadOnlyList<PcgChunk?> CanonicalBanks(byte[] data, PcgChunk? section, string sectionId)
    {
        if (section is null)
            return Array.Empty<PcgChunk?>();

        var children = section.Children;
        var slots = new PcgChunk?[CanonicalBankCount(sectionId)];
        foreach (var bank in children)
        {
            int index = bank.DataOffset + BankIdOffset + 4 <= data.Length
                ? CanonicalIndex(sectionId, BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan((int)(bank.DataOffset + BankIdOffset), 4)))
                : -1;
            if (index < 0 || index >= slots.Length || slots[index] is not null)
                return Positional(children); // unrecognized or duplicate identity — don't guess
            slots[index] = bank;
        }
        return slots;
    }

    private static IReadOnlyList<PcgChunk?> Positional(IReadOnlyList<PcgChunk> children)
    {
        var slots = new PcgChunk?[children.Count];
        for (int i = 0; i < children.Count; i++)
            slots[i] = children[i];
        return slots;
    }
}
