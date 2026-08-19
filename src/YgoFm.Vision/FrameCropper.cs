using System.Drawing;
using System.Drawing.Imaging;

namespace YgoFm.Vision;

/// <summary>
/// Applies a calibrated layout to a captured frame, producing one small image per region.
/// This is the handover point: everything upstream deals in screens and windows,
/// everything downstream deals only in these little pictures.
/// </summary>
public static class FrameCropper
{
    public static Bitmap Crop(Bitmap frame, Rectangle region)
    {
        // Clamp, because a layout calibrated on a slightly different window size can
        // otherwise ask for pixels just past the edge of the frame.
        var safe = Rectangle.Intersect(region, new Rectangle(Point.Empty, frame.Size));
        if (safe.Width <= 0 || safe.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region), "Region falls outside the captured frame.");

        return frame.Clone(safe, frame.PixelFormat);
    }

    /// <summary>Cut out every calibrated region, keyed by region name.</summary>
    public static Dictionary<string, Bitmap> CropAll(Bitmap frame, CaptureLayout layout)
    {
        var crops = new Dictionary<string, Bitmap>();
        foreach (var name in layout.Regions.Keys)
            crops[name] = Crop(frame, layout.RegionPixels(name, frame.Size));
        return crops;
    }

    /// <summary>
    /// Write each region out as a PNG file. Purely a verification aid — the fastest way
    /// to confirm a calibration is correct is to look at what it actually cut out.
    /// </summary>
    public static string ExportForReview(Bitmap frame, CaptureLayout layout, string directory)
    {
        Directory.CreateDirectory(directory);

        using (var viewport = Crop(frame, layout.ViewportPixels(frame.Size)))
            viewport.Save(Path.Combine(directory, "00-viewport.png"), ImageFormat.Png);

        var index = 1;
        foreach (var name in RegionNames.CalibrationOrder.Where(layout.Regions.ContainsKey))
        {
            using var crop = Crop(frame, layout.RegionPixels(name, frame.Size));
            crop.Save(Path.Combine(directory, $"{index++:00}-{name}.png"), ImageFormat.Png);
        }

        return directory;
    }
}
