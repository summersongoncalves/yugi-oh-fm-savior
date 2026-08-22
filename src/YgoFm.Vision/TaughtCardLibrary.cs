using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;

namespace YgoFm.Vision;

/// <summary>
/// A personal template library, built on the user's own machine from their own emulator's
/// rendering — the "path 1 teaches path 2" idea from CLAUDE.md's recognition strategy. Card
/// artwork here was captured under the exact filters, blur and colour grading the user's setup
/// produces, so matching a new capture against it is comparing like with like, instead of
/// comparing against pristine official art that a real capture may only ever correlate weakly
/// with (measured directly: real captures scored as low as 0.05-0.4 against official art for
/// unmistakably-correct cards, purely from that mismatch).
///
/// Persisted as one PNG per learned card under a "templates" folder, named by card id, so it
/// survives between runs and grows across every session rather than starting from zero each time.
/// </summary>
public sealed class TaughtCardLibrary : IDisposable
{
    private readonly string _folder;
    private readonly TemplateMatcher _matcher = new();

    private TaughtCardLibrary(string folder) => _folder = folder;

    /// <summary>How many cards have been taught so far.</summary>
    public int Count => _matcher.Count;

    /// <summary>Loads whatever has already been taught in a previous session.</summary>
    public static TaughtCardLibrary Load(string folder)
    {
        Directory.CreateDirectory(folder);
        var library = new TaughtCardLibrary(folder);

        foreach (var path in Directory.EnumerateFiles(folder, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(name, out var cardId)) continue;

            using var bitmap = new Bitmap(path);
            library._matcher.Set(cardId, TemplateMatcher.ToBgr(bitmap));
        }

        return library;
    }

    /// <summary>
    /// Record this crop as what card <paramref name="cardId"/> looks like on this machine,
    /// overwriting whatever was taught for it before. Saved to disk immediately so a later
    /// session picks up where this one left off.
    /// </summary>
    public void Teach(int cardId, Bitmap artCrop)
    {
        artCrop.Save(Path.Combine(_folder, $"{cardId}.png"), ImageFormat.Png);
        _matcher.Set(cardId, TemplateMatcher.ToBgr(artCrop));
    }

    /// <summary>
    /// The best match against everything taught so far, or null if nothing has been taught yet
    /// (there is nothing to compare against) — callers should fall back to
    /// <see cref="CardArtLibrary"/> in that case, and whenever this returns too weak a match.
    /// </summary>
    public ArtMatch? Match(Bitmap capturedRegion) => _matcher.Match(capturedRegion);

    public void Dispose() => _matcher.Dispose();
}
