using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using YgoFm.Vision;
// See BitmapInterop.cs for why System.Drawing is aliased rather than imported wholesale.
using DrawingBitmap = System.Drawing.Bitmap;
using ShapeRectangle = System.Windows.Shapes.Rectangle;
using WpfPoint = System.Windows.Point;

namespace YgoFm.App;

/// <summary>
/// Lets the user drag a box over a snapshot of the emulator to mark some region of it — the
/// hand-card row, or the card-name panel; the window does not care which, the caller supplies
/// the title and instructions to match.
///
/// Selecting on a still snapshot shown inside our own window, rather than on a transparent
/// overlay dragged across the real screen, sidesteps per-monitor DPI placement math entirely —
/// the drag happens in an Image control we already own, in the same per-monitor-aware window
/// as everything else. The result is expressed as proportions of the snapshot
/// (<see cref="NormRect"/>), so it still survives the emulator window being moved or resized
/// afterwards, exactly like the viewport/region split the capture layer already uses.
/// </summary>
public partial class SelectRegionWindow : Window
{
    private readonly DrawingBitmap _snapshot;
    private WpfPoint? _dragStart;
    private ShapeRectangle? _preview;

    public NormRect? Selection { get; private set; }

    public SelectRegionWindow(DrawingBitmap snapshot, string title, string instructions)
    {
        InitializeComponent();
        _snapshot = snapshot;
        Snapshot.Source = snapshot.ToBitmapSource();
        Title = title;
        InstructionsText.Text = instructions;
    }

    // ------------------------------------------------------------ mapping control <-> bitmap

    /// <summary>Where the snapshot is actually drawn inside the stage, given Stretch="Uniform".</summary>
    private Rect DisplayedImageRect()
    {
        var boxW = Stage.ActualWidth;
        var boxH = Stage.ActualHeight;
        if (boxW <= 0 || boxH <= 0) return Rect.Empty;

        var scale = Math.Min(boxW / _snapshot.Width, boxH / _snapshot.Height);
        var w = _snapshot.Width * scale;
        var h = _snapshot.Height * scale;
        return new Rect((boxW - w) / 2, (boxH - h) / 2, w, h);
    }

    /// <summary>A point on the overlay, clamped into the displayed image and expressed as
    /// 0..1 proportions of the snapshot.</summary>
    private WpfPoint ToFraction(WpfPoint p)
    {
        var box = DisplayedImageRect();
        if (box.Width <= 0 || box.Height <= 0) return new WpfPoint(0, 0);

        var x = Math.Clamp((p.X - box.X) / box.Width, 0, 1);
        var y = Math.Clamp((p.Y - box.Y) / box.Height, 0, 1);
        return new WpfPoint(x, y);
    }

    private static Rect Normalise(WpfPoint a, WpfPoint b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    // ------------------------------------------------------------ dragging

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Starting a fresh drag replaces whatever was marked before, so the overlay never
        // shows two boxes at once.
        Overlay.Children.Clear();

        _dragStart = e.GetPosition(Overlay);

        _preview = new ShapeRectangle
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(50, 0, 255, 0)),
        };
        Overlay.Children.Add(_preview);
        Overlay.CaptureMouse();
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _preview is null) return;

        var box = Normalise(start, e.GetPosition(Overlay));
        System.Windows.Controls.Canvas.SetLeft(_preview, box.X);
        System.Windows.Controls.Canvas.SetTop(_preview, box.Y);
        _preview.Width = box.Width;
        _preview.Height = box.Height;

        var f0 = ToFraction(new WpfPoint(box.Left, box.Top));
        var f1 = ToFraction(new WpfPoint(box.Right, box.Bottom));
        StatusText.Text = $"{(f1.X - f0.X) * 100:0}% × {(f1.Y - f0.Y) * 100:0}% da imagem capturada.";
    }

    private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start) return;

        Overlay.ReleaseMouseCapture();
        var box = Normalise(start, e.GetPosition(Overlay));
        _dragStart = null;

        if (box.Width < 4 || box.Height < 4)
        {
            Overlay.Children.Clear();
            _preview = null;
            StatusText.Text = "Seleção pequena demais — arraste um retângulo maior.";
            Selection = null;
            return;
        }

        // Left in place rather than removed, so the user can see exactly what was marked
        // before deciding to confirm it.
        if (_preview is not null)
        {
            _preview.Stroke = Brushes.Lime;
            _preview.StrokeThickness = 2;
        }

        var f0 = ToFraction(new WpfPoint(box.Left, box.Top));
        var f1 = ToFraction(new WpfPoint(box.Right, box.Bottom));
        Selection = new NormRect(f0.X, f0.Y, Math.Max(f1.X - f0.X, 0.01), Math.Max(f1.Y - f0.Y, 0.01));
        StatusText.Text = "Região marcada. Clique em Confirmar, ou arraste de novo para ajustar.";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (Selection is null)
        {
            MessageBox.Show(this, "Arraste um retângulo antes de confirmar.",
                "Nenhuma região selecionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
