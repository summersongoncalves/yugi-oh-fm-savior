using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using CvSize = OpenCvSharp.Size;

namespace YgoFm.Vision;

/// <summary>What the recogniser thinks a piece of captured artwork is.</summary>
/// <param name="CardId">Best matching card id.</param>
/// <param name="Score">Its match score (OpenCV normalised correlation, roughly 0..1).</param>
/// <param name="RunnerUpId">Second best card id, or 0 if there was none.</param>
/// <param name="RunnerUpScore">The second best score.</param>
/// <param name="MatchedRegion">
/// Where in the search image the winning card's art was found, in the search image's own
/// pixels. Approximate — the search only looks for the right *position*, not the right
/// *scale*, so this assumes the on-screen card renders close to the reference art's native
/// size. Good enough to outline for the user; not meant for anything more precise.
/// </param>
public sealed record ArtMatch(int CardId, double Score, int RunnerUpId, double RunnerUpScore, Rectangle MatchedRegion)
{
    /// <summary>How far ahead the winner is over the runner-up.</summary>
    public double Margin => Score - RunnerUpScore;
}

/// <summary>
/// The reference artwork for all 722 cards, sliced out of the game's own card sheet, and the
/// machinery to find which one best matches a captured piece of artwork.
///
/// Matching is two stages, because comparing a search image against 722 reference tiles with a
/// full alignment search every time is too slow for a live poll loop:
///
/// 1. A cheap colour-histogram comparison (translation- and blur-insensitive by construction,
///    since a histogram throws away *where* a colour appears) ranks all 722 cards and keeps a
///    shortlist of the most plausible ones.
/// 2. OpenCV template matching, run only on that shortlist, actually searches for the best
///    alignment of each candidate's art within the captured region and keeps the best-scoring
///    one. Both sides are downsampled first to a small common size before this search — this
///    was measured to matter: an emulator's upscaling blur otherwise tanks pixel-level
///    correlation even for the correct card, because sharp reference art and a blurred capture
///    of the same picture do not correlate well pixel-for-pixel. Downsampling both to the same
///    coarse resolution equalises that, and incidentally makes the alignment search itself
///    cheap enough to run per-frame.
///
/// The sheet's grid was measured off the file rather than assumed: the separator bands sit at
/// exact 107 pixel intervals across and 101 down, giving 102x96 tiles inset 5 pixels from the
/// origin, 25 to a row, 29 rows, 725 positions for 722 cards. A border scan of all 722 tiles
/// found no separator pixel bleeding into any of them, so the tiles need no inset.
///
/// This is deliberately the *large* card sheet, not the small one that shipped first. The two
/// are not the same crop of each card's art — the small sheet frames noticeably wider than the
/// icon the game actually draws for a hand card. That was found by comparing a real captured
/// hand card against both sheets side by side: the large sheet's framing matches closely, the
/// small one does not.
/// </summary>
public sealed class CardArtLibrary : IDisposable
{
    public const int TileWidth = 102;
    public const int TileHeight = 96;

    /// <summary>Width of the separator bands, which is also the sheet's outer margin.</summary>
    public const int Separator = 5;

    public const int Columns = 25;

    /// <summary>How many cards the histogram stage keeps for the expensive alignment search.</summary>
    private const int ShortlistSize = 120;

    /// <summary>
    /// Width both the reference tile and the search region are downsampled to before the
    /// alignment search. Chosen empirically against real captures: coarse enough to shrug off
    /// upscaling blur, fine enough to still tell most cards apart.
    /// </summary>
    private const int MatchWidth = 28;

    private readonly Mat _sheet;
    private readonly Mat[] _histograms; // 1-indexed by card id; index 0 unused
    private readonly Mat[] _smallTiles;
    private readonly CvSize _matchSize;

    private CardArtLibrary(Mat sheet, Mat[] histograms, Mat[] smallTiles, CvSize matchSize)
    {
        _sheet = sheet;
        _histograms = histograms;
        _smallTiles = smallTiles;
        _matchSize = matchSize;
    }

    public int Count => _histograms.Length - 1;

    /// <summary>Where the artwork for a card sits on the sheet.</summary>
    public static Rectangle TileFor(int cardId)
    {
        if (cardId < 1)
            throw new ArgumentOutOfRangeException(nameof(cardId), cardId, "Card ids start at 1.");

        var index = cardId - 1;
        return new Rectangle(
            Separator + index % Columns * (TileWidth + Separator),
            Separator + index / Columns * (TileHeight + Separator),
            TileWidth,
            TileHeight);
    }

    public static CardArtLibrary Load(string sheetPath, int cardCount)
    {
        if (!File.Exists(sheetPath))
            throw new FileNotFoundException(
                $"Card artwork sheet not found at '{sheetPath}'. It ships in the repository's data folder.",
                sheetPath);

        var sheet = Cv2.ImRead(sheetPath, ImreadModes.Color);
        if (sheet.Empty())
            throw new InvalidDataException($"'{sheetPath}' could not be read as an image.");

        try
        {
            var rows = (cardCount + Columns - 1) / Columns;
            var expectedWidth = Separator + Columns * (TileWidth + Separator);
            var expectedHeight = Separator + rows * (TileHeight + Separator);

            if (sheet.Width != expectedWidth || sheet.Height != expectedHeight)
                throw new InvalidDataException(
                    $"'{sheetPath}' is {sheet.Width}x{sheet.Height}; the {Columns}-column grid of " +
                    $"{TileWidth}x{TileHeight} tiles needed for {cardCount} cards is " +
                    $"{expectedWidth}x{expectedHeight}.");

            var matchSize = new CvSize(MatchWidth, MatchWidth * TileHeight / TileWidth);
            var histograms = new Mat[cardCount + 1];
            var smallTiles = new Mat[cardCount + 1];

            for (var id = 1; id <= cardCount; id++)
            {
                var rect = TileFor(id).ToCvRect();
                using var tile = new Mat(sheet, rect);
                histograms[id] = Histogram(tile);

                var small = new Mat();
                Cv2.Resize(tile, small, matchSize, interpolation: InterpolationFlags.Area);
                smallTiles[id] = small;
            }

            return new CardArtLibrary(sheet, histograms, smallTiles, matchSize);
        }
        catch
        {
            sheet.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Find the card whose art best matches a captured region. The region should roughly
    /// bracket one card — a whole hand-card slot is fine, no need to trim it down to just the
    /// artwork first, since the alignment search finds the art's position within it.
    /// </summary>
    public ArtMatch Match(Bitmap capturedRegion)
    {
        using var region3 = ToBgr(capturedRegion);
        using var regionHist = Histogram(region3);

        var shortlist = new List<(int Id, double Score)>(_histograms.Length - 1);
        for (var id = 1; id < _histograms.Length; id++)
            shortlist.Add((id, Cv2.CompareHist(regionHist, _histograms[id], HistCompMethods.Correl)));
        shortlist.Sort((a, b) => b.Score.CompareTo(a.Score));

        // Downsample once at the region's own aspect ratio, at the same pixel density as the
        // reference tiles, so the template search below compares like-for-like blur and scale.
        var scale = (double)MatchWidth / TileWidth;
        var smallRegionSize = new CvSize(
            Math.Max(_matchSize.Width, (int)Math.Round(region3.Width * scale)),
            Math.Max(_matchSize.Height, (int)Math.Round(region3.Height * scale)));
        using var smallRegion = new Mat();
        Cv2.Resize(region3, smallRegion, smallRegionSize, interpolation: InterpolationFlags.Area);

        var bestId = 0;
        var bestScore = double.NegativeInfinity;
        var bestLoc = new OpenCvSharp.Point();
        var secondId = 0;
        var secondScore = double.NegativeInfinity;

        using var result = new Mat();
        for (var i = 0; i < Math.Min(ShortlistSize, shortlist.Count); i++)
        {
            var id = shortlist[i].Id;
            Cv2.MatchTemplate(smallRegion, _smallTiles[id], result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double score, out _, out OpenCvSharp.Point loc);

            if (score > bestScore)
            {
                secondId = bestId; secondScore = bestScore;
                bestId = id; bestScore = score; bestLoc = loc;
            }
            else if (score > secondScore)
            {
                secondId = id; secondScore = score;
            }
        }

        // Map the match back from downsampled search-image pixels to the caller's own pixels.
        var backScale = region3.Width / (double)smallRegion.Width;
        var matched = new Rectangle(
            (int)Math.Round(bestLoc.X * backScale),
            (int)Math.Round(bestLoc.Y * backScale),
            (int)Math.Round(TileWidth * backScale * scale),
            (int)Math.Round(TileHeight * backScale * scale));

        return new ArtMatch(bestId, bestScore, secondId,
            double.IsNegativeInfinity(secondScore) ? 0 : secondScore, matched);
    }

    /// <summary>
    /// True when a region held no discernible detail — a flat, or nearly flat, expanse — so
    /// matching it is pointless. Measured as color spread across the whole region; a real card
    /// icon has far more variation than this even under heavy JPEG-like compression.
    /// </summary>
    public static bool LooksEmpty(Bitmap capturedRegion)
    {
        using var region3 = ToBgr(capturedRegion);
        Cv2.MeanStdDev(region3, out _, out Scalar stdDev);
        var spread = (stdDev.Val0 + stdDev.Val1 + stdDev.Val2) / 3;
        return spread < 6.0;
    }

    /// <summary>
    /// A copy of one card's reference artwork. Used only to write comparison images out for
    /// the user to look at, which is how a recognition mistake actually gets diagnosed.
    /// </summary>
    public Bitmap Tile(int cardId)
    {
        using var tile = new Mat(_sheet, TileFor(cardId).ToCvRect());
        return BitmapConverter.ToBitmap(tile);
    }

    /// <summary>
    /// A .NET <see cref="Bitmap"/> converts to a 4-channel BGRA <see cref="Mat"/>; the reference
    /// tiles were loaded 3-channel (no alpha), so callers need a matching 3-channel image
    /// before comparing the two.
    /// </summary>
    private static Mat ToBgr(Bitmap bitmap)
    {
        using var raw = BitmapConverter.ToMat(bitmap);
        if (raw.Channels() == 3) return raw.Clone();

        var bgr = new Mat();
        Cv2.CvtColor(raw, bgr, ColorConversionCodes.BGRA2BGR);
        return bgr;
    }

    private static Mat Histogram(Mat bgr)
    {
        var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var hist = new Mat();
        Cv2.CalcHist([hsv], [0, 1], null, hist, 2, [30, 32], [new Rangef(0, 180), new Rangef(0, 256)]);
        Cv2.Normalize(hist, hist, 0, 1, NormTypes.MinMax);
        hsv.Dispose();
        return hist;
    }

    public void Dispose()
    {
        _sheet.Dispose();
        foreach (var h in _histograms) h?.Dispose();
        foreach (var t in _smallTiles) t?.Dispose();
    }
}

internal static class RectangleExtensions
{
    public static Rect ToCvRect(this Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
