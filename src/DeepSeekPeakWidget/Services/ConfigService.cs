using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSeekPeakWidget.Models;

namespace DeepSeekPeakWidget.Services;

/// <summary>config.json 的读写。</summary>
public class ConfigService
{
    private readonly string _path;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ConfigService(string? path = null)
    {
        if (path is not null)
        {
            _path = path;
            return;
        }

        // 打包/未打包统一写入真实 LocalAppData（打包应用 exe 目录只读）
        var dir = Path.Combine(GetRealLocalAppData(), "DeepSeekPeakWidget");
        Directory.CreateDirectory(dir);
        var localPath = Path.Combine(dir, "config.json");
        if (!File.Exists(localPath))
        {
            var exeCfg = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(exeCfg))
            {
                try { File.Copy(exeCfg, localPath); } catch { }
            }
        }
        _path = localPath;
    }

    /// <summary>解析真实的 LocalAppData 路径（避开 MSIX 重定向）。</summary>
    private static string GetRealLocalAppData()
    {
        var env = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        var normal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(normal))
        {
            return normal;
        }

        try
        {
            var folderId = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"); // FOLDERID_LocalAppData
            var hr = SHGetKnownFolderPath(ref folderId, 0x00000800, IntPtr.Zero, out var path);
            if (hr == 0)
            {
                try { return Marshal.PtrToStringUni(path) ?? ""; }
                finally { Marshal.FreeCoTaskMem(path); }
            }
        }
        catch { }
        return "";
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    public string ConfigPath => _path;

    public AppConfig Load()
    {
        if (File.Exists(_path))
        {
            try
            {
                var json = File.ReadAllText(_path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions);
                if (cfg is not null)
                {
                    NormalizeWeeklyRule(cfg, json);
                    return cfg;
                }
            }
            catch
            {
                // 配置损坏时回退默认值
            }
        }

        var defaults = new AppConfig();
        Save(defaults);
        return defaults;
    }

    public void Save(AppConfig cfg)
    {
        // 别名同步：weekendAllValley 保留为“周六日是否全天谷时”，便于旧版回退兼容
        cfg.WeekendAllValley = cfg.WeekValleyDays is { Count: 7 } &&
                               cfg.WeekValleyDays[5] && cfg.WeekValleyDays[6];
        var json = JsonSerializer.Serialize(cfg, WriteOptions);
        File.WriteAllText(_path, json);
    }

    /// <summary>旧配置兼容：无 weekValleyDays 字段时，按旧 weekendAllValley 推导；仍缺失则按官方新规默认周末全天谷。</summary>
    private static void NormalizeWeeklyRule(AppConfig cfg, string rawJson)
    {
        if (cfg.WeekValleyDays is { Count: 7 }) return;

        if (rawJson.Contains("\"weekendAllValley\"", StringComparison.OrdinalIgnoreCase))
        {
            var list = new List<bool>();
            for (var i = 0; i < 7; i++)
            {
                list.Add(cfg.WeekendAllValley && i is 5 or 6);
            }
            cfg.WeekValleyDays = list;
            return;
        }

        cfg.WeekValleyDays = new List<bool> { false, false, false, false, false, true, true };
    }
}
