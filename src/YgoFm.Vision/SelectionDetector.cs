using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace YgoFm.Vision;

/// <summary>
/// Finds which hand slot the game currently has selected, by looking for the small red arrow
/// marker Forbidden Memories draws at the selected card's left edge.
///
/// This is what lets the "path 1 teaches path 2" pipeline know *which* slot's artwork to pair
/// with a freshly OCR'd card name (see <see cref="NameReader"/> and <see cref="TaughtCardLibrary"/>):
/// the name panel says what card is selected, this says which of the five slots that is.
///
/// Calibrated against four real captures, each with the arrow pointing at a different slot
/// (including the very first one). Two things turned out to matter:
///
/// - The marker is a strong, near-pure red (B and G both close to zero) that nothing else in
///   the hand row matches quite as tightly — a loose red threshold instead caught orange/red
///   card artwork (e.g. Ushi Oni's face). Restricting the scan to the bottom of the slot, where
///   the ATK/DEF row lives rather than the artwork, was needed too for the same reason.
/// - The arrow sits *at* a slot's left edge rather than inside its rectangle — for the very
///   first slot it is drawn mostly or fully off the left edge of the whole row. So the slot is
///   chosen by which slot's left-edge position is nearest the marker, not by which slot's
///   rectangle contains it; the latter got the boundary cases wrong by one slot.
/// </summary>
public static class SelectionDetector
{
    /// <summary>BGR bounds for the marker's colour: saturated red, negligible blue or green.</summary>
    private static readonly Scalar LowerRed = new(0, 0, 70);
    private static readonly Scalar UpperRed = new(12, 12, 255);

    /// <summary>Where the ATK/DEF row (and so the marker) lives, as a fraction of slot height.</summary>
    private const double BottomBandStart = 0.55;

    /// <summary>Below this many matching pixels, treat it as noise rather than a marker.</summary>
    private const int MinPixels = 4;

    /// <summary>Above this many matching pixels, it is too big to be the marker — likely a
    /// false positive from something else red in the frame.</summary>
    private const int MaxPixels = 700;

    /// <summary>
    /// The selected slot (0-based), or null if no marker was found — most likely because the
    /// selected hand region does not extend down far enough to include the ATK/DEF row.
    /// </summary>
    public static int? FindSelectedSlot(Bitmap handRegion, int slotCount)
    {
        using var bgr = TemplateMatcher.ToBgr(handRegion);

        var band = new Rect(0, (int)(bgr.Height * BottomBandStart), bgr.Width,
            bgr.Height - (int)(bgr.Height * BottomBandStart));
        using var bottomBand = new Mat(bgr, band);

        using var mask = new Mat();
        Cv2.InRange(bottomBand, LowerRed, UpperRed, mask);

        var moments = Cv2.Moments(mask, binaryImage: true);
        if (moments.M00 < MinPixels || moments.M00 > MaxPixels) return null;

        var centroidX = moments.M10 / moments.M00;

        var bestSlot = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < slotCount; i++)
        {
            var edgeX = bgr.Width * i / (double)slotCount;
            var distance = Math.Abs(centroidX - edgeX);
            if (distance < bestDistance) { bestDistance = distance; bestSlot = i; }
        }

        return bestSlot;
    }
}
