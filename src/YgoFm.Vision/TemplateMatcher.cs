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
/// Where in the search image the winning card's art was found, and at what size, in the search
/// image's own pixels. Found by the same position-and-scale search that picked the card, so
/// good enough to outline for the user, but downstream of a coarse downsampled search — not
/// meant for anything more precise.
/// </param>
public sealed record ArtMatch(int CardId, double Score, int RunnerUpId, double RunnerUpScore, Rectangle MatchedRegion)
{
    /// <summary>How far ahead the winner is over the runner-up.</summary>
    public double Margin => Score - RunnerUpScore;
}

/// <summary>
/// The reusable "which of these reference tiles matches this captured region" search: a cheap
/// colour-histogram comparison narrows every registered tile down to a shortlist, then OpenCV
/// template matching — tried at several scales, since how large the on-screen art renders
/// relative to the caller's box is not known up front — searches that shortlist for the best
/// alignment.
///
/// Both stages downsample before comparing, using the *search region's own* aspect ratio rather
/// than the tile's. Both of those were measured to matter against real captures: an emulator's
/// upscaling blur otherwise tanks pixel-level correlation even for the correct card (sharp
/// reference art and a blurred capture of the same picture do not correlate well pixel-for-
/// pixel), and assuming the region shares the tile's proportions broke outright on a selection
/// that was shorter/wider than the reference art — the correct card's score collapsed to near
/// zero because the template had no room to search vertically at all.
///
/// Extracted out of card-vs-official-art matching so it can be reused unchanged for matching
/// against a personal, grown-on-the-user's-own-machine template set (see
/// <see cref="TaughtCardLibrary"/>) — the search itself does not care where its tiles came from.
/// </summary>
internal sealed class TemplateMatcher : IDisposable
{
    /// <summary>How many tiles the histogram stage keeps for the expensive alignment search.</summary>
    private const int ShortlistSize = 120;

    /// <summary>Height the search region is downsampled to before the alignment search.</summary>
    private const int RegionMatchHeight = 32;

    /// <summary>
    /// Fractions of the downsampled region's height the template is tried at. Measured
    /// empirically: too narrow a set missed the right scale entirely; this range covers what
    /// real captures have needed so far.
    /// </summary>
    private static readonly double[] ScaleFractions = [0.55, 0.65, 0.75, 0.85, 0.95];

    private readonly Dictionary<int, Mat> _tiles = [];
    private readonly Dictionary<int, Mat> _histograms = [];

    public int Count => _tiles.Count;

    public IReadOnlyCollection<int> Ids => _tiles.Keys;

    /// <summary>
    /// Register (or replace) the reference tile for one id. Takes ownership of
    /// <paramref name="tileBgr"/> — the caller should not dispose it afterwards.
    /// </summary>
    public void Set(int id, Mat tileBgr)
    {
        if (_tiles.Remove(id, out var oldTile)) oldTile.Dispose();
        if (_histograms.Remove(id, out var oldHist)) oldHist.Dispose();

        _tiles[id] = tileBgr;
        _histograms[id] = Histogram(tileBgr);
    }

    public void Remove(int id)
    {
        if (_tiles.Remove(id, out var tile)) tile.Dispose();
        if (_histograms.Remove(id, out var hist)) hist.Dispose();
    }

    /// <summary>
    /// Find the registered tile that best matches a captured region, or null if nothing is
    /// registered yet. The region should roughly bracket one card — a whole hand-card slot is
    /// fine, no need to trim it down to just the artwork first, since the alignment search finds
    /// the art's position within it.
    /// </summary>
    public ArtMatch? Match(Bitmap capturedRegion)
    {
        if (_tiles.Count == 0) return null;

        using var region3 = ToBgr(capturedRegion);
        using var regionHist = Histogram(region3);

        var shortlist = new List<(int Id, double Score)>(_histograms.Count);
        foreach (var (id, hist) in _histograms)
            shortlist.Add((id, Cv2.CompareHist(regionHist, hist, HistCompMethods.Correl)));
        shortlist.Sort((a, b) => b.Score.CompareTo(a.Score));

        var regionMatchWidth = Math.Max(8, region3.Width * RegionMatchHeight / region3.Height);
        using var smallRegion = new Mat();
        Cv2.Resize(region3, smallRegion, new CvSize(regionMatchWidth, RegionMatchHeight),
            interpolation: InterpolationFlags.Area);

        var bestId = 0;
        var bestScore = double.NegativeInfinity;
        var bestLoc = new OpenCvSharp.Point();
        var bestSize = new CvSize(1, 1);
        var secondId = 0;
        var secondScore = double.NegativeInfinity;

        using var result = new Mat();
        for (var i = 0; i < Math.Min(ShortlistSize, shortlist.Count); i++)
        {
            var id = shortlist[i].Id;
            var tile = _tiles[id];

            var candidateScore = double.NegativeInfinity;
            var candidateLoc = new OpenCvSharp.Point();
            var candidateSize = new CvSize(1, 1);

            foreach (var fraction in ScaleFractions)
            {
                var h = Math.Max(6, (int)Math.Round(RegionMatchHeight * fraction));
                var w = Math.Max(6, h * tile.Width / tile.Height);
                if (w >= smallRegion.Width || h >= smallRegion.Height) continue;

                using var smallTile = new Mat();
                Cv2.Resize(tile, smallTile, new CvSize(w, h), interpolation: InterpolationFlags.Area);
                Cv2.MatchTemplate(smallRegion, smallTile, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double score, out _, out OpenCvSharp.Point loc);

                if (score > candidateScore)
                {
                    candidateScore = score;
                    candidateLoc = loc;
                    candidateSize = new CvSize(w, h);
                }
            }

            if (candidateScore > bestScore)
            {
                secondId = bestId; secondScore = bestScore;
                bestId = id; bestScore = candidateScore; bestLoc = candidateLoc; bestSize = candidateSize;
            }
            else if (candidateScore > secondScore)
            {
                secondId = id; secondScore = candidateScore;
            }
        }

        var backScale = region3.Width / (double)smallRegion.Width;
        var matched = new Rectangle(
            (int)Math.Round(bestLoc.X * backScale),
            (int)Math.Round(bestLoc.Y * backScale),
            (int)Math.Round(bestSize.Width * backScale),
            (int)Math.Round(bestSize.Height * backScale));

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
    /// A .NET <see cref="Bitmap"/> converts to a 4-channel BGRA <see cref="Mat"/>; tiles are
    /// kept 3-channel (no alpha), so callers need a matching 3-channel image before comparing.
    /// </summary>
    public static Mat ToBgr(Bitmap bitmap)
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
        foreach (var t in _tiles.Values) t.Dispose();
        foreach (var h in _histograms.Values) h.Dispose();
    }
}
