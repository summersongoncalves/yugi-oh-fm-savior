using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using YgoFm.Core;
using YgoFm.Vision;
// See BitmapInterop.cs for why System.Drawing is aliased rather than imported wholesale.
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace YgoFm.App;

/// <summary>
/// The whole program, for now: pick the emulator window, mark where the hand cards are, then
/// watch that region continuously and show what the recogniser thinks is in each slot. This is
/// the first slice — proving recognition works — before any fusion suggestion exists.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(700);

    private readonly ObservableCollection<SlotReadingView> _rows = [];

    private CardDatabase? _cards;
    private CardArtLibrary? _art;
    private HandReader? _reader;

    private IntPtr _targetWindow;
    private string _targetWindowTitle = "";
    private NormRect? _handRegion;
    private DispatcherTimer? _timer;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        ReadingsList.ItemsSource = _rows;
        Closed += (_, _) => StopObserving();
    }

    // ------------------------------------------------------------ Start

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_timer is not null)
        {
            StopObserving();
            return;
        }

        StartObserving();
    }

    private void StartObserving()
    {
        if (!EnsureDataLoaded()) return;

        var picker = new SelectEmulatorWindow { Owner = this };
        if (picker.ShowDialog() != true || picker.Chosen is not { } target) return;

        DrawingBitmap snapshot;
        try
        {
            // We read pixels off the composited desktop, so the target has to be on top,
            // and it needs a moment to actually finish drawing after being raised.
            ScreenCapture.BringToFront(target.Handle);
            System.Threading.Thread.Sleep(250);
            snapshot = ScreenCapture.CaptureWindow(target.Handle);
        }
        catch (Exception ex)
        {
            Status($"Não foi possível capturar '{target.Title}': {ex.Message}");
            return;
        }

        if (ScreenCapture.LooksBlank(snapshot))
        {
            snapshot.Dispose();
            Status($"A captura de '{target.Title}' saiu de uma cor só — o backend gráfico dessa janela bloqueia " +
                   "esse tipo de captura. Tente outro modo de renderização no emulador.");
            return;
        }

        using (snapshot)
        {
            var regionPicker = new SelectHandRegionWindow(snapshot) { Owner = this };
            if (regionPicker.ShowDialog() != true || regionPicker.Selection is not { } region) return;

            _targetWindow = target.Handle;
            _targetWindowTitle = target.Title;
            _handRegion = region;

            // The verification habit from CLAUDE.md: save what was actually selected, so a
            // wrong region can be diagnosed after the fact instead of only guessed at.
            var folder = ProjectPaths.NewCaptureFolder(DateTime.Now);
            snapshot.Save(Path.Combine(folder, "00-snapshot.png"), System.Drawing.Imaging.ImageFormat.Png);
            using (var marked = PreviewAnnotator.Annotate(snapshot,
                       [new SlotReading
                       {
                           Slot = 0, SlotBounds = region.ToPixels(new DrawingRectangle(DrawingPoint.Empty, snapshot.Size)),
                           ArtBounds = region.ToPixels(new DrawingRectangle(DrawingPoint.Empty, snapshot.Size)),
                           Verdict = SlotVerdict.Confident,
                       }]))
                marked.Save(Path.Combine(folder, "01-selected-region.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        StartMenuItem.Header = "_Parar";
        Status($"Observando '{target.Title}'.");

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void StopObserving()
    {
        if (_timer is null) return;

        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _timer = null;

        StartMenuItem.Header = "_Start";
        Status("Parado. Clique em Start para escolher a janela e a região novamente.");
    }

    private bool EnsureDataLoaded()
    {
        if (_cards is not null && _art is not null) return true;

        try
        {
            _cards = CardDatabase.Load(ProjectPaths.CardsFile);
            _art = CardArtLibrary.Load(ProjectPaths.CardArtFile, _cards.Count);
            _reader = new HandReader(_cards, _art);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Não consegui carregar a base de cartas:\n\n{ex.Message}\n\n" +
                $"Esperado em:\n{ProjectPaths.CardsFile}\n{ProjectPaths.CardArtFile}",
                "Base de cartas ausente", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    // ------------------------------------------------------------ observing loop

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (_busy || _reader is null || _handRegion is not { } region) return;
        _busy = true;

        try
        {
            var result = await System.Threading.Tasks.Task.Run(() => CaptureAndRead(region));

            if (result.Error is not null)
            {
                Status(result.Error);
                StopObserving();
                return;
            }

            if (result.Inactive)
            {
                // Deliberately leaves PreviewImage and _rows untouched — the last real frame
                // stays on screen rather than being replaced by anything synthetic.
                Status($"'{_targetWindowTitle}' não está em primeiro plano — pausado até você voltar a ela. " +
                       "Mostrando o último quadro observado.");
                return;
            }

            Status($"Observando '{_targetWindowTitle}'.");

            if (result.Annotated is not null)
            {
                PreviewImage.Source = result.Annotated.ToBitmapSource();
                result.Annotated.Dispose();
            }

            _rows.Clear();
            if (result.Rows is not null)
                foreach (var row in result.Rows) _rows.Add(row);
        }
        finally
        {
            _busy = false;
        }
    }

    private readonly record struct ReadResult(
        DrawingBitmap? Annotated, List<SlotReadingView>? Rows, string? Error, bool Inactive);

    /// <summary>Runs off the UI thread: capture, crop, recognise, annotate. Returns plain data
    /// plus a ready-to-show bitmap, so the UI thread only has to hand it to the controls.</summary>
    private ReadResult CaptureAndRead(NormRect region)
    {
        var bounds = ScreenCapture.WindowBounds(_targetWindow);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return new ReadResult(null, null, "A janela do emulador não está mais visível.", false);

        var cropBounds = region.ToPixels(new DrawingRectangle(DrawingPoint.Empty, bounds.Size));

        // We read pixels off the composited desktop: if the emulator is not the foreground
        // window, whatever got raised over it would be captured instead — another window, or
        // the desktop. Rather than capture that garbage, skip the tick entirely and leave the
        // last real frame on screen; the status line is what tells the user it is paused.
        if (!ScreenCapture.IsForeground(_targetWindow))
            return new ReadResult(null, null, null, true);

        using var full = ScreenCapture.CaptureRegion(bounds);

        DrawingBitmap crop;
        try
        {
            crop = FrameCropper.Crop(full, cropBounds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new ReadResult(null, null,
                "A região selecionada não cabe mais na janela — ela pode ter sido redimensionada.", false);
        }

        using (crop)
        {
            var readings = _reader!.Read(crop, HandLayout.Default);
            var annotated = PreviewAnnotator.Annotate(crop, readings);
            var rows = readings.Select(r => new SlotReadingView(r)).ToList();
            return new ReadResult(annotated, rows, null, false);
        }
    }

    private void Status(string text) => StatusText.Text = text;
}
