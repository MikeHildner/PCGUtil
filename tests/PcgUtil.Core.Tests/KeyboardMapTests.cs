using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

/// <summary>
/// The keyboard geometry, pinned exactly. Every drawn zone rides on these numbers, and an
/// off-by-one here would put a layer's key range on the wrong notes — the one error a gig
/// sheet must never make.
/// </summary>
public class KeyboardMapTests
{
    [Fact]
    public void The_88_keys_are_52_white_and_36_black()
    {
        int white = 0, black = 0;
        for (int n = KeyboardMap.LowestKey; n <= KeyboardMap.HighestKey; n++)
            if (KeyboardMap.IsBlack(n)) black++; else white++;

        Assert.Equal(88, white + black);
        Assert.Equal(KeyboardMap.WhiteKeyCount, white);
        Assert.Equal(52, white);
        Assert.Equal(36, black);
    }

    [Fact]
    public void White_positions_are_anchored_at_the_ends_and_at_middle_C()
    {
        Assert.Equal(0, KeyboardMap.WhiteIndex(21));    // A0, the lowest key
        Assert.Equal(23, KeyboardMap.WhiteIndex(60));   // C4 — middle C, this project's convention
        Assert.Equal(50, KeyboardMap.WhiteIndex(107));  // B7
        Assert.Equal(51, KeyboardMap.WhiteIndex(108));  // C8, the highest
    }

    [Fact]
    public void Black_keys_are_the_five_sharps_of_every_octave()
    {
        foreach (int n in Enumerable.Range(KeyboardMap.LowestKey, 88))
            Assert.Equal(n % 12 is 1 or 3 or 6 or 8 or 10, KeyboardMap.IsBlack(n));

        Assert.True(KeyboardMap.IsBlack(61));    // C#4
        Assert.False(KeyboardMap.IsBlack(60));   // C4
    }

    [Fact]
    public void Keys_march_left_to_right_without_going_backwards()
    {
        double previous = -1;
        for (int n = KeyboardMap.LowestKey; n <= KeyboardMap.HighestKey; n++)
        {
            double left = KeyboardMap.Span(n, 14, 8).Left;
            Assert.True(left >= previous, $"note {n} starts left of note {n - 1}");
            previous = left;
        }
    }

    [Fact]
    public void Every_black_key_is_centred_on_a_white_key_boundary()
    {
        for (int n = KeyboardMap.LowestKey; n <= KeyboardMap.HighestKey; n++)
        {
            if (!KeyboardMap.IsBlack(n)) continue;
            double centre = KeyboardMap.Centre(n, 14, 8);
            Assert.Equal(0, centre % 14, 6);   // lands exactly on a multiple of the white width
        }
    }

    [Fact]
    public void A_full_range_zone_covers_the_whole_keyboard_and_no_more()
    {
        // Combi zones are stored over the full MIDI range, wider than any keyboard.
        var (left, right) = KeyboardMap.ZoneSpan(0, 127, 14, 8);
        Assert.Equal(0, left, 6);
        Assert.Equal(52 * 14, right, 6);

        // The Footloose split: organ up to A4, brass from A#4.
        var organ = KeyboardMap.ZoneSpan(0, 69, 14, 8);
        var brass = KeyboardMap.ZoneSpan(70, 127, 14, 8);
        Assert.Equal(0, organ.Left, 6);
        Assert.True(organ.Right <= brass.Left + 8, "the two zones should meet, not overlap widely");
        Assert.Equal(52 * 14, brass.Right, 6);
    }
}
