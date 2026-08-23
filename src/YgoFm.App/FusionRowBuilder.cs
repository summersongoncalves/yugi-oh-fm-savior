using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YgoFm.Core;

namespace YgoFm.App;

/// <summary>
/// Builds one fusion chain's row as a plain WPF <see cref="UIElement"/>, rather than through a
/// <c>GridView</c>/<c>DataTemplate</c> as the earlier table-shaped version of this UI did.
///
/// The reason is what the row actually needs to show: "carta + carta + ... = resultado", where
/// the number of "+ carta" segments varies per chain (a chain has 2 to 5 materials) and a bold
/// "+" or "=" glyph sits *between* tiles rather than belonging to any one of them. WPF's
/// declarative templating binds one fixed template per list item; it does not comfortably
/// express "a sequence whose length and separators vary per item" without extra machinery
/// (a value converter, a nested ItemsControl with its own DataTemplateSelector, and so on).
/// Building the row's element tree directly in code sidesteps all of that — a plain loop can
/// say exactly "for each material, add a tile, then a +, except swap the final one for =" in a
/// way XAML cannot as directly.
///
/// This works with <c>MainWindow.xaml</c>'s plain <c>&lt;ItemsControl x:Name="FusionsList" /&gt;</c>
/// (no <c>DataTemplate</c> declared there at all) because of a WPF-specific rule: when an item
/// added to an <c>ItemsControl</c> is already a <see cref="UIElement"/>, the control uses it
/// directly as that item's visual content instead of wrapping it in a default template. Handing
/// the control a list of ready-made <c>WrapPanel</c>s (one per chain, see <see cref="Build"/>)
/// is what lets each row just... be exactly the panel this class built, with nothing further.
/// </summary>
internal static class FusionRowBuilder
{
    /// <summary>Card art tiles are shown at double the size the old table used (42px), per the
    /// request that made this whole redesign happen — bigger tiles read more like "here is the
    /// card" and less like a dense spreadsheet cell.</summary>
    private const int TileSize = 84;

    public static UIElement Build(FusionFinder.FusionChain chain, CardThumbnailCache thumbnails)
    {
        var row = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 10),
            VerticalAlignment = VerticalAlignment.Center,
        };

        foreach (var material in chain.Materials)
        {
            row.Children.Add(CardTile(thumbnails.Get(material.Id), material.Name, stats: null));
            row.Children.Add(Glyph("+"));
        }

        // Every material got a trailing "+" above, including the last one — replace that final
        // separator with "=" rather than special-casing the loop to skip it on the last item,
        // which would need an index and an if-check for what is otherwise a plain foreach.
        row.Children.RemoveAt(row.Children.Count - 1);
        row.Children.Add(Glyph("="));

        // Only the result shows ATK/DEF: what a fusion produces is what actually ends up on the
        // field, so that card's stats are what matters here — the materials are just spent.
        row.Children.Add(CardTile(thumbnails.Get(chain.Result.Id), chain.Result.Name,
            $"ATQ {chain.Result.Attack} / DEF {chain.Result.Defense}"));

        return row;
    }

    private static UIElement CardTile(BitmapSource image, string name, string? stats)
    {
        var stack = new StackPanel { Margin = new Thickness(6, 0, 6, 0), Width = TileSize + 12, ToolTip = name };

        stack.Children.Add(new Image { Source = image, Height = TileSize, Stretch = Stretch.Uniform });
        stack.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        });

        if (stats is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = stats,
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.Gray,
            });
        }

        return stack;
    }

    private static UIElement Glyph(string symbol) => new TextBlock
    {
        Text = symbol,
        FontSize = 24,
        FontWeight = FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 4, 0),
    };
}
