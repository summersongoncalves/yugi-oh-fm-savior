using System.ComponentModel;
using System.Drawing;

namespace YgoFm.Calibrator;

/// <summary>One line in the checklist of regions the user still has to draw.</summary>
public sealed class RegionRow(string name) : INotifyPropertyChanged
{
    private Rectangle? _rect;

    public string Name { get; } = name;

    /// <summary>The drawn box, in pixels of the captured frame. Null until the user draws it.</summary>
    public Rectangle? Rect
    {
        get => _rect;
        set
        {
            _rect = value;
            Notify(nameof(Rect));
            Notify(nameof(Marker));
            Notify(nameof(Detail));
        }
    }

    public string Marker => _rect is null ? "○" : "✓";

    public string Detail => _rect is { } r
        ? $"{r.X}, {r.Y}   {r.Width} × {r.Height} px"
        : "not set";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
