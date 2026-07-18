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

    /// <summary>The slot's comment field (up to 512 ASCII chars, may contain line breaks) —
    /// the per-song notes shown on the hardware's Set List display.</summary>
    public required string Description { get; init; }

    /// <summary>Slot color 0–15 (the hardware's 16-color picker; name via
    /// <see cref="SetListSlotColors.Name"/>).</summary>
    public required int Color { get; init; }

    /// <summary>Slot volume 0–127.</summary>
    public required int Volume { get; init; }

    /// <summary>Slot transpose in semitones (0 when the slot plays at pitch).</summary>
    public required int Transpose { get; init; }

    public bool IsEmpty => Name.Length == 0;
}

/// <summary>
/// A decoded slot reference. <c>B0 &amp; 0x03</c> is the type (Combi=0, Program=1, Song=2)
/// and <c>B0</c> bits 2–5 carry the slot color; <c>B1 &amp; 0x1F</c> is the bank, and
/// <c>B2 &amp; 0x7F</c> is the item number within the bank. Raw bytes 3–5 are the slot's
/// comment font size (constant 6 on every observed file), volume, and transpose ×32
/// (probe-file verified 2026-07-18) — not part of the reference.
/// </summary>
public sealed class SetListReference
{
    public required PcgItemKind Kind { get; init; }
    public required int Bank { get; init; }
    public required int Index { get; init; }
    public required IReadOnlyList<byte> Raw { get; init; }

    public string Hex => string.Join(' ', Raw.Select(b => b.ToString("X2")));
}

/// <summary>
/// The hardware's 16 slot colors, in the color picker's own order (left to right, top to
/// bottom — verified against a probe file whose slots were set to the 16 colors in order).
/// Css values approximate the picker swatches for on-screen chips.
/// </summary>
public static class SetListSlotColors
{
    private static readonly (string Name, string Css)[] Palette =
    {
        ("Default", "#9aa2ad"),
        ("Charcoal", "#3a3f46"),
        ("Brick", "#c0504d"),
        ("Burgundy", "#7b2d42"),
        ("Ivy", "#8db73f"),
        ("Olive", "#8a8a3c"),
        ("Gold", "#c8a83c"),
        ("Cacao", "#8a5d42"),
        ("Indigo", "#4653c7"),
        ("Navy", "#2c4f9e"),
        ("Rose", "#c090b8"),
        ("Lavender", "#9b7fd4"),
        ("Azure", "#57a7d8"),
        ("Denim", "#4a7ab5"),
        ("Silver", "#b8bcc4"),
        ("Slate", "#5d6b7a"),
    };

    public static int Count => Palette.Length;

    public static string Name(int color) =>
        color >= 0 && color < Palette.Length ? Palette[color].Name : $"Color {color}";

    public static string Css(int color) =>
        color >= 0 && color < Palette.Length ? Palette[color].Css : "#9aa2ad";
}
