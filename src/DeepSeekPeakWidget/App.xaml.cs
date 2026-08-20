using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace DeepSeekPeakWidget;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                var log = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekPeakWidget", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(log)!);
                System.IO.File.WriteAllText(log,
                    $"[{DateTime.Now:HH:mm:ss}] {e.Exception}\n{e.Message}");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { AppNotificationManager.Default.Register(); } catch { }
        _window = new MainWindow();
        _window.Activate();
    }
}
