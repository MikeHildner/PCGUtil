namespace PcgUtil.Core;

/// <summary>What a Set List slot loads. Values match the hardware's 2-bit type field (<c>B0 &amp; 0x03</c>).</summary>
public enum PcgItemKind
{
    Combi = 0,
    Program = 1,
    Song = 2,
}

/// <summary>A decoded Set List (one of 128) and its slots.</summary>
public sealed class SetList
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<SetListSlot> Slots { get; init; }

    /// <summary>Slots that have a non-empty name.</summary>
    public IEnumerable<SetListSlot> NamedSlots => Slots.Where(s => !s.IsEmpty);

    public string DisplayName => string.IsNullOrEmpty(Name) ? $"Set List {Index:D3}" : Name;
}

/// <summary>One slot within a <see cref="SetList"/>.</summary>
public sealed class SetListSlot
{
    public required int Index { get; init; }
    public required string Name { get; init; }

    /// <summary>The Program or Combi this slot loads, decoded from the slot reference.</summary>
    public required SetListReference Reference { get; init; }

    public bool IsEmpty => Name.Length == 0;
}

/// <summary>
/// A decoded slot reference. <c>B0 &amp; 0x03</c> is the type (Combi=0, Program=1, Song=2),
/// <c>B1 &amp; 0x1F</c> is the bank, and <c>B2 &amp; 0x7F</c> is the item number within the bank.
/// (Raw bytes 3–5 are the slot's color/volume/transpose, not part of the reference.)
/// </summary>
public sealed class SetListReference
{
    public required PcgItemKind Kind { get; init; }
    public required int Bank { get; init; }
    public required int Index { get; init; }
    public required IReadOnlyList<byte> Raw { get; init; }

    public string Hex => string.Join(' ', Raw.Select(b => b.ToString("X2")));
}
