namespace PcgUtil.Core;

/// <summary>How a Combi timbre plays its program (bits 5–7 of the timbre's third byte).</summary>
public enum TimbreStatus
{
    Off = 0,
    Int = 1,
    Both = 2,
    Ext = 3,
    Ex2 = 4,
}

/// <summary>A decoded Combi and its timbres.</summary>
public sealed class Combi
{
    /// <summary>Combi bank list index (matches <see cref="PcgCatalog.CombiBanks"/>).</summary>
    public required int Bank { get; init; }
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<CombiTimbre> Timbres { get; init; }

    public bool IsEmpty => Name.Length == 0;

    /// <summary>
    /// Empty, or a factory "Init Combi" placeholder. Such combis' timbres all default to the
    /// first program, so they're excluded from usage analysis (matches how PCG Tools treats them).
    /// </summary>
    public bool IsEmptyOrInit =>
        Name.Length == 0 ||
        (Name.Contains("Init", StringComparison.OrdinalIgnoreCase) &&
         Name.Contains("Combi", StringComparison.OrdinalIgnoreCase));
}

/// <summary>One timbre of a <see cref="Combi"/> and the Program it references.</summary>
public sealed class CombiTimbre
{
    public required int Index { get; init; } // 0..15
    public required TimbreStatus Status { get; init; }

    /// <summary>Raw program-bank PcgId; resolve a name via <see cref="PcgCatalog.ResolveProgram"/>.</summary>
    public required int ProgramBankPcgId { get; init; }
    public required int ProgramNumber { get; init; }

    /// <summary>True when the timbre actually plays an internal program (Int or Both).</summary>
    public bool UsesInternalProgram => Status is TimbreStatus.Int or TimbreStatus.Both;
}
