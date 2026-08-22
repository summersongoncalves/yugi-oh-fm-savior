using System.Windows.Media;
using YgoFm.Vision;

namespace YgoFm.App;

/// <summary>Display-friendly wrapper around a <see cref="SlotReading"/> for the readings list —
/// keeps the formatting choices for "how a verdict reads to a human" out of YgoFm.Vision, which
/// has no business knowing about presentation.</summary>
public sealed class SlotReadingView(SlotReading reading)
{
    public int Slot => reading.Slot;

    public string CardLabel => reading.Verdict switch
    {
        SlotVerdict.Empty => "—",
        SlotVerdict.Uncertain => $"{reading.Card?.Name ?? "?"} (incerto)",
        SlotVerdict.Confident => reading.Card?.Name ?? "?",
        _ => "?",
    };

    public string VerdictLabel => reading.Verdict switch
    {
        SlotVerdict.Empty => "vazio",
        SlotVerdict.Uncertain => "incerto",
        SlotVerdict.Confident => "ok",
        _ => "",
    };

    /// <summary>
    /// Red/yellow/green at a glance, matching <see cref="VerdictLabel"/> — bound in the XAML as
    /// the "Confiança" column's <c>Foreground</c>. A WPF <c>TextBlock.Foreground</c> is typed as
    /// <see cref="Brush"/>, not <see cref="Color"/>, which is why this returns one of the named
    /// <see cref="Brushes"/> constants rather than a colour value directly.
    /// </summary>
    public Brush VerdictBrush => reading.Verdict switch
    {
        SlotVerdict.Confident => Brushes.ForestGreen,
        SlotVerdict.Uncertain => Brushes.DarkGoldenrod,
        SlotVerdict.Empty => Brushes.Firebrick,
        _ => Brushes.Gray,
    };

    public string ScoreLabel => reading.Verdict == SlotVerdict.Empty
        ? ""
        : $"{reading.Score:0.00} (Δ{reading.Margin:0.00})";

    /// <summary>Which library the match came from — useful while watching the taught library
    /// take over from official-art matching card by card.</summary>
    public string SourceLabel => reading.Source switch
    {
        MatchSource.Taught => "ensinada",
        MatchSource.Official => "oficial",
        _ => "",
    };
}
