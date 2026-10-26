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
    /// <summary>每周规则：周一..周日是否全天按谷时（官方 2026-08-23 起周六/周日全天谷价）。</summary>
    public List<bool> WeekValleyDays { get; set; } = new() { false, false, false, false, false, true, true };
    /// <summary>旧版“周末全天谷时”开关，仅用于旧配置兼容与别名同步。</summary>
    public bool WeekendAllValley { get; set; }
    /// <summary>
    /// 法定节假日（北京时间，yyyy-MM-dd），当天全天按谷时计费。
    /// 官方规则：周一至周五（不含法定节假日）9:00-12:00、14:00-18:00 为高峰，其余时段（含周末与法定节假日全天）为空闲。
    /// 2026 年安排见国办发明电〔2025〕7 号；2027 年安排待国务院公布，这里按法定节日核心日预置。
    /// </summary>
    public List<string> Holidays { get; set; } = new()
    {
        // 2026 年（已无待放假期，保留全年便于回看/回溯）
        "2026-01-01", "2026-01-02", "2026-01-03",
        "2026-02-15", "2026-02-16", "2026-02-17", "2026-02-18", "2026-02-19",
        "2026-02-20", "2026-02-21", "2026-02-22", "2026-02-23",
        "2026-04-04", "2026-04-05", "2026-04-06",
        "2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-05-05",
        "2026-06-19", "2026-06-20", "2026-06-21",
        "2026-09-25", "2026-09-26", "2026-09-27",
        "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04", "2026-10-05",
        "2026-10-06", "2026-10-07",
        // 2027 年法定节日核心日（《全国年节及纪念日放假办法》确定；完整放假/调休安排待国务院 2026 年 11 月公布后补充）
        "2027-01-01",
        "2027-02-05", "2027-02-06", "2027-02-07", "2027-02-08",
        "2027-04-05",
        "2027-05-01", "2027-05-02",
        "2027-06-09",
        "2027-09-15",
        "2027-10-01", "2027-10-02", "2027-10-03",
    };
    public NotifyConfig Notify { get; set; } = new();
    public int RefreshMinutes { get; set; } = 30;
    /// <summary>内置价格方案版本：官方调价后更新此值，旧配置的价格会被自动替换为新价。</summary>
    public string PriceRevision { get; set; } = "2026-10-26";
    /// <summary>余额自动刷新间隔（秒，0=关闭自动刷新）。</summary>
    public int BalanceRefreshSeconds { get; set; } = 300;
    /// <summary>V4.1 Flash（官方模型名 deepseek-flash，当前版本 DeepSeek-V4.1-Flash-1017）。</summary>
    public ModelPrice Flash { get; set; } = new()
    {
        Name = "V4.1 Flash",
        HitPeak = 0.04, HitValley = 0.02,
        InputPeak = 2.00, InputValley = 1.00,
        OutputPeak = 8.00, OutputValley = 4.00,
    };
    /// <summary>V4 Pro（官方模型名 deepseek-v4-pro，当前版本 DeepSeek-V4-Pro-0813）。</summary>
    public ModelPrice Pro { get; set; } = new()
    {
        Name = "V4 Pro",
        HitPeak = 0.30, HitValley = 0.15,
        InputPeak = 9.00, InputValley = 4.50,
        OutputPeak = 27.00, OutputValley = 13.50,
    };
}
