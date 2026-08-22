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

    /// <summary>Ids of every card taught so far, for a "what has this learned" listing.</summary>
    public IReadOnlyCollection<int> LearnedCardIds => _matcher.Ids;

    /// <summary>Where a learned card's image is stored, for a listing to show what was actually
    /// captured (as opposed to the official art) — the point of looking is to catch a bad lesson.</summary>
    public string PathFor(int cardId) => Path.Combine(_folder, $"{cardId}.png");

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

    /// <summary>
    /// Forgets everything taught, on disk and in memory. For testing the teaching pipeline
    /// itself from a clean slate, without needing to touch the file system by hand.
    ///
    /// Both halves matter: removing only the in-memory <see cref="TemplateMatcher"/> entries
    /// would leave the PNGs on disk, so the very next <see cref="Load"/> (e.g. the next time the
    /// app starts) would just read them straight back in. Deleting only the files but not
    /// calling <c>_matcher.Remove</c> would leave this running session still matching against
    /// templates that no longer exist on disk, silently diverging from what a fresh load would
    /// see. <c>.ToList()</c> is needed before the loop because <see cref="TemplateMatcher.Remove"/>
    /// mutates the very dictionary that <c>_matcher.Ids</c> reads from — iterating a collection
    /// while removing from it throws, so the id list is copied out first.
    /// </summary>
    public void Clear()
    {
        foreach (var id in _matcher.Ids.ToList())
            _matcher.Remove(id);

        foreach (var path in Directory.EnumerateFiles(_folder, "*.png"))
            File.Delete(path);
    }

    public void Dispose() => _matcher.Dispose();
}
