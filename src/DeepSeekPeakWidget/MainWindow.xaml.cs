using DeepSeekPeakWidget.Models;
using DeepSeekPeakWidget.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace DeepSeekPeakWidget;

public sealed partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private AppConfig _config;
    private PeakSchedule _schedule;
    private readonly DispatcherQueueTimer _timer;
    private DispatcherQueueTimer? _refreshTimer;
    private DateTime _configLastWrite;

    private bool _backdropApplied;
    private bool _dragActive;
    private double _dragOffsetX;
    private double _dragOffsetY;
    private double _frac;

    private bool? _lastPhase;
    private readonly HashSet<long> _notifiedTicks = new();
    private string _lastTransitionKey = "";
    private uint _lastDpi;
    // 窗口位置持久化（参照 WinUIEx WindowManager 方案）
    private static readonly Dictionary<IntPtr, MainWindow> _hwndMap = new();
    private static SUBCLASSPROC? _subclassDelegate;
    private bool _showHandled;
    private bool _restoringPlacement;
    private int _balanceRefreshTick;
    private bool _balanceRefreshing;
    private string? _amountText;
    private Brush _amountBrush = NewBrush("#4CAF50");
    private Storyboard? _amountStoryboard;
    private readonly List<OdometerDigit> _amountDigits = new();
    private readonly List<Border> _timelineCells = new();
    private readonly List<PriceRow> _priceRows = new();

    private Brush _brushText = NewBrush("#E8EDF4");
    private Brush _brushSub = NewBrush("#8A94A6");
    private Brush _brushCard = NewBrush("#1B2330");
    private Brush _brushTrack = NewBrush("#2A3242");
    private Brush _brushCardBorder = NewBrush("#2A3242");
    private Brush _brushPeak = NewBrush("#FFB300");
    private Brush _brushOk = NewBrush("#4CAF50");
    private Brush _brushPeakDark = NewBrush("#9C6A00");
    private Brush _brushValleyDark = NewBrush("#2E7D32");
    private Brush _brushRow = NewBrush("#222B3A");
    private Brush _brushError = NewBrush("#FF6B6B");

    private bool IsLightMode =>
        _config.Window.ThemeMode == "light" ||
        (_config.Window.ThemeMode == "system" && IsSystemLightTheme());

    public MainWindow()
    {
        InitializeComponent();

        _configService = new ConfigService();
        _config = _configService.Load();
        _schedule = new PeakSchedule(_config);
        _configLastWrite = GetConfigLastWrite();

        ApplyWindowChrome();
        InstallWindowSubclass();
        SystemBackdrop = _config.Window.Backdrop == "none" ? null : new PersistentAcrylicBackdrop();
        ApplyThemeMode();
        UpdatePinButton();
        SetMode(_config.Window.Mode, resize: false);

        AppWindow.Changed += (_, e) =>
        {
            try
            {
                if (!(e.DidPositionChange || e.DidSizeChange)) return;
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var dpi = GetDpiForWindow(hwnd);
                if (dpi != 0 && dpi != _lastDpi)
                {
                    _lastDpi = dpi;
                    ResizeWindowForCurrentMode();
                }
            }
            catch { }
        };

        Activated += (_, _) =>
        {
            if (SystemBackdrop is PersistentAcrylicBackdrop pb)
            {
                pb.ForceInputActive();
            }
            if (!_backdropApplied)
            {
                _backdropApplied = true;
                if (!_showHandled || !HasUsablePlacement())
                {
                    // 仅在 WM_SHOWWINDOW 未处理，或没有可用 placement 记录
                    // （无记录 / 显示器布局变化）时才走按显示器匹配的回退；
                    // 否则以 SetWindowPlacement 的物理像素恢复为准，避免被回退二次移动
                    RestoreWindowPosition();
                }
                ResizeWindowForCurrentMode();
                // 窗口显示后 DPI 才真正生效，延迟一帧校准一次
                var cal = DispatcherQueue.GetForCurrentThread().CreateTimer();
                cal.Interval = TimeSpan.FromMilliseconds(300);
                cal.IsRepeating = false;
                cal.Tick += (_, _) =>
                {
                    cal.Stop();
                    ResizeWindowForCurrentMode();
                    ApplySavedPlacementCorrection();
                };
                cal.Start();
                ScheduleCloakCheck();
                try
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    var ex = GetWindowLong(hwnd, -20);
                    SetWindowLong(hwnd, -20, ex | 0x80); // WS_EX_TOOLWINDOW
                }
                catch { }
            }
        };

        Closed += (_, _) =>
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                if (GetWindowPlacement(hwnd, ref placement))
                {
                    var rc = placement.rcNormalPosition;
                    _config.Window.Left = rc.Left;
                    _config.Window.Top = rc.Top;
                    _config.Window.NormalLeft = rc.Left;
                    _config.Window.NormalTop = rc.Top;
                    _config.Window.NormalRight = rc.Right;
                    _config.Window.NormalBottom = rc.Bottom;
                    _config.Window.MonitorLayout = GetMonitorLayout();
                }
                else
                {
                    var pos = AppWindow.Position;
                    _config.Window.Left = pos.X;
                    _config.Window.Top = pos.Y;
                }
                try
                {
                    var (devId, monName) = GetMonitorIdentity(hwnd);
                    _config.Window.MonitorDeviceId = devId;
                    _config.Window.MonitorName = monName;
                }
                catch
                {
                    _config.Window.MonitorDeviceId = null;
                    _config.Window.MonitorName = null;
                }
                UninstallWindowSubclass(hwnd);
                _configService.Save(_config);
            }
            catch { }
        };

        ProgressTrack.SizeChanged += (_, _) => UpdateProgressFill();

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) =>
        {
            UpdateDisplay();
            // 按配置的秒数自动刷新余额（0 = 关闭自动刷新）
            var balanceSeconds = Math.Max(0, _config.BalanceRefreshSeconds);
            if (balanceSeconds > 0 && ++_balanceRefreshTick >= balanceSeconds)
            {
                _balanceRefreshTick = 0;
                _ = RefreshBalanceAsync();
            }
        };
        _timer.Start();
        UpdateRefreshTimer();

        BuildTimeline();
        BuildPrices();
        UpdateDisplay();
        _ = RefreshBalanceAsync();
    }

    // ---------- 窗口位置持久化（参照 WinUIEx WindowManager 方案） ----------

    /// <summary>子类化窗口，在首次显示前用 SetWindowPlacement 原子恢复位置+大小（物理像素）。</summary>
    private void InstallWindowSubclass()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _hwndMap[hwnd] = this;
            _subclassDelegate ??= SubclassProc;
            SetWindowSubclass(hwnd,
                Marshal.GetFunctionPointerForDelegate(_subclassDelegate),
                new UIntPtr(1), IntPtr.Zero);
        }
        catch { }
    }

    private void UninstallWindowSubclass(IntPtr hwnd)
    {
        try
        {
            if (_hwndMap.Remove(hwnd) && _subclassDelegate is not null)
            {
                RemoveWindowSubclass(hwnd,
                    Marshal.GetFunctionPointerForDelegate(_subclassDelegate),
                    new UIntPtr(1));
            }
        }
        catch { }
    }

    private static IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (_hwndMap.TryGetValue(hWnd, out var window))
        {
            if (uMsg == WM_SHOWWINDOW && wParam != IntPtr.Zero)
            {
                window.OnFirstShow();
            }
            else if (uMsg == WM_GETMINMAXINFO && window._restoringPlacement)
            {
                window.ClampMinMaxDuringRestore(lParam);
            }
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    /// <summary>窗口首次显示前调用：尝试用 SetWindowPlacement 一次恢复保存的物理矩形。</summary>
    private void OnFirstShow()
    {
        if (_showHandled) return;
        _showHandled = true;
        try
        {
            TryRestorePlacement();
        }
        catch { }
    }

    /// <summary>显示器布局未变时，用 SetWindowPlacement 原子恢复位置+大小，避免 DPI 二次缩放。</summary>
    private bool TryRestorePlacement()
    {
        var cfg = _config.Window;
        if (cfg.NormalLeft is not int l || cfg.NormalTop is not int t ||
            cfg.NormalRight is not int r || cfg.NormalBottom is not int b)
        {
            return false;
        }
        if (!MonitorLayoutMatches())
        {
            return false;
        }
        var placement = new WINDOWPLACEMENT
        {
            length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)),
            flags = 0,
            showCmd = 1, // SW_SHOWNORMAL
            rcNormalPosition = new RECT { Left = l, Top = t, Right = r, Bottom = b },
        };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _restoringPlacement = true;
        try
        {
            return SetWindowPlacement(hwnd, ref placement);
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    /// <summary>恢复期间把最小/最大跟踪尺寸锁为保存值，防止 DPI 切换把窗口二次缩放。</summary>
    private void ClampMinMaxDuringRestore(IntPtr lParam)
    {
        try
        {
            var cfg = _config.Window;
            var w = cfg.NormalRight.GetValueOrDefault() - cfg.NormalLeft.GetValueOrDefault();
            var h = cfg.NormalBottom.GetValueOrDefault() - cfg.NormalTop.GetValueOrDefault();
            if (w <= 0 || h <= 0) return;
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
            mmi.ptMinTrackSize = new POINT { X = w, Y = h };
            mmi.ptMaxTrackSize = new POINT { X = w, Y = h };
            mmi.ptMaxSize = new POINT { X = w, Y = h };
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        catch { }
    }

    /// <summary>首次显示完全稳定后，用 SetWindowPos 把窗口精确校正到保存的物理矩形
    /// （补偿 WinUI 首次显示时对隐藏标题栏高度的偏移）。</summary>
    private void ApplySavedPlacementCorrection()
    {
        try
        {
            if (!HasUsablePlacement()) return;
            var cfg = _config.Window;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowPos(hwnd, IntPtr.Zero,
                cfg.NormalLeft!.Value, cfg.NormalTop!.Value,
                cfg.NormalRight!.Value - cfg.NormalLeft!.Value,
                cfg.NormalBottom!.Value - cfg.NormalTop!.Value,
                0x0001 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE
        }
        catch { }
    }

    private int _cloakCheckIndex;
    private static readonly int[] _cloakCheckDelays = { 5, 15, 30 };

    private static string CloakRelaunchFlagPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepSeekPeakWidget", "cloak-relaunch-flag");

    /// <summary>
    /// 开机自启时若窗口被 DWM 遮盖（进程在、桌面看不到），定时检查并自愈重启。
    /// 多次检查均正常则删除标记；本次开机已因遮盖重启过则不再重复，避免循环。
    /// </summary>
    private void ScheduleCloakCheck()
    {
        try
        {
            if (System.IO.File.Exists(CloakRelaunchFlagPath)) return;
            _cloakCheckIndex = 0;
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (IsWindowCloaked())
                {
                    RelaunchSelf();
                    return;
                }
                _cloakCheckIndex++;
                if (_cloakCheckIndex < _cloakCheckDelays.Length)
                {
                    timer.Interval = TimeSpan.FromSeconds(_cloakCheckDelays[_cloakCheckIndex]);
                    timer.Start();
                }
                else
                {
                    try { System.IO.File.Delete(CloakRelaunchFlagPath); } catch { }
                }
            };
            timer.Interval = TimeSpan.FromSeconds(_cloakCheckDelays[0]);
            timer.Start();
        }
        catch { }
    }

    private bool IsWindowCloaked()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var cloaked = 0;
            DwmGetWindowAttribute(hwnd, 14, out cloaked, sizeof(int)); // DWMWA_CLOAKED = 14
            return cloaked != 0;
        }
        catch { return false; }
    }

    /// <summary>保存配置后以 explorer 重新启动打包应用，再关闭当前实例。</summary>
    private void RelaunchSelf()
    {
        try
        {
            _configService.Save(_config);
            try { System.IO.File.WriteAllText(CloakRelaunchFlagPath, DateTime.Now.ToString("O")); } catch { }
            var aumid = $"{Windows.ApplicationModel.Package.Current.Id.FamilyName}!App";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\{aumid}",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
            Close();
        }
        catch { }
    }

    /// <summary>当前所有显示器的完整矩形（用于校验布局是否变化）。</summary>
    private static List<MonitorLayoutInfo> GetMonitorLayout()
    {
        var list = new List<MonitorLayoutInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMon, IntPtr hdc,
            ref RECT r, IntPtr data)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
            if (GetMonitorInfo(hMon, ref mi))
            {
                list.Add(new MonitorLayoutInfo
                {
                    Left = mi.rcMonitor.Left,
                    Top = mi.rcMonitor.Top,
                    Right = mi.rcMonitor.Right,
                    Bottom = mi.rcMonitor.Bottom,
                });
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>保存时的显示器布局与当前是否一致。</summary>
    private bool MonitorLayoutMatches()
    {
        var saved = _config.Window.MonitorLayout;
        if (saved is null || saved.Count == 0) return false;
        var current = GetMonitorLayout();
        if (current.Count != saved.Count) return false;
        for (var i = 0; i < current.Count; i++)
        {
            if (current[i].Left != saved[i].Left || current[i].Top != saved[i].Top ||
                current[i].Right != saved[i].Right || current[i].Bottom != saved[i].Bottom)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>是否存在可用的物理矩形 placement（含显示器布局校验）。</summary>
    private bool HasUsablePlacement()
    {
        var cfg = _config.Window;
        return cfg.NormalLeft is int && cfg.NormalTop is int &&
               cfg.NormalRight is int && cfg.NormalBottom is int &&
               MonitorLayoutMatches();
    }

    private void RestoreWindowPosition()
    {
        try
        {
            var size = AppWindow.Size;

            // 1) 优先：按保存的 PnP 设备 ID 找回原屏幕（普通本机稳定）
            if (!string.IsNullOrEmpty(_config.Window.MonitorDeviceId))
            {
                if (TryGetMonitorWorkArea(_config.Window.MonitorDeviceId,
                        out var wx, out var wy, out var ww, out var wh))
                {
                    MoveIntoWorkArea(wx, wy, ww, wh,
                        _config.Window.Left, _config.Window.Top, size);
                    return;
                }
            }

            // 2) 次选：按显示器名（\\.\DISPLAYx）找回原屏幕
            if (!string.IsNullOrEmpty(_config.Window.MonitorName))
            {
                if (TryGetMonitorWorkAreaByName(_config.Window.MonitorName,
                        out var x2, out var y2, out var w2, out var h2))
                {
                    MoveIntoWorkArea(x2, y2, w2, h2,
                        _config.Window.Left, _config.Window.Top, size);
                    return;
                }
            }

            // 3) 最后回退：按保存坐标所在显示器定位
            if (_config.Window.Left is int left && _config.Window.Top is int top)
            {
                var area = DisplayArea.GetFromPoint(
                    new PointInt32(left, top), DisplayAreaFallback.Nearest);
                var wa = area.WorkArea;
                MoveIntoWorkArea(wa.X, wa.Y, wa.Width, wa.Height,
                    left, top, size);
            }
        }
        catch { }
    }

    /// <summary>把窗口放进指定工作区（保留记忆位置，越界时夹紧，无记忆时居中）。</summary>
    private void MoveIntoWorkArea(
        int workX, int workY, int workW, int workH,
        int? savedLeft, int? savedTop, SizeInt32 size)
    {
        const int margin = 80;
        var xMin = workX - size.Width + margin;
        var xMax = workX + workW - margin;
        var yMin = workY - size.Height + margin;
        var yMax = workY + workH - margin;
        int x, y;
        if (savedLeft is int l && savedTop is int t)
        {
            x = xMin <= xMax ? Math.Clamp(l, xMin, xMax) : workX;
            y = yMin <= yMax ? Math.Clamp(t, yMin, yMax) : workY;
        }
        else
        {
            x = workX + (workW - size.Width) / 2;
            y = workY + (workH - size.Height) / 2;
        }
        AppWindow.Move(new PointInt32(x, y));
    }

    /// <summary>读取窗口所在显示器的 PnP 设备 ID 与显示器名（用于记忆屏幕）。</summary>
    private static (string? DeviceId, string? Name) GetMonitorIdentity(IntPtr hwnd)
    {
        var hMon = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
        if (hMon == IntPtr.Zero) return (null, null);
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
        if (!GetMonitorInfo(hMon, ref mi)) return (null, null);
        return (GetDeviceIdForName(mi.szDevice), mi.szDevice);
    }

    /// <summary>通过“枚举适配器 → 其上的显示器”取 PnP 设备 ID（远程/虚拟显示器可能为空）。</summary>
    private static string? GetDeviceIdForName(string deviceName)
    {
        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if (adapter.DeviceName != deviceName) continue;
            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            if (EnumDisplayDevices(adapter.DeviceName, 0, ref mon, 0))
            {
                return string.IsNullOrEmpty(mon.DeviceID) ? null : mon.DeviceID;
            }
        }
        return null;
    }

    /// <summary>按 PnP 设备 ID 查找显示器的工作区。</summary>
    private static bool TryGetMonitorWorkArea(
        string deviceId, out int x, out int y, out int w, out int h)
    {
        var rx = 0;
        var ry = 0;
        var rw = 0;
        var rh = 0;
        var found = false;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMon, IntPtr hdc,
            ref RECT r, IntPtr data)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
            if (GetMonitorInfo(hMon, ref mi))
            {
                if (GetDeviceIdForName(mi.szDevice) == deviceId)
                {
                    rx = mi.rcWork.Left;
                    ry = mi.rcWork.Top;
                    rw = mi.rcWork.Right - mi.rcWork.Left;
                    rh = mi.rcWork.Bottom - mi.rcWork.Top;
                    found = true;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        x = rx;
        y = ry;
        w = rw;
        h = rh;
        return found;
    }

    /// <summary>按显示器名（\\.\DISPLAYx）查找显示器的工作区。</summary>
    private static bool TryGetMonitorWorkAreaByName(
        string name, out int x, out int y, out int w, out int h)
    {
        var rx = 0;
        var ry = 0;
        var rw = 0;
        var rh = 0;
        var found = false;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMon, IntPtr hdc,
            ref RECT r, IntPtr data)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
            if (GetMonitorInfo(hMon, ref mi) && mi.szDevice == name)
            {
                rx = mi.rcWork.Left;
                ry = mi.rcWork.Top;
                rw = mi.rcWork.Right - mi.rcWork.Left;
                rh = mi.rcWork.Bottom - mi.rcWork.Top;
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        x = rx;
        y = ry;
        w = rw;
        h = rh;
        return found;
    }

    private void ApplyWindowChrome()
    {
        var appWindow = AppWindow;
        if (appWindow is not null)
        {
            ResizeWindow(
                Math.Max(280, (int)_config.Window.Width),
                Math.Max(320, (int)_config.Window.Height));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = _config.Window.AlwaysOnTop;
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                // 保留系统边框（主题色），隐藏标题栏文本
                presenter.SetBorderAndTitleBar(true, false);
            }
        }

        // 全页面拖动
        RootGrid.PointerPressed += (s, e) =>
        {
            var btnPt = e.GetCurrentPoint(PinBtn);
            if (btnPt.Position.X >= 0 && btnPt.Position.X <= PinBtn.ActualWidth &&
                btnPt.Position.Y >= 0 && btnPt.Position.Y <= PinBtn.ActualHeight)
            {
                return;
            }
            if (_config.Window.PinLock)
            {
                e.Handled = true;
                return;
            }
            if (e.GetCurrentPoint(RootGrid).Properties.IsLeftButtonPressed)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                GetCursorPos(out var cursor);
                GetWindowRect(hwnd, out var rect);
                _dragOffsetX = cursor.X - rect.Left;
                _dragOffsetY = cursor.Y - rect.Top;
                _dragActive = true;
                RootGrid.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        };
        RootGrid.PointerMoved += (s, e) =>
        {
            if (!_dragActive) return;
            GetCursorPos(out var cursor);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowPos(hwnd, IntPtr.Zero,
                cursor.X - (int)_dragOffsetX,
                cursor.Y - (int)_dragOffsetY,
                0, 0,
                0x0001 | 0x0004 | 0x0010);
            e.Handled = true;
        };
        RootGrid.PointerReleased += (s, e) =>
        {
            if (_dragActive)
            {
                _dragActive = false;
                RootGrid.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        };
        AppTitleBar.RightTapped += (s, e) => e.Handled = true;
    }

    /// <summary>按主题更新卡片、文字与强调色。</summary>
    private void ApplyThemeMode()
    {
        var light = IsLightMode;
        try
        {
            Application.Current.RequestedTheme = light
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
        }
        catch { }
        RootGrid.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;

        _brushText = NewBrush(light ? "#1B1B1B" : "#E8EDF4");
        _brushSub = NewBrush(light ? "#6B6B6B" : "#8A94A6");
        _brushCard = NewBrush(light ? "#FFFFFF" : "#1B2330");
        _brushTrack = NewBrush(light ? "#E3E3E3" : "#2A3242");
        _brushCardBorder = NewBrush(light ? "#E0E0E0" : "#2A3242");
        _brushPeak = NewBrush(light ? "#B26A00" : "#FFB300");
        _brushOk = NewBrush(light ? "#2E7D32" : "#4CAF50");
        _brushPeakDark = NewBrush(light ? "#E6A23C" : "#9C6A00");
        _brushValleyDark = NewBrush(light ? "#81C784" : "#2E7D32");
        _brushRow = NewBrush(light ? "#F3F3F3" : "#222B3A");
        _brushError = NewBrush(light ? "#C42B1C" : "#FF6B6B");

        var textCol = light ? "#1B1B1B" : "#E8EDF4";
        var subCol = light ? "#6B6B6B" : "#8A94A6";
        TitleIcon.Foreground = NewBrush(textCol);
        TitleText.Foreground = NewBrush(textCol);
        PhaseTag.Foreground = NewBrush(subCol);
        CountdownText.Foreground = NewBrush(textCol);
        FooterText.Foreground = NewBrush(light ? "#6B6B6B" : "#9AA4B2");
        ScheduleHint.Foreground = NewBrush(subCol);
        PhaseSubText.Foreground = NewBrush(light ? "#6B6B6B" : "#9AA4B2");
        NextPhaseText.Foreground = NewBrush(light ? "#6B6B6B" : "#9AA4B2");

        foreach (var card in new Border[] { StatusCard, BalanceCard, TimelineCard, TransitionCard, PriceCard })
        {
            card.Background = _brushCard;
            card.BorderBrush = _brushCardBorder;
        }
        ProgressTrack.Background = _brushTrack;
        // 背景透明度：仅调整亚克力背景的 tint，内容保持不透明（与 PVE 小组件一致）
        if (SystemBackdrop is PersistentAcrylicBackdrop pab)
        {
            pab.SetBackground(_config.Window.Opacity, light);
        }

        if (light)
        {
            RootGrid.Background = NewBrush("#F0F0F0");
            FooterBar.Background = NewBrush("#F0F0F0");
        }
        else
        {
            RootGrid.Background = _config.Window.Backdrop == "acrylic" ? null : NewBrush("#141823");
            FooterBar.Background = _config.Window.Backdrop == "acrylic" ? null : NewBrush("#141823");
        }
        _lastTransitionKey = ""; // 主题变化时强制重建“接下来切换”列表，刷新配色
        UpdateTimeline();
        UpdatePrices();
        UpdateTransitions();
        UpdatePinButton();
    }

    internal static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("SystemUsesLightTheme") is int sys)
            {
                return sys == 1;
            }
            if (key?.GetValue("AppsUseLightTheme") is int v)
            {
                return v == 1;
            }
        }
        catch { }
        return false;
    }

    /// <summary>保存配置后由设置窗口回调：重载并即时应用。</summary>
    public void ApplyConfig()
    {
        _schedule = new PeakSchedule(_config);
        ApplyWindowChrome();
        SystemBackdrop = _config.Window.Backdrop == "none" ? null : new PersistentAcrylicBackdrop();
        ApplyThemeMode();
        UpdatePinButton();
        SetMode(_config.Window.Mode, resize: false);
        BuildTimeline();
        BuildPrices();
        UpdateDisplay();
        UpdateRefreshTimer();
        _balanceRefreshTick = 0; // 应用新间隔，立即按新配置计时
        _ = RefreshBalanceAsync();
    }

    /// <summary>按配置间隔自动重读 config.json，定价/时段变化无需重启即可生效。</summary>
    private void UpdateRefreshTimer()
    {
        try
        {
            if (_refreshTimer is null)
            {
                _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _refreshTimer.IsRepeating = true;
                _refreshTimer.Tick += (_, _) => CheckAutoReload();
            }
            var minutes = Math.Max(0, _config.RefreshMinutes);
            _refreshTimer.Stop();
            if (minutes > 0)
            {
                _refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
                _refreshTimer.Start();
            }
        }
        catch { }
    }

    private DateTime GetConfigLastWrite()
    {
        try
        {
            var fi = new FileInfo(_configService.ConfigPath);
            if (fi.Exists) return fi.LastWriteTimeUtc;
        }
        catch { }
        return DateTime.MinValue;
    }

    private void CheckAutoReload()
    {
        try
        {
            var lastWrite = GetConfigLastWrite();
            if (lastWrite == _configLastWrite) return;
            _configLastWrite = lastWrite;
            var fresh = _configService.Load();
            if (fresh is null) return;
            _config = fresh;
            ApplyConfig();
        }
        catch { }
    }

    private void SetMode(string mode, bool resize = true)
    {
        _config.Window.Mode = mode;
        var compact = mode == "compact";
        var showExtra = compact ? Visibility.Collapsed : Visibility.Visible;
        if (resize && AppWindow is not null)
        {
            var w = compact ? 250 : (int)_config.Window.Width;
            var h = compact ? 168 : (int)_config.Window.Height;
            ResizeWindow(w, h);
        }
        TimelineCard.Visibility = showExtra;
        BalanceCard.Visibility = showExtra;
        TransitionCard.Visibility = showExtra;
        PriceCard.Visibility = showExtra;
        FooterBar.Visibility = showExtra;
        PhaseSubText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (compact)
        {
            PhaseText.FontSize = 20;
            CountdownText.FontSize = 15;
            NextPhaseText.Visibility = Visibility.Collapsed;
        }
        else
        {
            PhaseText.FontSize = 26;
            CountdownText.FontSize = 18;
            NextPhaseText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>按当前显示器 DPI 缩放系数换算后调整窗口大小（AppWindow.Resize 使用物理像素）。</summary>
    private void ResizeWindowForCurrentMode()
    {
        if (AppWindow is null) return;
        var compact = _config.Window.Mode == "compact";
        ResizeWindow(
            compact ? 250 : Math.Max(280, (int)_config.Window.Width),
            compact ? 168 : Math.Max(320, (int)_config.Window.Height));
    }

    private void ResizeWindow(double dipWidth, double dipHeight)
    {
        if (AppWindow is null) return;
        var scale = GetDpiScale();
        AppWindow.Resize(new SizeInt32(
            Math.Max(1, (int)Math.Round(dipWidth * scale)),
            Math.Max(1, (int)Math.Round(dipHeight * scale))));
    }

    /// <summary>获取窗口所在显示器的 DPI 缩放系数（如 1.5 = 150%）。</summary>
    private double GetDpiScale()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd != IntPtr.Zero)
            {
                var dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return dpi / 96.0;
            }
        }
        catch { }
        try
        {
            return GetDpiForSystem() / 96.0;
        }
        catch { return 1.0; }
    }

    private void BuildTimeline()
    {
        TimelineGrid.ColumnDefinitions.Clear();
        TimelineGrid.Children.Clear();
        _timelineCells.Clear();
        for (var h = 0; h < 24; h++)
        {
            TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
            var isPeak = _schedule.IsPeakHour(h);
            var cell = new Border
            {
                Margin = new Thickness(0.5, 0, 0.5, 0),
                CornerRadius = new CornerRadius(2),
            };
            ToolTipService.SetToolTip(cell,
                $"{h:00}:00-{h + 1:00}:00 · {(isPeak ? "峰时（全价）" : "谷时（半价）")}");
            Grid.SetColumn(cell, h);
            TimelineGrid.Children.Add(cell);
            _timelineCells.Add(cell);
        }
        UpdateTimeline();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < _config.PeakWindows.Count; i++)
        {
            if (i > 0) sb.Append(" · ");
            sb.Append($"峰时 {_config.PeakWindows[i].Start}-{_config.PeakWindows[i].End}");
        }
        sb.Append("（其余为谷时）");
        if (_config.WeekendAllValley) sb.Append("，周末及节假日全天谷时");
        ScheduleHint.Text = sb.ToString();
    }

    private void UpdateTimeline()
    {
        var hour = _schedule.ScheduleNow.Hour;
        for (var h = 0; h < 24 && h < _timelineCells.Count; h++)
        {
            var cell = _timelineCells[h];
            var isPeak = _schedule.IsPeakHour(h);
            if (h == hour)
            {
                cell.Background = isPeak ? _brushPeak : _brushOk;
                cell.BorderThickness = new Thickness(1.5);
                cell.BorderBrush = _brushText;
            }
            else
            {
                cell.Background = isPeak ? _brushPeakDark : _brushValleyDark;
                cell.BorderThickness = new Thickness(0);
            }
        }
    }

    private sealed class PriceRow
    {
        public Grid Grid { get; init; } = new();
        public ModelPrice Model { get; init; } = new();
        public bool Peak { get; init; }
        public TextBlock[] Cells { get; init; } = new TextBlock[3];
        public TextBlock Tag { get; init; } = new();
    }

    private void BuildPrices()
    {
        PriceGrid.Children.Clear();
        PriceGrid.RowDefinitions.Clear();
        PriceGrid.ColumnDefinitions.Clear();
        foreach (var width in new[] { 78.0, 1.0, 1.0, 1.0, 36.0 })
        {
            PriceGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = width < 2
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(width, GridUnitType.Pixel),
            });
        }
        AddPriceRowHeader();

        _priceRows.Clear();
        AddPriceRow(_config.Flash, peak: false);
        AddPriceRow(_config.Flash, peak: true);
        AddPriceRow(_config.Pro, peak: false);
        AddPriceRow(_config.Pro, peak: true);
        UpdatePrices();
    }

    private void AddPriceRowHeader()
    {
        var rd = new RowDefinition { Height = GridLength.Auto };
        PriceGrid.RowDefinitions.Add(rd);
        var headers = new[] { "模型", "命中输入", "输入", "输出", "时段" };
        for (var i = 0; i < headers.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = headers[i],
                FontSize = 10,
                Foreground = _brushSub,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            Grid.SetColumn(tb, i);
            PriceGrid.Children.Add(tb);
        }
    }

    private void AddPriceRow(ModelPrice model, bool peak)
    {
        var rd = new RowDefinition { Height = GridLength.Auto };
        PriceGrid.RowDefinitions.Add(rd);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var width in new[] { 78.0, 1.0, 1.0, 1.0, 36.0 })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = width < 2
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(width, GridUnitType.Pixel),
            });
        }

        var name = new TextBlock
        {
            Text = model.Name,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _brushText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2),
        };
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        var cells = new TextBlock[3];
        for (var i = 0; i < 3; i++)
        {
            cells[i] = new TextBlock
            {
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(cells[i], i + 1);
            grid.Children.Add(cells[i]);
        }
        var tag = new TextBlock
        {
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tag, 4);
        grid.Children.Add(tag);

        Grid.SetRow(grid, PriceGrid.RowDefinitions.Count - 1);
        Grid.SetColumnSpan(grid, 5);
        PriceGrid.Children.Add(grid);
        _priceRows.Add(new PriceRow { Grid = grid, Model = model, Peak = peak, Cells = cells, Tag = tag });
    }

    private void UpdatePrices()
    {
        var phase = _schedule.Current();
        var isPeak = phase.IsPeak;
        foreach (var row in _priceRows)
        {
            var isCurrent = row.Peak == isPeak;
            var hit = row.Peak ? row.Model.HitPeak : row.Model.HitValley;
            var input = row.Peak ? row.Model.InputPeak : row.Model.InputValley;
            var output = row.Peak ? row.Model.OutputPeak : row.Model.OutputValley;
            row.Cells[0].Text = hit.ToString("N2");
            row.Cells[1].Text = input.ToString("N2");
            row.Cells[2].Text = output.ToString("N2");
            row.Tag.Text = row.Peak ? "峰" : "谷";
            if (isCurrent)
            {
                var color = isPeak ? _brushPeak : _brushOk;
                foreach (var c in row.Cells) c.Foreground = color;
                row.Tag.Foreground = color;
                row.Grid.Opacity = 1.0;
            }
            else
            {
                foreach (var c in row.Cells) c.Foreground = _brushSub;
                row.Tag.Foreground = _brushSub;
                row.Grid.Opacity = 0.55;
            }
        }
    }

    private void UpdateTransitions()
    {
        var phase = _schedule.Current();
        var trans = _schedule.NextTransitions(phase, 3);
        var key = string.Join("|", trans.Select(t => $"{t.Time.Ticks}-{t.IsPeak}"));
        if (key != _lastTransitionKey)
        {
            _lastTransitionKey = key;
            TransitionList.Children.Clear();
            foreach (var t in trans)
            {
                var border = new Border
                {
                    Background = _brushRow,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 5, 8, 5),
                };
                var inner = new Grid();
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = t.IsPeak ? _brushPeak : _brushOk,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                inner.Children.Add(dot);
                var timeTb = new TextBlock
                {
                    Text = $"{t.Time:HH:mm} 进入{(t.IsPeak ? "峰时" : "谷时")}",
                    FontSize = 11,
                    Foreground = _brushText,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(timeTb, 1);
                inner.Children.Add(timeTb);
                var remainTb = new TextBlock
                {
                    FontSize = 10.5,
                    Foreground = _brushSub,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = FormatRemain(t.Time - phase.Now),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(remainTb, 2);
                inner.Children.Add(remainTb);
                border.Child = inner;
                TransitionList.Children.Add(border);
            }
        }
        else
        {
            // 仅更新剩余时间文本
            var idx = 0;
            foreach (var t in trans)
            {
                if (idx < TransitionList.Children.Count &&
                    TransitionList.Children[idx] is Border b && b.Child is Grid g &&
                    g.Children.Count > 2 && g.Children[2] is TextBlock remain)
                {
                    remain.Text = FormatRemain(t.Time - phase.Now);
                }
                idx++;
            }
        }
    }

    private void UpdateProgressFill()
    {
        var width = Math.Max(0, ProgressTrack.ActualWidth) * Math.Clamp(_frac, 0, 1);
        ProgressFill.Width = width;
    }

    private void UpdateDisplay()
    {
        try
        {
            var phase = _schedule.Current();
            var isPeak = phase.IsPeak;
            var color = isPeak ? _brushPeak : _brushOk;

            PhaseDot.Fill = color;
            PhaseTag.Text = isPeak ? "峰时" : "谷时";
            PhaseTag.Foreground = color;
            PhaseText.Text = isPeak ? "峰时 · 全价" : "谷时 · 半价";
            PhaseText.Foreground = color;
            PhaseSubText.Text = isPeak
                ? "当前按全价计费，建议错峰使用"
                : "API 价格按高峰时段的一半计费";

            if (phase.AllDayValley)
            {
                CountdownText.Text = "--:--:--";
                NextPhaseText.Text = "今天全天谷时";
                _frac = 1;
            }
            else if (phase.NextTime is DateTime nt)
            {
                CountdownText.Text = FormatDuration(nt - phase.Now);
                NextPhaseText.Text = $"{nt:HH:mm} 进入{(phase.NextIsPeak ? "峰时" : "谷时")}";
                var total = (nt - phase.SegmentStart).TotalSeconds;
                _frac = total > 0 ? (phase.Now - phase.SegmentStart).TotalSeconds / total : 0;
            }
            else
            {
                CountdownText.Text = "--:--:--";
                NextPhaseText.Text = "";
                _frac = 1;
            }
            ProgressFill.Background = color;
            UpdateProgressFill();
            UpdateTimeline();
            UpdateTransitions();
            UpdatePrices();
            CheckNotifications(phase);
            FooterText.Text = $"更新于 {DateTime.Now:HH:mm:ss} · 北京时间 · 右击打开菜单";
        }
        catch (Exception ex)
        {
            FooterText.Text = $"渲染异常: {ex.Message}";
        }
    }

    // ---------- DeepSeek 余额 ----------

    /// <summary>查询 DeepSeek 账户余额并更新底部显示（未配置 API Key 时给出提示）。</summary>
    private async Task RefreshBalanceAsync()
    {
        if (_balanceRefreshing) return; // 防止上一次请求未完成时重复发起
        _balanceRefreshing = true;
        try
        {
            var key = _config.ApiKey?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                UpdateBalanceCard("未配置", _amountText ?? "--", "未配置 API Key（右键 → 个性化设置）", false);
                return;
            }
            var bal = await DeepSeekApiClient.GetBalanceAsync(key);
            if (bal is null)
            {
                UpdateBalanceCard("失败", _amountText ?? "--", "余额获取失败", true);
                return;
            }
            var info = bal.BalanceInfos?.FirstOrDefault();
            if (info is null)
            {
                UpdateBalanceCard(bal.IsAvailable ? "可用" : "不可用", _amountText ?? "--",
                    "账户可用，余额未知", !bal.IsAvailable);
                return;
            }
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(info.GrantedBalance) && info.GrantedBalance != "0.00")
            {
                parts.Add($"赠送 ¥{info.GrantedBalance}");
            }
            if (!string.IsNullOrEmpty(info.ToppedUpBalance) && info.ToppedUpBalance != "0.00")
            {
                parts.Add($"充值 ¥{info.ToppedUpBalance}");
            }
            var detail = parts.Count > 0 ? string.Join(" · ", parts) : "无充值/赠送明细";
            UpdateBalanceCard(bal.IsAvailable ? "可用" : "不可用",
                $"¥{info.TotalBalance}", detail, !bal.IsAvailable);
        }
        catch (Exception ex)
        {
            UpdateBalanceCard("失败", _amountText ?? "--", $"余额获取失败：{ShortBalanceError(ex)}", true);
        }
        finally
        {
            _balanceRefreshing = false;
        }
    }

    private void UpdateBalanceCard(string status, string amount, string detail, bool isError)
    {
        try
        {
            BalanceStatusText.Text = status;
            BalanceStatusText.Foreground = isError ? _brushError : _brushSub;
            BalanceDetailText.Text = detail;
            SetAmount(amount, isError ? _brushError : _brushOk);
        }
        catch { }
    }

    // ---------- 余额数字“拨轮码盘”滚动动画 ----------

    private const double AmountDigitWidth = 12;
    private const double AmountDigitHeight = 30;
    private const int AmountWheelCycles = 4; // 每个滚轮放 4 组 0-9，保证正向/反向都能滚动

    private sealed class OdometerDigit
    {
        public bool IsDigit;
        public UIElement Root = null!;
        public StackPanel? Wheel;
        public int CurrentDigit;
        public List<TextBlock> Texts = new();
    }

    private TextBlock MakeAmountChar(string text, double width)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Width = width,
            Height = AmountDigitHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = _amountBrush,
        };
    }

    /// <summary>按金额字符串重建码盘（形状变化时调用，无动画）。</summary>
    private void BuildAmountPanel(string amount)
    {
        BalanceAmountPanel.Children.Clear();
        _amountDigits.Clear();
        foreach (var c in amount)
        {
            var item = new OdometerDigit();
            if (char.IsDigit(c))
            {
                item.IsDigit = true;
                var cell = new Grid
                {
                    Width = AmountDigitWidth,
                    Height = AmountDigitHeight,
                    Clip = new RectangleGeometry
                    {
                        Rect = new Windows.Foundation.Rect(0, 0, AmountDigitWidth, AmountDigitHeight),
                    },
                };
                var wheel = new StackPanel();
                wheel.RenderTransform = new TranslateTransform();
                for (var cycle = 0; cycle < AmountWheelCycles; cycle++)
                {
                    for (var d = 0; d < 10; d++)
                    {
                        var tb = MakeAmountChar(d.ToString(), AmountDigitWidth);
                        wheel.Children.Add(tb);
                        item.Texts.Add(tb);
                    }
                }
                item.Wheel = wheel;
                cell.Children.Add(wheel);
                item.Root = cell;
            }
            else
            {
                var width = c switch
                {
                    '¥' => 16.0,
                    '.' => 6.0,
                    ',' => 6.0,
                    '-' => 12.0,
                    _ => 12.0,
                };
                var tb = MakeAmountChar(c.ToString(), width);
                item.Root = tb;
                item.Texts.Add(tb);
            }
            _amountDigits.Add(item);
            BalanceAmountPanel.Children.Add(item.Root);
        }

        // 把滚轮定位到实际数字（中间周期）
        for (var i = 0; i < amount.Length && i < _amountDigits.Count; i++)
        {
            var item = _amountDigits[i];
            if (!item.IsDigit || item.Wheel is null) continue;
            var d = amount[i] - '0';
            item.CurrentDigit = d;
            item.Wheel.RenderTransform = new TranslateTransform { Y = -(10 + d) * AmountDigitHeight };
        }
    }

    /// <summary>更新金额显示：形状相同则滚动动画，形状变化则重建。</summary>
    private void SetAmount(string amount, Brush brush)
    {
        _amountBrush = brush;
        ApplyAmountBrush();
        if (amount == _amountText) return;
        if (!CanAnimateAmount(_amountText, amount))
        {
            BuildAmountPanel(amount);
        }
        else
        {
            AnimateAmountDigits(_amountText!, amount);
        }
        _amountText = amount;
    }

    private void ApplyAmountBrush()
    {
        foreach (var item in _amountDigits)
        {
            foreach (var tb in item.Texts)
            {
                tb.Foreground = _amountBrush;
            }
        }
    }

    private static bool CanAnimateAmount(string? oldAmount, string newAmount)
    {
        if (string.IsNullOrEmpty(oldAmount) || oldAmount.Length != newAmount.Length) return false;
        for (var i = 0; i < oldAmount.Length; i++)
        {
            if (!char.IsDigit(oldAmount[i]) && oldAmount[i] != newAmount[i]) return false;
        }
        return true;
    }

    /// <summary>逐位滚动到新数字（从右向左级联，模拟机械码盘）。</summary>
    private void AnimateAmountDigits(string oldAmount, string newAmount)
    {
        var digitCount = oldAmount.Count(char.IsDigit);
        var animations = new List<(OdometerDigit Digit, int FromIdx, int ToIdx, int FromRight, int NewDigit)>();
        var seen = 0;
        for (var i = 0; i < oldAmount.Length && i < _amountDigits.Count; i++)
        {
            var item = _amountDigits[i];
            if (!item.IsDigit) continue;
            var oldDigit = oldAmount[i] - '0';
            var newDigit = newAmount[i] - '0';
            seen++;
            if (oldDigit == newDigit) continue;

            // 走最短路径（9→0 会绕回另一端滚动，接近真实码盘进位）
            var delta = newDigit - oldDigit;
            if (delta > 5) delta -= 10;
            if (delta < -5) delta += 10;
            animations.Add((item, 10 + oldDigit, 10 + oldDigit + delta, digitCount - seen, newDigit));
        }
        if (animations.Count == 0) return;

        _amountStoryboard?.Stop();
        var sb = new Storyboard();
        foreach (var a in animations)
        {
            var durationMs = 300 + Math.Abs(a.ToIdx - a.FromIdx) * 45;
            var da = new DoubleAnimation
            {
                From = -a.FromIdx * AmountDigitHeight,
                To = -a.ToIdx * AmountDigitHeight,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                BeginTime = TimeSpan.FromMilliseconds(a.FromRight * 40),
            };
            Storyboard.SetTarget(da, a.Digit.Wheel);
            Storyboard.SetTargetProperty(da, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(da);
            a.Digit.CurrentDigit = a.NewDigit;
        }
        _amountStoryboard = sb;
        sb.Begin();
    }

    private static string ShortBalanceError(Exception ex)
    {
        if (ex is HttpRequestException hre && hre.StatusCode is System.Net.HttpStatusCode sc)
        {
            return sc switch
            {
                System.Net.HttpStatusCode.Unauthorized => "API Key 无效",
                System.Net.HttpStatusCode.Forbidden => "无权限",
                System.Net.HttpStatusCode.TooManyRequests => "请求过于频繁",
                _ => $"HTTP {(int)sc}",
            };
        }
        var msg = ex.Message;
        return msg.Length > 40 ? msg[..40] + "…" : msg;
    }

    private void CheckNotifications(ScheduleInfo phase)
    {
        if (!_config.Notify.Enabled) return;
        var isPeak = phase.IsPeak;

        if (_lastPhase is bool last && last != isPeak && _config.Notify.OnChange)
        {
            var key = phase.NextTime?.Ticks ?? -1;
            if (key >= 0 && _notifiedTicks.Add(key * 10 + 1))
            {
                ShowToast(
                    isPeak ? "DeepSeek 进入峰时" : "DeepSeek 进入谷时",
                    isPeak
                        ? "现在按全价计费（09:00-12:00 / 14:00-18:00），建议错峰使用。"
                        : "现在按半价计费，适合跑批量任务和长任务！");
            }
        }
        _lastPhase = isPeak;

        var advance = Math.Max(0, _config.Notify.AdvanceMinutes);
        if (advance > 0 && phase.NextTime is DateTime nt)
        {
            var remainMin = (nt - phase.Now).TotalMinutes;
            if (remainMin > 0 && remainMin <= advance)
            {
                var key = nt.Ticks;
                if (_notifiedTicks.Add(key * 10 + 2))
                {
                    var left = Math.Max(1, (int)Math.Ceiling(remainMin));
                    ShowToast(
                        phase.NextIsPeak ? "即将进入峰时" : "即将进入谷时",
                        phase.NextIsPeak
                            ? $"约 {left} 分钟后进入峰时（全价），有任务请尽快开始。"
                            : $"约 {left} 分钟后进入谷时（半价），可以开始跑批量任务啦！");
                }
            }
        }
    }

    private void ShowToast(string title, string body)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch { }
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 0) return "00:00:00";
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static string FormatRemain(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return "即将切换";
        var totalMin = (int)Math.Ceiling(ts.TotalMinutes);
        if (totalMin < 60) return $"还有 {totalMin} 分钟";
        var h = totalMin / 60;
        var m = totalMin % 60;
        return m == 0 ? $"还有 {h} 小时" : $"还有 {h} 小时 {m} 分";
    }

    private static Brush NewBrush(string hex)
    {
        return new SolidColorBrush(HexColor(hex));
    }

    private static Color HexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8)
        {
            return Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        }
        return Color.FromArgb(
            255,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    // ---------- 右键菜单 ----------
    private void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        UpdateDisplay();
        _ = RefreshBalanceAsync();
    }

    private void MenuMode_Click(object sender, RoutedEventArgs e)
    {
        _config.Window.Mode = _config.Window.Mode == "compact" ? "full" : "compact";
        _configService.Save(_config);
        SetMode(_config.Window.Mode);
    }

    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_config, _configService, cfg =>
        {
            _config = cfg;
            ApplyConfig();
        });
        window.Activate();
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{_configService.ConfigPath}\"",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private void MenuReload_Click(object sender, RoutedEventArgs e)
    {
        _config = _configService.Load();
        _configLastWrite = GetConfigLastWrite();
        ApplyConfig();
    }

    private void MenuPricing_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://api-docs.deepseek.com/zh-cn/quick_start/pricing/",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private void MenuQuit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void MenuAutoStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync("DeepSeekPeakStartup");
            if (task.State == Windows.ApplicationModel.StartupTaskState.Enabled ||
                task.State == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
            {
                task.Disable();
                FooterText.Text = "已关闭开机自启";
                AutoStartMenuItem.Text = "开机自启：已关闭";
            }
            else
            {
                var result = await task.RequestEnableAsync();
                FooterText.Text = result switch
                {
                    Windows.ApplicationModel.StartupTaskState.Enabled => "已开启开机自启",
                    Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => "已开启开机自启（策略）",
                    _ => "未开启开机自启",
                };
                AutoStartMenuItem.Text = result is Windows.ApplicationModel.StartupTaskState.Enabled or
                                         Windows.ApplicationModel.StartupTaskState.EnabledByPolicy
                    ? "开机自启：已开启"
                    : "开机自启：已关闭";
            }
        }
        catch (Exception ex)
        {
            FooterText.Text = $"开机自启设置失败: {ex.Message}";
        }
    }

    private async void MainMenu_Opening(object? sender, object e)
    {
        try
        {
            var t = await Windows.ApplicationModel.StartupTask.GetAsync("DeepSeekPeakStartup");
            AutoStartMenuItem.Text = t.State is Windows.ApplicationModel.StartupTaskState.Enabled or
                                     Windows.ApplicationModel.StartupTaskState.EnabledByPolicy
                ? "开机自启：已开启"
                : "开机自启：已关闭";
        }
        catch
        {
            AutoStartMenuItem.Text = "开机自启（仅安装版）";
        }
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _config.Window.PinLock = !_config.Window.PinLock;
        _configService.Save(_config);
        UpdatePinButton();
    }

    private void UpdatePinButton()
    {
        PinIcon.Glyph = _config.Window.PinLock ? "\uE840" : "\uE77A";
        if (_config.Window.PinLock)
        {
            PinBtn.Background = NewBrush("#2E7D32");
            PinIcon.Foreground = NewBrush("#FFFFFF");
        }
        else
        {
            PinBtn.Background = null;
            PinIcon.Foreground = NewBrush(IsLightMode ? "#1B1B1B" : "#E8EDF4");
        }
        ToolTipService.SetToolTip(PinBtn,
            _config.Window.PinLock ? "已锁定位置（点击解锁）" : "未锁定（点击锁定位置）");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
        public uint StateFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref RECT lprcMonitor, IntPtr dwData);

    private const uint WM_SHOWWINDOW = 0x0018;
    private const uint WM_GETMINMAXINFO = 0x0024;

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, IntPtr pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, IntPtr pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}

/// <summary>
/// 持久亚克力背景：基于官方 DesktopAcrylicController，
/// 强制 IsInputActive=true，使窗口失焦时仍保持亚克力模糊而不退化为纯色。
/// </summary>
public sealed class PersistentAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private double _tintOpacity = 0.8;
    private Color _tintColor = Color.FromArgb(255, 20, 24, 35);

    /// <summary>
    /// 调整亚克力背景 tint 的透明度（值越大背景越实，越小越通透），
    /// 只影响背景，不影响窗口内容。
    /// </summary>
    internal void SetBackground(double opacity, bool light)
    {
        _tintOpacity = Math.Clamp(opacity, 0.05, 1.0);
        var alpha = (byte)Math.Round(_tintOpacity * 255);
        _tintColor = light
            ? Color.FromArgb(alpha, 243, 243, 243) // 浅色 tint #F3F3F3
            : Color.FromArgb(alpha, 20, 24, 35);   // 深色 tint #141823（与 PVE 一致）
        if (_controller is not null)
        {
            try
            {
                _controller.TintColor = _tintColor;
                _controller.TintOpacity = 1.0f; // alpha 通道控制 tint 强度
            }
            catch { }
        }
    }

    internal void ForceInputActive()
    {
        if (_configuration is not null && _controller is not null)
        {
            try
            {
                _configuration.IsInputActive = true;
                _controller.SetSystemBackdropConfiguration(_configuration);
            }
            catch { }
        }
    }

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (_controller is not null) return;
        try
        {
            _controller = new DesktopAcrylicController();
            _configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
            _configuration.IsInputActive = true;
            _controller.AddSystemBackdropTarget(connectedTarget);
            _controller.TintColor = _tintColor;
            _controller.TintOpacity = 1.0f;
            _controller.SetSystemBackdropConfiguration(_configuration);
        }
        catch
        {
            _controller?.Dispose();
            _controller = null;
            _configuration = null;
        }
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        try
        {
            base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        }
        catch { }
        if (_configuration is not null && _controller is not null)
        {
            _configuration.IsInputActive = true;
            _controller.SetSystemBackdropConfiguration(_configuration);
        }
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }
}
