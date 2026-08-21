using System.Drawing;
using YgoFm.Core;

namespace YgoFm.Vision;

/// <summary>How confident the recogniser is about one slot.</summary>
public enum SlotVerdict
{
    /// <summary>Nothing there: the crop was a flat expanse with no artwork in it.</summary>
    Empty,

    /// <summary>Something is there but the match is too weak or too close to call.</summary>
    Uncertain,

    /// <summary>A clear winner, well ahead of the runner-up.</summary>
    Confident,
}

/// <summary>How the selected region is divided into cards.</summary>
/// <param name="SlotCount">How many card positions the region spans.</param>
public sealed record HandLayout(int SlotCount)
{
    /// <summary>Five slots, because that is the hand size.</summary>
    public static HandLayout Default { get; } = new(5);
}

/// <summary>What the recogniser made of one card position.</summary>
public sealed record SlotReading
{
    public required int Slot { get; init; }

    /// <summary>The slot's area within the captured frame, in frame pixels.</summary>
    public required Rectangle SlotBounds { get; init; }

    /// <summary>Where within the slot the winning card's art was found — approximate, see
    /// <see cref="ArtMatch.MatchedRegion"/>. Falls back to the whole slot when empty.</summary>
    public required Rectangle ArtBounds { get; init; }

    public required SlotVerdict Verdict { get; init; }

    /// <summary>Best matching card, or null when the slot looked empty.</summary>
    public Card? Card { get; init; }

    /// <summary>Runner-up, kept so a wrong call can be seen for what it is.</summary>
    public Card? RunnerUp { get; init; }

    public double Score { get; init; }

    public double RunnerUpScore { get; init; }

    /// <summary>How far the winner led the runner-up.</summary>
    public double Margin => Score - RunnerUpScore;
}

/// <summary>
/// Turns a captured picture of the player's cards into a list of card identities.
///
/// It knows nothing about where the picture came from, which keeps it emulator-agnostic by
/// construction, and nothing about fusions, which keeps recognition and rules apart. The actual
/// matching — including finding the right alignment within a loosely-selected slot — lives in
/// <see cref="CardArtLibrary"/>; this class is just the "cut into N slots and ask" loop plus the
/// thresholds for turning a raw score into a verdict a person can act on.
/// </summary>
public sealed class HandReader
{
    /// <summary>
    /// Below this similarity, the best match is treated as a guess rather than a reading.
    /// Provisional, and set low: even a correct match against real, filtered emulator output
    /// was measured landing anywhere from the high 0.3s to the high 0.7s depending on the
    /// card, so a strict threshold would reject good reads as often as bad ones. Leaning on
    /// <see cref="ConfidentMargin"/> to do the real filtering here is deliberate.
    /// </summary>
    public const double ConfidentScore = 0.35;

    /// <summary>
    /// The winner must also lead the runner-up by this much. A high score with a thin margin
    /// means two pieces of artwork genuinely look alike, which no threshold on score alone
    /// would catch — and with real captures landing at unpredictable absolute scores, margin
    /// is the more trustworthy signal of the two.
    /// </summary>
    public const double ConfidentMargin = 0.05;

    private readonly CardDatabase _cards;
    private readonly CardArtLibrary _art;

    public HandReader(CardDatabase cards, CardArtLibrary art)
    {
        _cards = cards;
        _art = art;
    }

    /// <summary>Divide a frame into equal side-by-side card positions.</summary>
    public static Rectangle[] SlotBounds(Size frame, int slotCount)
    {
        if (slotCount < 1)
            throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "Need at least one slot.");

        var slots = new Rectangle[slotCount];
        for (var i = 0; i < slotCount; i++)
        {
            // Split on exact edges so the slots tile the frame with no gap or overlap, even
            // when the width does not divide evenly.
            var left = (int)Math.Round(frame.Width * i / (double)slotCount);
            var right = (int)Math.Round(frame.Width * (i + 1) / (double)slotCount);
            slots[i] = Rectangle.FromLTRB(left, 0, Math.Max(right, left + 1), frame.Height);
        }

        return slots;
    }

    public IReadOnlyList<SlotReading> Read(Bitmap frame, HandLayout layout)
    {
        var slots = SlotBounds(frame.Size, layout.SlotCount);
        var readings = new List<SlotReading>(slots.Length);

        for (var i = 0; i < slots.Length; i++)
            readings.Add(ReadSlot(frame, i + 1, slots[i]));

        return readings;
    }

    private SlotReading ReadSlot(Bitmap frame, int slotNumber, Rectangle slotBounds)
    {
        using var slotBitmap = frame.Clone(slotBounds, frame.PixelFormat);

        if (CardArtLibrary.LooksEmpty(slotBitmap))
        {
            return new SlotReading
            {
                Slot = slotNumber,
                SlotBounds = slotBounds,
                ArtBounds = slotBounds,
                Verdict = SlotVerdict.Empty,
            };
        }

        var match = _art.Match(slotBitmap);
        var confident = match.Score >= ConfidentScore && match.Margin >= ConfidentMargin;

        // MatchedRegion is relative to the slot bitmap; translate it back to frame coordinates.
        var artBounds = new Rectangle(
            slotBounds.X + match.MatchedRegion.X, slotBounds.Y + match.MatchedRegion.Y,
            match.MatchedRegion.Width, match.MatchedRegion.Height);

        return new SlotReading
        {
            Slot = slotNumber,
            SlotBounds = slotBounds,
            ArtBounds = artBounds,
            Verdict = confident ? SlotVerdict.Confident : SlotVerdict.Uncertain,
            Card = _cards.TryGet(match.CardId, out var card) ? card : null,
            RunnerUp = _cards.TryGet(match.RunnerUpId, out var runner) ? runner : null,
            Score = match.Score,
            RunnerUpScore = match.RunnerUpScore,
        };
    }
}
