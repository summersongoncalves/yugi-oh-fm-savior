using System.IO;
using System.Windows.Media.Imaging;
// System.Drawing is aliased rather than imported wholesale: its Color, Rectangle and Image
// types collide with WPF's own, which is exactly the gotcha CLAUDE.md warns about for any
// WPF file in this repository.
using DrawingBitmap = System.Drawing.Bitmap;

namespace YgoFm.App;

/// <summary>
/// The one place this project converts a captured <see cref="System.Drawing.Bitmap"/> (what
/// YgoFm.Vision produces) into something WPF can put in an Image control.
/// </summary>
internal static class BitmapInterop
{
    public static BitmapSource ToBitmapSource(this DrawingBitmap bitmap)
    {
        using var stream = new MemoryStream();
        // PNG round-trips losslessly and needs no native handle bookkeeping, unlike the more
        // common CreateBitmapSourceFromHBitmap interop path.
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
