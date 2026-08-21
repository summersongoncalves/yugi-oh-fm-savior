using System.Drawing;

namespace YgoFm.Vision;

/// <summary>
/// Cuts a piece out of a captured frame. This is the handover point: everything upstream
/// deals in screens and windows, everything downstream deals only in these little pictures.
/// </summary>
public static class FrameCropper
{
    public static Bitmap Crop(Bitmap frame, Rectangle region)
    {
        // Clamp, because a region selected against a slightly different window size can
        // otherwise ask for pixels just past the edge of the frame.
        var safe = Rectangle.Intersect(region, new Rectangle(Point.Empty, frame.Size));
        if (safe.Width <= 0 || safe.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region), "Region falls outside the captured frame.");

        return frame.Clone(safe, frame.PixelFormat);
    }
}
