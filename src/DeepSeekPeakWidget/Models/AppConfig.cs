namespace DeepSeekPeakWidget.Models;

/// <summary>窗口行为配置。</summary>
public class WindowConfig
{
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 600;
    public double Opacity { get; set; } = 0.97;
    public bool AlwaysOnTop { get; set; }
    public string Backdrop { get; set; } = "acrylic";
    public string Theme { get; set; } = "blue";
    public string ThemeMode { get; set; } = "dark";
    public bool PinLock { get; set; }
    public string Mode { get; set; } = "full";
    public int? Left { get; set; }
    public int? Top { get; set; }
    // 物理像素矩形（SetWindowPlacement 用，启动时原子恢复位置+大小，避免 DPI 二次缩放）
    public int? NormalLeft { get; set; }
    public int? NormalTop { get; set; }
    public int? NormalRight { get; set; }
    public int? NormalBottom { get; set; }
    // 保存时的显示器布局（布局变化则不再恢复，防止窗口跑到错误屏幕）
    public List<MonitorLayoutInfo>? MonitorLayout { get; set; }
    public string? MonitorDeviceId { get; set; }
    public string? MonitorName { get; set; }
}

/// <summary>单个显示器的完整矩形（用于校验显示器布局是否变化）。</summary>
public class MonitorLayoutInfo
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
}

/// <summary>一个峰时段（北京时间 HH:mm）。</summary>
public class PeakWindow
{
    public string Start { get; set; } = "09:00";
    public string End { get; set; } = "12:00";
}

/// <summary>提醒设置。</summary>
public class NotifyConfig
{
    public bool Enabled { get; set; } = true;
    public int AdvanceMinutes { get; set; } = 10;
    public bool OnChange { get; set; } = true;
}

/// <summary>单个模型的峰/谷单价（元 / 百万 tokens）。</summary>
public class ModelPrice
{
    public string Name { get; set; } = "";
    public double HitPeak { get; set; }
    public double HitValley { get; set; }
    public double InputPeak { get; set; }
    public double InputValley { get; set; }
    public double OutputPeak { get; set; }
    public double OutputValley { get; set; }
}

/// <summary>应用配置根对象，对应 config.json。</summary>
public class AppConfig
{
    public WindowConfig Window { get; set; } = new();
    /// <summary>DeepSeek API Key（sk-...），用于查询账户余额；留空则不显示余额。</summary>
    public string? ApiKey { get; set; }
    public double TimezoneOffsetHours { get; set; } = 8;
    public List<PeakWindow> PeakWindows { get; set; } = new()
    {
        new() { Start = "09:00", End = "12:00" },
        new() { Start = "14:00", End = "18:00" },
    };
    public bool WeekendAllValley { get; set; }
    public NotifyConfig Notify { get; set; } = new();
    public int RefreshMinutes { get; set; } = 30;
    /// <summary>余额自动刷新间隔（秒，0=关闭自动刷新）。</summary>
    public int BalanceRefreshSeconds { get; set; } = 300;
    public ModelPrice Flash { get; set; } = new()
    {
        Name = "V4 Flash",
        HitPeak = 0.10, HitValley = 0.05,
        InputPeak = 3.00, InputValley = 1.50,
        OutputPeak = 9.00, OutputValley = 4.50,
    };
    public ModelPrice Pro { get; set; } = new()
    {
        Name = "V4 Pro",
        HitPeak = 0.30, HitValley = 0.15,
        InputPeak = 9.00, InputValley = 4.50,
        OutputPeak = 27.00, OutputValley = 13.50,
    };
}
