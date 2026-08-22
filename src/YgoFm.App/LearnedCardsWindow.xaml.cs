using System.Windows;
using System.Windows.Media.Imaging;
using YgoFm.Core;
using YgoFm.Vision;
// See BitmapInterop.cs for why System.Drawing is aliased rather than imported wholesale.
using DrawingBitmap = System.Drawing.Bitmap;

namespace YgoFm.App;

/// <summary>
/// Shows exactly what the personal template library has learned so far — deliberately the
/// actual captured crop for each card (loaded straight off <see cref="TaughtCardLibrary.PathFor"/>),
/// not the official art from <see cref="CardThumbnailCache"/> that the fusion table uses. The
/// whole point of this window is to let a bad lesson be caught by eye (a card taught from a
/// transition frame would show up as an obviously wrong picture here), which the official art
/// could never reveal — it always looks "correct" regardless of what was actually learned.
/// </summary>
public partial class LearnedCardsWindow : Window
{
    /// <summary>One row's worth of data, built once in the constructor rather than exposed as a
    /// live view over <see cref="TaughtCardLibrary"/> — this window shows a snapshot of the
    /// learned set at the moment it was opened; it does not need to update while it's open.</summary>
    public sealed record Row(int Id, string Name, BitmapSource Image);

    public LearnedCardsWindow(CardDatabase cards, TaughtCardLibrary taught)
    {
        InitializeComponent();

        var rows = taught.LearnedCardIds
            .OrderBy(id => id)
            .Select(id =>
            {
                // Loaded directly from disk rather than through TaughtCardLibrary.Match (which
                // only answers "what does this look like closest to", not "give me card N's
                // picture"). The `using` disposes the System.Drawing.Bitmap right after
                // ToBitmapSource() copies its pixels into a WPF-native BitmapSource — nothing
                // downstream needs the original GDI+ object once that conversion is done.
                using DrawingBitmap bitmap = new(taught.PathFor(id));
                return new Row(id, cards[id].Name, bitmap.ToBitmapSource());
            })
            .ToList();

        // ItemsSource takes a plain List here rather than an ObservableCollection, unlike
        // MainWindow's tables — those refresh their contents every tick and need the UI to
        // react to that; this window's content is fixed for its whole lifetime.
        CardsList.ItemsSource = rows;
        SummaryText.Text = $"{rows.Count} de {cards.Count} cartas aprendidas nesta máquina.";
    }
}
