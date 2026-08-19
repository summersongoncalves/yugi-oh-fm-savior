using System.Drawing;

namespace YgoFm.Vision;

/// <summary>
/// A rectangle stored as proportions (0..1) of some parent box rather than as pixels.
///
/// This is what makes the tool emulator-agnostic: "the first card in hand" is always
/// at the same proportions of the game image, whether that image is being drawn at
/// 320x240 in a small window or stretched across a 4K screen.
/// </summary>
public sealed record NormRect(double X, double Y, double W, double H)
{
    /// <summary>Resolve these proportions against a concrete parent rectangle, in pixels.</summary>
    public Rectangle ToPixels(Rectangle parent) => new(
        parent.X + (int)Math.Round(X * parent.Width),
        parent.Y + (int)Math.Round(Y * parent.Height),
        Math.Max(1, (int)Math.Round(W * parent.Width)),
        Math.Max(1, (int)Math.Round(H * parent.Height)));

    /// <summary>Express a pixel rectangle as proportions of the given parent.</summary>
    public static NormRect FromPixels(Rectangle r, Rectangle parent)
    {
        if (parent.Width <= 0 || parent.Height <= 0)
            throw new ArgumentException("Parent rectangle has no area.", nameof(parent));

        return new NormRect(
            (r.X - parent.X) / (double)parent.Width,
            (r.Y - parent.Y) / (double)parent.Height,
            r.Width / (double)parent.Width,
            r.Height / (double)parent.Height);
    }
}
