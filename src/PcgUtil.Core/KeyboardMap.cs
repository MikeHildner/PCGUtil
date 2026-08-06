namespace PcgUtil.Core;

/// <summary>
/// Geometry for drawing an 88-key keyboard: A0 (MIDI 21) to C8 (MIDI 108), 52 white keys and
/// 36 black ones. Pure numbers, no markup — the drawing lives in the report, the arithmetic
/// lives here where it can be pinned exactly.
///
/// White keys are laid out edge to edge, so a white key's index <em>is</em> its position. A
/// black key always sits above the boundary between the white key below it and the next one,
/// which makes its centre exactly <c>(WhiteIndex(note - 1) + 1) × width</c> — symmetric
/// rather than the asymmetric offsets of a real piano, and far easier to read at print size.
/// </summary>
public static class KeyboardMap
{
    /// <summary>A0 — the lowest key of an 88-key instrument.</summary>
    public const int LowestKey = 21;

    /// <summary>C8 — the highest.</summary>
    public const int HighestKey = 108;

    /// <summary>White keys between <see cref="LowestKey"/> and <see cref="HighestKey"/>.</summary>
    public const int WhiteKeyCount = 52;

    // White keys per pitch class, counted from C: C0 D1 E2 F3 G4 A5 B6, with each black key
    // sharing the index of the white key below it.
    private static readonly int[] WhiteOfPitchClass = { 0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6 };

    public static bool IsBlack(int note) => note % 12 is 1 or 3 or 6 or 8 or 10;

    /// <summary>
    /// Position of a note along the keyboard, counted in white keys from A0. A black key
    /// reports the index of the white key it sits above the right edge of.
    /// </summary>
    public static int WhiteIndex(int note) =>
        7 * (note / 12) + WhiteOfPitchClass[note % 12] - 12;

    /// <summary>Left and right edge of one key, in the same units as <paramref name="white"/>.</summary>
    public static (double Left, double Right) Span(int note, double white, double black)
    {
        if (!IsBlack(note))
        {
            double left = WhiteIndex(note) * white;
            return (left, left + white);
        }
        double centre = (WhiteIndex(note - 1) + 1) * white;
        return (centre - black / 2, centre + black / 2);
    }

    /// <summary>
    /// Left and right edge of a key zone, clamped to the 88 keys — combi zones routinely run
    /// the full 0–127 MIDI range, which is wider than any keyboard.
    /// </summary>
    public static (double Left, double Right) ZoneSpan(int bottom, int top, double white, double black)
    {
        int lo = Math.Clamp(Math.Min(bottom, top), LowestKey, HighestKey);
        int hi = Math.Clamp(Math.Max(bottom, top), LowestKey, HighestKey);
        double left = Span(lo, white, black).Left;
        double right = Span(hi, white, black).Right;
        return (Math.Max(0, left), Math.Min(WhiteKeyCount * white, right));
    }

    /// <summary>Horizontal centre of a key, for axis labels and split markers.</summary>
    public static double Centre(int note, double white, double black)
    {
        var (left, right) = Span(note, white, black);
        return (left + right) / 2;
    }
}
