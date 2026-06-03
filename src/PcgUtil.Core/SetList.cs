namespace PcgUtil.Core;

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

    /// <summary>
    /// Raw reference bytes (slot offset +8). These encode the Program / Combi / Song
    /// target for the slot; the encoding is not yet decoded, so the bytes are kept
    /// verbatim for inspection and future work.
    /// </summary>
    public required IReadOnlyList<byte> Reference { get; init; }

    public bool IsEmpty => Name.Length == 0;

    public string ReferenceHex => string.Join(' ', Reference.Select(b => b.ToString("X2")));
}
