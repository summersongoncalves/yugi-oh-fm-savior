using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace YgoFm.Vision;

/// <summary>
/// The reference artwork for all 722 cards, sliced out of the game's own card sheet, matched via
/// the shared <see cref="TemplateMatcher"/> search (histogram shortlist, then multi-scale
/// template matching — see that class for why).
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

    private readonly Mat _sheet;
    private readonly TemplateMatcher _matcher;

    private CardArtLibrary(Mat sheet, TemplateMatcher matcher)
    {
        _sheet = sheet;
        _matcher = matcher;
    }

    public int Count => _matcher.Count;

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

        var matcher = new TemplateMatcher();
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

            for (var id = 1; id <= cardCount; id++)
                matcher.Set(id, new Mat(sheet, TileFor(id).ToCvRect()).Clone());

            return new CardArtLibrary(sheet, matcher);
        }
        catch
        {
            sheet.Dispose();
            matcher.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Find the card whose art best matches a captured region. The region should roughly
    /// bracket one card — a whole hand-card slot is fine, no need to trim it down to just the
    /// artwork first, since the alignment search finds the art's position within it.
    /// </summary>
    public ArtMatch Match(Bitmap capturedRegion) => _matcher.Match(capturedRegion)!;

    /// <summary>
    /// True when a region held no discernible detail — a flat, or nearly flat, expanse — so
    /// matching it is pointless.
    /// </summary>
    public static bool LooksEmpty(Bitmap capturedRegion) => TemplateMatcher.LooksEmpty(capturedRegion);

    /// <summary>
    /// A copy of one card's reference artwork. Used only to write comparison images out for
    /// the user to look at, which is how a recognition mistake actually gets diagnosed.
    /// </summary>
    public Bitmap Tile(int cardId)
    {
        using var tile = new Mat(_sheet, TileFor(cardId).ToCvRect());
        return BitmapConverter.ToBitmap(tile);
    }

    public void Dispose()
    {
        _sheet.Dispose();
        _matcher.Dispose();
    }
}

internal static class RectangleExtensions
{
    public static Rect ToCvRect(this Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
