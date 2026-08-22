using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace YgoFm.Vision;

/// <summary>
/// Reads whatever text the game prints in the hovered-card name panel, using Windows' built-in
/// OCR (Optical Character Recognition) engine — on-device, no network dependency, no model to
/// ship. This is the "teacher" in the two-path recognition strategy from CLAUDE.md: text
/// survives an emulator's filters far better than artwork, so a name read here is paired with
/// an artwork crop to build a personal template library (see <see cref="TaughtCardLibrary"/>)
/// instead of relying solely on comparing against official art.
///
/// Measured against 16 real captures collected across two sessions, this read the exact name in
/// 14 of them and landed one character off (still unambiguously resolvable by fuzzy matching)
/// in the other 2 — far more reliable than artwork matching has been. Two things mattered:
/// upscaling the crop 2x before recognition (the raw in-game font is otherwise too small for the
/// engine), and giving it a generously wide crop (a tight one truncated longer names outright,
/// which no upscaling could recover).
/// </summary>
public static class NameReader
{
    private static readonly OcrEngine? Engine =
        OcrEngine.TryCreateFromLanguage(new Language("en")) ?? OcrEngine.TryCreateFromUserProfileLanguages();

    /// <summary>True on any Windows the OCR engine actually initialised on. Windows.Media.Ocr
    /// has existed since the first Windows 10 release, so this should be true on any supported
    /// machine; kept as a check rather than an assumption in case a minimal/server install is
    /// missing language packs.</summary>
    public static bool IsAvailable => Engine is not null;

    /// <summary>Read whatever text is in this crop. Returns "" if nothing was recognised.</summary>
    public static async Task<string> Read(Bitmap namePanelCrop)
    {
        if (Engine is null) return "";

        // The in-game font renders too small for the OCR engine to read reliably at native
        // size; a plain 2x nearest-neighbour upscale (no smoothing, so no new blur added) is
        // what closed most of the gap when this was measured against real captures.
        using var upscaled = Upscale(namePanelCrop, 2);
        using var softwareBitmap = await ToSoftwareBitmap(upscaled);

        var result = await Engine.RecognizeAsync(softwareBitmap);
        return result.Text.Trim();
    }

    private static Bitmap Upscale(Bitmap source, int factor)
    {
        var scaled = new Bitmap(source.Width * factor, source.Height * factor);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        return scaled;
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmap(Bitmap bitmap)
    {
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, ImageFormat.Png);
        pngStream.Position = 0;

        using var randomAccessStream = new InMemoryRandomAccessStream();
        using var output = randomAccessStream.GetOutputStreamAt(0);
        await output.WriteAsync(pngStream.GetWindowsRuntimeBuffer());
        await output.FlushAsync();

        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        return await decoder.GetSoftwareBitmapAsync();
    }
}
