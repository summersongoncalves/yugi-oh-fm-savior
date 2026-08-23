using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YgoFm.Vision;
// See BitmapInterop.cs for why System.Drawing is aliased rather than imported wholesale.
using DrawingBitmap = System.Drawing.Bitmap;
using ShapeRectangle = System.Windows.Shapes.Rectangle;
using WpfPoint = System.Windows.Point;

namespace YgoFm.App;

/// <summary>
/// Lets the user drag a box over a snapshot of the emulator to mark one or more regions in a
/// single continuous session — the hand-card row, then the card-name panel, without closing and
/// reopening a window between them. A label follows the cursor naming whichever region is
/// currently being marked; each finished box freezes in place (in a different colour) so earlier
/// regions stay visible as later ones are marked. The moment the last region lands, a yes/no
/// prompt (see <see cref="PromptFinishOrRedo"/>) asks whether to proceed or throw everything out
/// and redo the whole pass — there is no state where the user is left free-dragging extra boxes
/// with nothing left for them to mean.
///
/// Selecting on a still snapshot shown inside our own window, rather than on a transparent
/// overlay dragged across the real screen, sidesteps per-monitor DPI placement math entirely —
/// the drag happens in an Image control we already own, in the same per-monitor-aware window
/// as everything else. Each result is expressed as proportions of the snapshot
/// (<see cref="NormRect"/>), so it still survives the emulator window being moved or resized
/// afterwards, exactly like the viewport/region split the capture layer already uses.
/// </summary>
public partial class SelectRegionWindow : Window
{
    /// <summary>One region to mark, named by the label shown next to the cursor while marking it.</summary>
    public sealed record Stage(string Label);

    private readonly DrawingBitmap _snapshot;
    private readonly IReadOnlyList<Stage> _stages;
    private readonly NormRect?[] _selections;
    private int _stageIndex;

    private WpfPoint? _dragStart;
    private ShapeRectangle? _preview;

    private readonly Border _cursorLabel;
    private readonly TextBlock _cursorLabelText;

    /// <summary>The confirmed regions, in the same order as the stages passed to the constructor.
    /// Only meaningful once the dialog returns true.</summary>
    public IReadOnlyList<NormRect> Selections => _selections.Select(s => s!).ToArray();

    public SelectRegionWindow(DrawingBitmap snapshot, string title, IReadOnlyList<Stage> stages)
    {
        if (stages.Count == 0) throw new ArgumentException("Need at least one stage.", nameof(stages));

        InitializeComponent();
        _snapshot = snapshot;
        Snapshot.Source = snapshot.ToBitmapSource();
        Title = title;
        _stages = stages;
        _selections = new NormRect?[stages.Count];

        _cursorLabelText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
        };
        _cursorLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Child = _cursorLabelText,
            IsHitTestVisible = false, // must never steal the drag's mouse events
        };
        Overlay.Children.Add(_cursorLabel);
        Canvas.SetLeft(_cursorLabel, -1000); // off-screen until the mouse actually enters

        UpdateStageStatus();
    }

    /// <summary>
    /// Only ever called with a stage still pending — <see cref="Overlay_MouseUp"/> calls
    /// <see cref="PromptFinishOrRedo"/> instead of this the moment the last stage completes, so
    /// there is no "all done, label blank" state for this to describe any more.
    /// </summary>
    private void UpdateStageStatus()
    {
        _cursorLabelText.Text = _stages[_stageIndex].Label;
        StatusText.Text = $"Passo {_stageIndex + 1} de {_stages.Count}.";
    }

    // ------------------------------------------------------------ mapping control <-> bitmap

    /// <summary>Where the snapshot is actually drawn inside the stage, given Stretch="Uniform".</summary>
    private Rect DisplayedImageRect()
    {
        var boxW = StageGrid.ActualWidth;
        var boxH = StageGrid.ActualHeight;
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
        // Once every stage has a box, dragging is over — PromptFinishOrRedo (called the moment
        // the last stage's box lands, see Overlay_MouseUp) either closes the window or resets
        // everything for a redo. Ignoring further mouse-downs here means there is never a state
        // where the user can draw extra, unwired boxes while that decision is pending.
        if (_stageIndex >= _stages.Count) return;

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
        var pos = e.GetPosition(Overlay);

        // The label rides along with the cursor regardless of whether a drag is in progress,
        // so it is always obvious which region is being asked for next.
        Canvas.SetLeft(_cursorLabel, pos.X + 18);
        Canvas.SetTop(_cursorLabel, pos.Y + 18);

        if (_dragStart is not { } start || _preview is null) return;

        var box = Normalise(start, pos);
        Canvas.SetLeft(_preview, box.X);
        Canvas.SetTop(_preview, box.Y);
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
            if (_preview is not null) { Overlay.Children.Remove(_preview); _preview = null; }
            StatusText.Text = "Seleção pequena demais — arraste um retângulo maior.";
            return;
        }

        var f0 = ToFraction(new WpfPoint(box.Left, box.Top));
        var f1 = ToFraction(new WpfPoint(box.Right, box.Bottom));
        var selection = new NormRect(f0.X, f0.Y, Math.Max(f1.X - f0.X, 0.01), Math.Max(f1.Y - f0.Y, 0.01));

        // Freeze this stage's box in a different colour than the live drag preview, so it
        // stays legible as a "done" marker while the next region is dragged over it.
        _preview!.Stroke = Brushes.Cyan;
        _preview.StrokeThickness = 2;
        _preview = null;

        _selections[_stageIndex] = selection;
        _stageIndex++;

        if (_stageIndex == _stages.Count)
            PromptFinishOrRedo();
        else
            UpdateStageStatus();
    }

    /// <summary>
    /// Fires the instant the last stage's box lands — asks in one step whether to run with these
    /// regions or throw them all out and redo the whole marking pass. Replaces what used to
    /// happen here: the window just sat there letting the user draw more, unwired boxes with no
    /// clear purpose, and the cursor label went blank (nothing left to ask for) with no
    /// indication of what to do next.
    /// </summary>
    private void PromptFinishOrRedo()
    {
        StatusText.Text = "Todas as regiões marcadas.";

        var result = MessageBox.Show(this,
            "Você selecionou as duas áreas corretamente?",
            "Confirmar seleção", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            DialogResult = true;
            return;
        }

        // Redo: throw away every committed box and start the stage sequence over from zero.
        foreach (var rectangle in Overlay.Children.OfType<ShapeRectangle>().ToList())
            Overlay.Children.Remove(rectangle);

        Array.Clear(_selections);
        _stageIndex = 0;
        UpdateStageStatus();
    }

    /// <summary>
    /// Shows a small schematic of where the two regions go, for the "ℹ" button next to the
    /// instructions. Built as one plain Window here rather than a second .xaml file — its whole
    /// content is a single Image, so a dedicated XAML file would be more ceremony than the thing
    /// it displays. The picture is loaded from a "pack://application:,,," URI, which is how WPF
    /// addresses a file that was compiled into the assembly as a Resource (see the
    /// Resource Include in YgoFm.App.csproj) rather than one copied alongside the .exe on disk —
    /// that is what lets this work regardless of the app's current working directory.
    /// </summary>
    private void ShowGuide_Click(object sender, RoutedEventArgs e)
    {
        var image = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/region-guide.png")),
            Stretch = Stretch.Uniform,
        };

        new Window
        {
            Title = "Exemplo de marcação",
            Content = image,
            Width = 620,
            Height = 400,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
        }.ShowDialog();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_stageIndex < _stages.Count)
        {
            MessageBox.Show(this, $"Ainda falta marcar: {_stages[_stageIndex].Label}",
                "Faltam regiões", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Reachable only if PromptFinishOrRedo somehow didn't already run (defensive, not
        // expected in normal use) — same yes/no decision either way.
        PromptFinishOrRedo();
    }
}
