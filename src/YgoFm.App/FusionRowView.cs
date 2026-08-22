using System.Windows.Media.Imaging;
using YgoFm.Core;

namespace YgoFm.App;

/// <summary>
/// Display-friendly wrapper around one <see cref="FusionFinder.FusionChain"/> — the layer that
/// turns raw domain data (which cards, in which order, producing what) into the individual
/// pieces a WPF <c>DataTemplate</c> can bind to by name (see MainWindow.xaml's "Carta 1".."Carta
/// 5"/"Resultado" columns, each binding to one <c>ImageN</c>/<c>CardN</c> pair here). WPF's
/// binding system finds these through reflection at runtime by property name, which is why each
/// column needs its own named property rather than, say, an indexer or a list — {Binding Image1}
/// only works because a public <c>Image1</c> getter exists to be found.
///
/// The table shows a fixed five material columns (the largest a hand can ever have) rather than
/// a dynamically-sized set, since WPF's GridView does not make variable column counts worth the
/// complexity here — a two-material chain just leaves the later columns' bindings resolve to
/// null/"", which XAML already renders as blank.
/// </summary>
public sealed class FusionRowView(FusionFinder.FusionChain chain, CardThumbnailCache thumbnails)
{
    private Card? MaterialAt(int index) => index < chain.Materials.Count ? chain.Materials[index] : null;

    // Kept as text (shown under the picture and as its tooltip in the XAML) so the artwork is
    // never the only way to tell two similarly-coloured cards apart — a real failure mode
    // encountered while building the recogniser itself (see CardArtLibrary's history).
    public string Card1 => MaterialAt(0)?.Name ?? "";
    public string Card2 => MaterialAt(1)?.Name ?? "";
    public string Card3 => MaterialAt(2)?.Name ?? "";
    public string Card4 => MaterialAt(3)?.Name ?? "";
    public string Card5 => MaterialAt(4)?.Name ?? "";
    public string Result => chain.Result.Name;

    // Each pulls its BitmapSource from the shared CardThumbnailCache rather than converting the
    // art itself — see that class for why (mainly: the same card recurs across many rows every
    // tick, and the conversion is not free).
    public BitmapSource? Image1 => MaterialAt(0) is { } c ? thumbnails.Get(c.Id) : null;
    public BitmapSource? Image2 => MaterialAt(1) is { } c ? thumbnails.Get(c.Id) : null;
    public BitmapSource? Image3 => MaterialAt(2) is { } c ? thumbnails.Get(c.Id) : null;
    public BitmapSource? Image4 => MaterialAt(3) is { } c ? thumbnails.Get(c.Id) : null;
    public BitmapSource? Image5 => MaterialAt(4) is { } c ? thumbnails.Get(c.Id) : null;
    public BitmapSource ImageResult => thumbnails.Get(chain.Result.Id);
}
