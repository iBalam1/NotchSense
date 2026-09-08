using System.Windows;

namespace NotchSense;

public partial class App : Application
{
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _window = new MainWindow();
        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _window?.Dispose();
        base.OnExit(e);
    }
}
