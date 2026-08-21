using System.Drawing;
using System.Drawing.Drawing2D;
using YgoFm.Vision;

namespace YgoFm.App;

/// <summary>
/// Draws the slot and artwork boxes onto a copy of the observed frame, so the live preview is
/// itself the verification aid — the CLAUDE.md habit of always having a way to look at what the
/// code actually cut out, applied to a continuously updating capture instead of a one-off export.
/// </summary>
internal static class PreviewAnnotator
{
    private static readonly Color EmptyColor = Color.FromArgb(160, 160, 160);
    private static readonly Color UncertainColor = Color.FromArgb(255, 193, 7);
    private static readonly Color ConfidentColor = Color.FromArgb(76, 217, 100);

    public static Bitmap Annotate(Bitmap frame, IReadOnlyList<SlotReading> readings)
    {
        var annotated = new Bitmap(frame);
        using var g = Graphics.FromImage(annotated);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", Math.Max(8f, frame.Height / 22f), FontStyle.Bold);

        foreach (var reading in readings)
        {
            var color = reading.Verdict switch
            {
                SlotVerdict.Confident => ConfidentColor,
                SlotVerdict.Uncertain => UncertainColor,
                _ => EmptyColor,
            };

            using var pen = new Pen(color, 2);
            g.DrawRectangle(pen, reading.SlotBounds);
            using var dashed = new Pen(color, 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dashed, reading.ArtBounds);

            var label = reading.Verdict == SlotVerdict.Empty ? $"{reading.Slot}" : $"{reading.Slot}: {reading.Card?.Name ?? "?"}";
            var textPos = new PointF(reading.SlotBounds.X + 2, reading.SlotBounds.Bottom - font.Height - 2);
            using var backdrop = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            var size = g.MeasureString(label, font);
            g.FillRectangle(backdrop, textPos.X - 1, textPos.Y, size.Width + 2, size.Height);
            using var textBrush = new SolidBrush(color);
            g.DrawString(label, font, textBrush, textPos);
        }

        return annotated;
    }
}
