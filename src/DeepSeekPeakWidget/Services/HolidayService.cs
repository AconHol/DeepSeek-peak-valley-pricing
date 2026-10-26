using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using DeepSeekPeakWidget.Models;

namespace DeepSeekPeakWidget.Services;

/// <summary>一次节假日联网抓取的结果。</summary>
public sealed class HolidayFetchResult
{
    public bool Success { get; init; }
    public string Source { get; init; } = "";
    public string? Error { get; init; }
    /// <summary>年份 -> 该年法定节假日日期（yyyy-MM-dd，升序）。空列表表示官方尚未公布。</summary>
    public Dictionary<int, List<string>> DatesByYear { get; init; } = new();
    public int TotalCount => DatesByYear.Values.Sum(list => list.Count);
}

/// <summary>
/// 法定节假日联网抓取：优先国内可访问的 timor.tech，其次 GitHub 上的 holiday-cn 数据（含国务院通知链接）。
/// 抓不到时由调用方继续使用本地内置清单，保证断网可用。
/// </summary>
public static class HolidayService
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>数据源顺序（前面的优先）。</summary>
    private static readonly string[] Sources = { "timor.tech", "holiday-cn" };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DeepSeekPeakWidget");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json,*/*");
        return client;
    }

    /// <summary>抓取指定年份的法定节假日；某个年份官方未公布时返回空列表而不是失败。</summary>
    public static async Task<HolidayFetchResult> FetchAsync(IEnumerable<int> years, CancellationToken cancellationToken = default)
    {
        var yearList = years.Distinct().Where(y => y >= 2000 && y <= 2100).OrderBy(y => y).ToList();
        var errors = new List<string>();

        foreach (var source in Sources)
        {
            try
            {
                var datesByYear = new Dictionary<int, List<string>>();
                foreach (var year in yearList)
                {
                    var dates = source == "timor.tech"
                        ? await FetchFromTimorAsync(year, cancellationToken)
                        : await FetchFromHolidayCnAsync(year, cancellationToken);
                    datesByYear[year] = dates;
                }

                // 所有年份都为空：官方尚未公布（例如 2027 年通知未发布）。这不算失败，但没有可写入的数据。
                if (datesByYear.Values.Any(list => list.Count > 0))
                {
                    return new HolidayFetchResult
                    {
                        Success = true,
                        Source = source,
                        DatesByYear = datesByYear,
                    };
                }
                errors.Add($"{source}: 返回空数据（官方可能尚未公布）");
            }
            catch (Exception ex)
            {
                errors.Add($"{source}: {ex.Message}");
            }
        }

        return new HolidayFetchResult
        {
            Success = false,
            Error = string.Join("；", errors),
        };
    }

    /// <summary>把联网结果合并进配置：有数据的年份整体替换，没有数据的年份保留本地清单。
    /// 成功时总会记录同步时间与来源；返回节假日清单是否真的发生变化。</summary>
    public static bool MergeInto(AppConfig config, HolidayFetchResult result)
    {
        if (!result.Success)
        {
            return false;
        }

        var merged = new List<string>(config.Holidays ?? new List<string>());
        var changed = false;

        foreach (var pair in result.DatesByYear)
        {
            if (pair.Value.Count == 0)
            {
                continue; // 官方尚未公布：保留本机内置/手工清单
            }

            var prefix = pair.Key.ToString("0000", CultureInfo.InvariantCulture) + "-";
            var oldDates = merged.Where(d => d.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToList();
            var newDates = pair.Value.Distinct().OrderBy(d => d, StringComparer.Ordinal).ToList();
            if (oldDates.SequenceEqual(newDates))
            {
                continue;
            }

            merged.RemoveAll(d => d.StartsWith(prefix, StringComparison.Ordinal));
            merged.AddRange(newDates);
            changed = true;
        }

        if (changed)
        {
            merged.Sort(StringComparer.Ordinal);
            config.Holidays = merged;
        }

        config.HolidayLastSync = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        config.HolidaySource = result.Source;
        return changed;
    }

    /// <summary>摘要文本，用于设置界面显示（例如「2026 年 33 天；2027 年待公布」）。</summary>
    public static string Describe(HolidayFetchResult result)
    {
        if (!result.Success)
        {
            return "更新失败：" + (result.Error ?? "未知错误");
        }

        var parts = result.DatesByYear
            .OrderBy(p => p.Key)
            .Select(p => p.Value.Count > 0 ? $"{p.Key} 年 {p.Value.Count} 天" : $"{p.Key} 年待公布")
            .ToList();
        return $"{string.Join("；", parts)}（来源：{result.Source}）";
    }

    // ---------- timor.tech ----------

    private static async Task<List<string>> FetchFromTimorAsync(int year, CancellationToken cancellationToken)
    {
        var url = $"https://timor.tech/api/holiday/year/{year}";
        var json = await Http.GetStringAsync(url, cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var result = new List<string>();
        if (!doc.RootElement.TryGetProperty("holiday", out var holiday) ||
            holiday.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var item in holiday.EnumerateObject())
        {
            var value = item.Value;
            if (value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (!value.TryGetProperty("holiday", out var isHoliday) || isHoliday.ValueKind != JsonValueKind.True)
            {
                continue;
            }
            var date = value.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString()
                : $"{year}-{item.Name}";
            if (TryNormalizeDate(date, out var normalized))
            {
                result.Add(normalized);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result.Distinct().ToList();
    }

    // ---------- holiday-cn（GitHub 开源数据，含国务院通知链接） ----------

    private static async Task<List<string>> FetchFromHolidayCnAsync(int year, CancellationToken cancellationToken)
    {
        var url = $"https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{year}.json";
        var json = await Http.GetStringAsync(url, cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var result = new List<string>();
        if (!doc.RootElement.TryGetProperty("days", out var days) || days.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var day in days.EnumerateArray())
        {
            if (day.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (!day.TryGetProperty("isOffDay", out var off) || off.ValueKind != JsonValueKind.True)
            {
                continue;
            }
            if (day.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String &&
                TryNormalizeDate(d.GetString(), out var normalized))
            {
                result.Add(normalized);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result.Distinct().ToList();
    }

    private static bool TryNormalizeDate(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        if (DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }
        return false;
    }
}
