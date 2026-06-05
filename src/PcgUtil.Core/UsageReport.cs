namespace PcgUtil.Core;

/// <summary>What kind of place references a Program.</summary>
public enum UsageSiteKind
{
    CombiTimbre,
    SetListSlot,
}

/// <summary>One in-file place that references a Program.</summary>
public sealed record UsageSite(UsageSiteKind Kind, string Description);

/// <summary>A Program and every in-file place that references it.</summary>
public sealed class ProgramUsage
{
    /// <summary>Program-bank list index (matches <see cref="PcgCatalog.ProgramBanks"/>).</summary>
    public required int BankIndex { get; init; }
    public required int Number { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<UsageSite> Sites { get; init; }

    public int ReferenceCount => Sites.Count;
}

/// <summary>A named patch with no in-file references.</summary>
public sealed record UnreferencedPatch(int BankIndex, int Number, string Name);

/// <summary>Cross-reference of how Programs are used in a file, plus unreferenced patches.</summary>
public sealed class UsageReport
{
    public required IReadOnlyList<ProgramUsage> Programs { get; init; }
    public required IReadOnlyList<UnreferencedPatch> UnreferencedPrograms { get; init; }
    public required IReadOnlyList<UnreferencedPatch> UnreferencedCombis { get; init; }
}
