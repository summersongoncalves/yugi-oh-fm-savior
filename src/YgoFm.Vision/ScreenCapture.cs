using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YgoFm.Vision;

/// <summary>
/// Takes pictures of what is on screen. Emulator-agnostic by construction: it copies
/// pixels out of the composited desktop, so it neither knows nor cares which program
/// drew them or which graphics backend was used.
/// </summary>
public static class ScreenCapture
{
    /// <summary>The visible frame of a window, in screen pixels.</summary>
    public static Rectangle WindowBounds(IntPtr hWnd)
    {
        // The extended frame bounds exclude the invisible resize border, so the captured
        // frame does not start with a few pixels of desktop showing through.
        if (NativeMethods.DwmGetWindowAttribute(
                hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeMethods.RECT frame, Marshal.SizeOf<NativeMethods.RECT>()) == 0
            && frame.Width > 0 && frame.Height > 0)
        {
            return Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
        }

        return NativeMethods.GetWindowRect(hWnd, out var rect)
            ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : Rectangle.Empty;
    }

    /// <summary>
    /// Raise a window to the front. Needed before capturing, because we copy pixels from the
    /// composited desktop — a window sitting behind another one would capture the wrong thing.
    /// </summary>
    public static void BringToFront(IntPtr hWnd) => NativeMethods.SetForegroundWindow(hWnd);

    /// <summary>Capture the area a window occupies, including anything drawn on top of it.</summary>
    public static Bitmap CaptureWindow(IntPtr hWnd)
    {
        var bounds = WindowBounds(hWnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("That window has no visible area to capture.");

        return CaptureRegion(bounds);
    }

    /// <summary>Capture one whole monitor — the path to use when the emulator runs fullscreen.</summary>
    public static Bitmap CaptureScreen(int monitorIndex = 0)
    {
        var screens = Screen.AllScreens;
        var screen = screens[Math.Clamp(monitorIndex, 0, screens.Length - 1)];
        return CaptureRegion(screen.Bounds);
    }

    public static Bitmap CaptureRegion(Rectangle region)
    {
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    /// <summary>
    /// True when a captured frame is entirely one flat colour, which means the capture
    /// failed rather than the game being black. Some graphics backends refuse to be read
    /// this way, and the honest thing is to tell the user instead of silently matching noise.
    /// </summary>
    public static bool LooksBlank(Bitmap frame)
    {
        var first = frame.GetPixel(0, 0);
        var stepX = Math.Max(1, frame.Width / 32);
        var stepY = Math.Max(1, frame.Height / 32);

        for (var y = 0; y < frame.Height; y += stepY)
            for (var x = 0; x < frame.Width; x += stepX)
                if (frame.GetPixel(x, y) != first)
                    return false;

        return true;
    }
}
