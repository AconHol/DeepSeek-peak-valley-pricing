using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using System.Runtime.InteropServices;

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
        _ = ShowWindowWhenShellReadyAsync(_window);
    }

    /// <summary>
    /// 开机自启时 shell 可能尚未就绪，过早显示窗口会被 DWM 遮盖（进程在、桌面上却看不到）。
    /// 等 Program Manager（shell 主窗口）出现后再 Activate，超时 30 秒兜底。
    /// </summary>
    private static async Task ShowWindowWhenShellReadyAsync(Window window)
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                if (GetShellWindow() != IntPtr.Zero) break;
            }
            catch { break; }
            await Task.Delay(500);
        }
        window.Activate();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
}
