using System.Windows;
using System.Windows.Media.Animation;

namespace YgoFm.App;

/// <summary>
/// The logo shown briefly on startup: fades in, holds, fades out, then tells
/// <see cref="App"/> it is done so the real <see cref="MainWindow"/> can take over. It knows
/// nothing about the app's actual startup work — it is purely a timed animation plus one event.
/// </summary>
public partial class SplashWindow : Window
{
    private static readonly TimeSpan FadeIn = TimeSpan.FromSeconds(0.6);
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.4);
    private static readonly TimeSpan FadeOut = TimeSpan.FromSeconds(0.6);

    /// <summary>Raised once the fade-out finishes. The window does not close itself — see
    /// <see cref="App.OnStartup"/> for why that has to happen after <see cref="MainWindow"/> is
    /// already showing, not before.</summary>
    public event EventHandler? FadeSequenceCompleted;

    public SplashWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RunFadeSequence();
    }

    private void RunFadeSequence()
    {
        // Two DoubleAnimations on the same Storyboard, back to back in time via BeginTime,
        // rather than two separate Storyboards chained through Completed events — a Storyboard
        // is WPF's declarative way to describe "this property takes these values over time",
        // and expressing the whole in/hold/out timeline as one Storyboard keeps that timeline
        // in one place instead of split across event handlers.
        var fadeIn = new DoubleAnimation(0, 1, new Duration(FadeIn));

        var fadeOut = new DoubleAnimation(1, 0, new Duration(FadeOut))
        {
            BeginTime = FadeIn + Hold,
        };
        fadeOut.Completed += (_, _) => FadeSequenceCompleted?.Invoke(this, EventArgs.Empty);

        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);

        // Storyboard.SetTarget/SetTargetProperty are attached-property-style calls: they store
        // "animate THIS property on THIS object" metadata on each DoubleAnimation, since the
        // animation itself only knows *what values* to move through, not *what to apply them to*.
        Storyboard.SetTarget(fadeIn, this);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(fadeOut, this);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));

        storyboard.Begin();
    }
}
