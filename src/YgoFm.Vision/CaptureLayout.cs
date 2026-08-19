using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YgoFm.Vision;

/// <summary>
/// The result of calibration: where the game picture sits inside a captured frame,
/// and where each region of interest sits inside that game picture.
///
/// Two levels on purpose. <see cref="Viewport"/> is proportions of the whole captured
/// frame and depends on the emulator and window size. <see cref="Regions"/> is
/// proportions of the viewport, which is a property of the game itself — so those
/// values are portable between emulators and can ship as defaults.
/// </summary>
public sealed class CaptureLayout
{
    /// <summary>Title of the window this was calibrated against, purely as a reminder to the user.</summary>
    public string? SourceWindowTitle { get; set; }

    /// <summary>The game picture, as proportions of the captured frame.</summary>
    public NormRect? Viewport { get; set; }

    /// <summary>Regions of interest, as proportions of <see cref="Viewport"/>.</summary>
    public Dictionary<string, NormRect> Regions { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsComplete =>
        Viewport is not null &&
        RegionNames.CalibrationOrder
            .Where(r => r != RegionNames.Viewport)
            .All(Regions.ContainsKey);

    /// <summary>Resolve the game picture to pixels within a captured frame of the given size.</summary>
    public Rectangle ViewportPixels(Size frameSize) =>
        Viewport?.ToPixels(new Rectangle(Point.Empty, frameSize))
        ?? throw new InvalidOperationException("Layout has no viewport — run calibration first.");

    /// <summary>Resolve one region to pixels within a captured frame of the given size.</summary>
    public Rectangle RegionPixels(string region, Size frameSize) =>
        Regions.TryGetValue(region, out var norm)
            ? norm.ToPixels(ViewportPixels(frameSize))
            : throw new KeyNotFoundException($"Region '{region}' has not been calibrated.");

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static CaptureLayout Load(string path) =>
        JsonSerializer.Deserialize<CaptureLayout>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"'{path}' is not a valid layout file.");
}
