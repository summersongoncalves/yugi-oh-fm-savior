using System.Windows;

namespace YgoFm.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var splash = new SplashWindow();
        splash.FadeSequenceCompleted += (_, _) =>
        {
            var main = new MainWindow();

            // Application.MainWindow defaults to whatever window was created first — which
            // would be the splash, since nothing has said otherwise yet. Left alone, that
            // matters because ShutdownMode defaults to OnMainWindowClose: closing the splash
            // below would end the whole application before MainWindow ever got a chance to
            // matter. Pointing MainWindow at the real window first, then showing it, then
            // closing the splash, keeps that shutdown check aimed at the window that should
            // actually decide when the app ends.
            MainWindow = main;
            main.Show();
            splash.Close();
        };
        splash.Show();
    }
}
