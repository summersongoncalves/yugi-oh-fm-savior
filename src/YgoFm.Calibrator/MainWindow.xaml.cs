using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YgoFm.Vision;
// System.Drawing is not imported wholesale: its Brushes, Color and Rectangle would
// collide with the WPF types of the same name. The few we need are aliased instead.
using Bitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;
using ShapeRectangle = System.Windows.Shapes.Rectangle;
using WpfPoint = System.Windows.Point;

namespace YgoFm.Calibrator;

/// <summary>
/// The cut tool. Capture one frame from any emulator, drag a box around the game picture,
/// then drag a box around each card slot. Saves the result as proportions, so the same
/// calibration keeps working when the window is resized.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<RegionRow> _rows = [];
    private Bitmap? _frame;
    private double _zoom = 2.0;
    private WpfPoint? _dragStart;
    private ShapeRectangle? _preview;

    public MainWindow()
    {
        InitializeComponent();

        foreach (var name in RegionNames.CalibrationOrder)
            _rows.Add(new RegionRow(name));

        RegionListBox.ItemsSource = _rows;
        RegionListBox.SelectedIndex = 0;

        RefreshWindows();
    }

    private RegionRow? Selected => RegionListBox.SelectedItem as RegionRow;

    private RegionRow ViewportRow => _rows[0];

    // ---------------------------------------------------------------- capture

    private void OnRefreshWindows(object sender, RoutedEventArgs e) => RefreshWindows();

    private void RefreshWindows()
    {
        var windows = WindowFinder.Visible();
        WindowList.ItemsSource = windows;
        if (windows.Count > 0) WindowList.SelectedIndex = 0;
        Status($"{windows.Count} windows found. Likely emulators are listed first.");
    }

    private async void OnCaptureWindow(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowInfo target)
        {
            Status("Pick a window first.");
            return;
        }

        await CaptureAfterDelay(() =>
        {
            // We read pixels off the composited desktop, so the target has to be on top.
            ScreenCapture.BringToFront(target.Handle);
            Thread.Sleep(250);
            return ScreenCapture.CaptureWindow(target.Handle);
        }, target.Title);
    }

    private async void OnCaptureScreen(object sender, RoutedEventArgs e) =>
        await CaptureAfterDelay(() => ScreenCapture.CaptureScreen(), "whole screen");

    private async Task CaptureAfterDelay(Func<Bitmap> capture, string sourceName)
    {
        var seconds = int.Parse((string)((System.Windows.Controls.ComboBoxItem)DelayList.SelectedItem).Tag);

        for (var remaining = seconds; remaining > 0; remaining--)
        {
            Status($"Capturing {sourceName} in {remaining}…");
            await Task.Delay(1000);
        }

        try
        {
            var frame = await Task.Run(capture);
            Activate();
            SetFrame(frame, sourceName);
        }
        catch (Exception ex)
        {
            Status($"Capture failed: {ex.Message}");
        }
    }

    private void SetFrame(Bitmap frame, string sourceName)
    {
        _frame?.Dispose();
        _frame = frame;

        FrameImage.Source = ToBitmapSource(frame);
        ApplyZoom();

        if (ScreenCapture.LooksBlank(frame))
        {
            Status($"Captured {frame.Width}×{frame.Height} from {sourceName}, but the image is one flat colour — "
                 + "the graphics backend blocked the capture. Try the emulator's other renderer, or capture the screen instead.");
            return;
        }

        Status($"Captured {frame.Width}×{frame.Height} from {sourceName}. "
             + "Select a region on the right, then drag a box on the picture.");
    }

    // ---------------------------------------------------------------- drawing boxes

    private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_frame is null) { Status("Capture a frame first."); return; }
        if (Selected is null) { Status("Select which region you are drawing, on the right."); return; }

        Overlay.Focus();
        _dragStart = e.GetPosition(Overlay);

        _preview = new ShapeRectangle
        {
            Stroke = Brushes.Magenta,
            StrokeThickness = 1.5,
            StrokeDashArray = [3, 2],
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 0, 255))
        };

        Overlay.Children.Add(_preview);
        Overlay.CaptureMouse();
    }

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _preview is null) return;

        var current = e.GetPosition(Overlay);
        var box = Normalise(start, current);

        System.Windows.Controls.Canvas.SetLeft(_preview, box.X);
        System.Windows.Controls.Canvas.SetTop(_preview, box.Y);
        _preview.Width = box.Width;
        _preview.Height = box.Height;

        var inFrame = ToFramePixels(box);
        Status($"{Selected?.Name}: {inFrame.Width} × {inFrame.Height} px at {inFrame.X}, {inFrame.Y}");
    }

    private void OnOverlayMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start || _frame is null) return;

        Overlay.ReleaseMouseCapture();
        var box = Normalise(start, e.GetPosition(Overlay));
        _dragStart = null;

        if (_preview is not null)
        {
            Overlay.Children.Remove(_preview);
            _preview = null;
        }

        // Too small to be a deliberate drag — treat it as a stray click.
        if (box.Width < 4 || box.Height < 4) { RedrawOverlay(); return; }

        var row = Selected;
        if (row is null) return;

        row.Rect = ToFramePixels(box);
        AdvanceToNextUnset();
        RedrawOverlay();
        Status($"{row.Name} set to {row.Detail}. Arrow keys nudge it; Shift + arrows resize it.");
    }

    private void OnOverlayKeyDown(object sender, KeyEventArgs e)
    {
        if (Selected is not { Rect: { } rect } row) return;

        var resize = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 10 : 1;

        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0)
        };

        if (dx == 0 && dy == 0) return;
        e.Handled = true;

        row.Rect = resize
            ? new DrawingRectangle(rect.X, rect.Y, Math.Max(2, rect.Width + dx), Math.Max(2, rect.Height + dy))
            : new DrawingRectangle(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);

        RedrawOverlay();
        Status($"{row.Name}: {row.Detail}");
    }

    private void OnRegionSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RegionHint.Text = Selected is null ? "" : RegionNames.Describe(Selected.Name);
        RedrawOverlay();
    }

    private void OnClearRegion(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        row.Rect = null;
        RedrawOverlay();
        Status($"{row.Name} cleared.");
    }

    private void AdvanceToNextUnset()
    {
        var next = _rows.FirstOrDefault(r => r.Rect is null);
        if (next is not null) RegionListBox.SelectedItem = next;
    }

    // ---------------------------------------------------------------- rendering

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _zoom = e.NewValue;
        if (ZoomText is not null) ZoomText.Text = $"{_zoom:0.0}x";
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (_frame is null) return;
        ImageHost.Width = _frame.Width * _zoom;
        ImageHost.Height = _frame.Height * _zoom;
        RedrawOverlay();
    }

    private void RedrawOverlay()
    {
        Overlay.Children.Clear();
        if (_frame is null) return;

        foreach (var row in _rows)
        {
            if (row.Rect is not { } rect) continue;

            var isViewport = row.Name == RegionNames.Viewport;
            var isSelected = ReferenceEquals(row, Selected);

            var shape = new ShapeRectangle
            {
                Width = rect.Width * _zoom,
                Height = rect.Height * _zoom,
                Stroke = isSelected ? Brushes.Magenta : isViewport ? Brushes.Yellow : Brushes.Cyan,
                StrokeThickness = isSelected ? 2 : 1,
                Fill = Brushes.Transparent
            };

            System.Windows.Controls.Canvas.SetLeft(shape, rect.X * _zoom);
            System.Windows.Controls.Canvas.SetTop(shape, rect.Y * _zoom);
            Overlay.Children.Add(shape);

            var label = new System.Windows.Controls.TextBlock
            {
                Text = row.Name,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
                Padding = new Thickness(3, 0, 3, 0),
                FontSize = 11
            };

            System.Windows.Controls.Canvas.SetLeft(label, rect.X * _zoom);
            System.Windows.Controls.Canvas.SetTop(label, Math.Max(0, rect.Y * _zoom - 15));
            Overlay.Children.Add(label);
        }
    }

    /// <summary>Turn two drag corners into a positive-sized box in canvas coordinates.</summary>
    private Rect Normalise(WpfPoint a, WpfPoint b)
    {
        if (_frame is null) return new Rect();

        var maxX = _frame.Width * _zoom;
        var maxY = _frame.Height * _zoom;

        var x1 = Math.Clamp(Math.Min(a.X, b.X), 0, maxX);
        var y1 = Math.Clamp(Math.Min(a.Y, b.Y), 0, maxY);
        var x2 = Math.Clamp(Math.Max(a.X, b.X), 0, maxX);
        var y2 = Math.Clamp(Math.Max(a.Y, b.Y), 0, maxY);

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>Canvas coordinates are zoomed; the layout is stored in real frame pixels.</summary>
    private DrawingRectangle ToFramePixels(Rect box) => new(
        (int)Math.Round(box.X / _zoom),
        (int)Math.Round(box.Y / _zoom),
        Math.Max(1, (int)Math.Round(box.Width / _zoom)),
        Math.Max(1, (int)Math.Round(box.Height / _zoom)));

    // ---------------------------------------------------------------- saving and checking

    private void OnSaveLayout(object sender, RoutedEventArgs e)
    {
        if (_frame is null) { Status("Capture a frame first."); return; }

        if (ViewportRow.Rect is not { } viewport)
        {
            Status("Draw the 'viewport' box first — every other region is measured relative to it.");
            return;
        }

        var layout = new CaptureLayout
        {
            SourceWindowTitle = (WindowList.SelectedItem as WindowInfo)?.Title,
            Viewport = NormRect.FromPixels(viewport, new DrawingRectangle(0, 0, _frame.Width, _frame.Height))
        };

        foreach (var row in _rows.Skip(1))
            if (row.Rect is { } rect)
                layout.Regions[row.Name] = NormRect.FromPixels(rect, viewport);

        layout.Save(ProjectPaths.LayoutFile);

        var missing = _rows.Count(r => r.Rect is null);
        Status($"Saved to {ProjectPaths.LayoutFile}"
             + (missing > 0 ? $"  ({missing} region(s) still not drawn)" : "  (all regions drawn)"));
    }

    private void OnLoadLayout(object sender, RoutedEventArgs e)
    {
        if (_frame is null) { Status("Capture a frame first, so the saved proportions can be resolved to pixels."); return; }
        if (!File.Exists(ProjectPaths.LayoutFile)) { Status("No layout saved yet."); return; }

        try
        {
            var layout = CaptureLayout.Load(ProjectPaths.LayoutFile);
            var frameRect = new DrawingRectangle(0, 0, _frame.Width, _frame.Height);

            if (layout.Viewport is null) { Status("That layout has no viewport."); return; }

            var viewport = layout.Viewport.ToPixels(frameRect);
            ViewportRow.Rect = viewport;

            foreach (var row in _rows.Skip(1))
                row.Rect = layout.Regions.TryGetValue(row.Name, out var norm) ? norm.ToPixels(viewport) : null;

            RedrawOverlay();
            Status($"Loaded {ProjectPaths.LayoutFile} and re-fitted it to this {_frame.Width}×{_frame.Height} frame.");
        }
        catch (Exception ex)
        {
            Status($"Could not load layout: {ex.Message}");
        }
    }

    private void OnExportCrops(object sender, RoutedEventArgs e)
    {
        if (_frame is null) { Status("Capture a frame first."); return; }
        if (ViewportRow.Rect is not { } viewport) { Status("Draw the 'viewport' box first."); return; }

        var layout = new CaptureLayout
        {
            Viewport = NormRect.FromPixels(viewport, new DrawingRectangle(0, 0, _frame.Width, _frame.Height))
        };

        foreach (var row in _rows.Skip(1))
            if (row.Rect is { } rect)
                layout.Regions[row.Name] = NormRect.FromPixels(rect, viewport);

        try
        {
            var folder = Path.Combine(ProjectPaths.Captures, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            FrameCropper.ExportForReview(_frame, layout, folder);
            Process.Start("explorer.exe", folder);
            Status($"Exported {layout.Regions.Count + 1} crops to {folder} — check each one shows exactly what its name says.");
        }
        catch (Exception ex)
        {
            Status($"Export failed: {ex.Message}");
        }
    }

    private void OnSaveFrame(object sender, RoutedEventArgs e)
    {
        if (_frame is null) { Status("Capture a frame first."); return; }

        var path = Path.Combine(ProjectPaths.Captures, $"frame_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        _frame.Save(path, ImageFormat.Png);
        Process.Start("explorer.exe", $"/select,\"{path}\"");
        Status($"Saved the full frame to {path}");
    }

    // ---------------------------------------------------------------- helpers

    private void Status(string message) => StatusText.Text = message;

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
