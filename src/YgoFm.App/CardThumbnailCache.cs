using System.Windows.Media.Imaging;
using YgoFm.Vision;

namespace YgoFm.App;

/// <summary>
/// Small pictures of card art for the fusion table, keyed by card id.
///
/// Two design choices worth understanding:
///
/// 1. The artwork always comes from the official reference sheet (<see cref="CardArtLibrary"/>),
///    never from the personal taught library. This is purely illustrative — a person reading the
///    table just wants to recognise the card by eye — so it must never fail to have a picture.
///    The taught library only has entries for cards that happen to have been seen this session;
///    the official one covers all 722 unconditionally. Using the taught library here would mean
///    some rows silently having no picture until that card was played once, for no benefit.
///
/// 2. The cache exists because a WPF <c>Image</c> control's <c>Source</c> binding re-reads
///    whatever value is on the view-model every time the row is (re)rendered — which happens a
///    lot: the fusion table is rebuilt from scratch every ~2 second tick, and the same handful
///    of cards (whatever is in the hand right now, plus common fusion results) reappear across
///    many rows. Without caching, every one of those repeats would redo the whole conversion in
///    <see cref="Get"/>: slice the tile out of the sheet Bitmap, then re-encode/decode it through
///    a PNG round trip (see <c>BitmapInterop.ToBitmapSource</c>) just to get a WPF-usable image.
///    Keying by card id and keeping the result means that work happens at most once per card,
///    ever, for the lifetime of the window.
/// </summary>
public sealed class CardThumbnailCache(CardArtLibrary art)
{
    private readonly Dictionary<int, BitmapSource> _cache = [];

    public BitmapSource Get(int cardId)
    {
        if (_cache.TryGetValue(cardId, out var cached)) return cached;

        using var tile = art.Tile(cardId);

        // BitmapInterop.ToBitmapSource() calls Freeze() on the result. A frozen WPF Freezable
        // becomes immutable and, critically, thread-safe to read from any thread — which matters
        // here because Get() is first called from the background thread that builds the fusion
        // rows (see MainWindow.CaptureAndReadCoreAsync), while the UI thread later reads the same
        // cached BitmapSource back out through data binding. An unfrozen BitmapSource is tied to
        // the thread that created it and would throw if touched from another one.
        var source = tile.ToBitmapSource();
        _cache[cardId] = source;
        return source;
    }
}
