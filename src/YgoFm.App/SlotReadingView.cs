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

    public string ScoreLabel => reading.Verdict == SlotVerdict.Empty
        ? ""
        : $"{reading.Score:0.00} (Δ{reading.Margin:0.00})";
}
