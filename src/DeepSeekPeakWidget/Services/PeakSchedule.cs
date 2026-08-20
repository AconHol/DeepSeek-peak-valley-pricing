using DeepSeekPeakWidget.Models;

namespace DeepSeekPeakWidget.Services;

/// <summary>当前时段信息。</summary>
public class ScheduleInfo
{
    public DateTime Now { get; set; }
    public bool IsPeak { get; set; }
    public DateTime SegmentStart { get; set; }
    public DateTime? NextTime { get; set; }
    public bool NextIsPeak { get; set; }
    public bool AllDayValley { get; set; }
}

/// <summary>DeepSeek 峰谷时段计算引擎。</summary>
public class PeakSchedule
{
    private readonly AppConfig _cfg;

    public PeakSchedule(AppConfig cfg)
    {
        _cfg = cfg;
    }

    /// <summary>按配置时区偏移后的“时段时间”。</summary>
    public DateTime ScheduleNow => DateTime.UtcNow.AddHours(_cfg.TimezoneOffsetHours);

    private bool DayAllValley(DateTime d) =>
        _cfg.WeekendAllValley && (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);

    private IEnumerable<(DateTime Time, bool IsPeak)> DayEvents(DateTime day)
    {
        var list = new List<(DateTime Time, bool IsPeak)> { (day.Date, false) };
        if (!DayAllValley(day))
        {
            foreach (var w in _cfg.PeakWindows)
            {
                if (TimeSpan.TryParse(w.Start, out var s) && TimeSpan.TryParse(w.End, out var e))
                {
                    list.Add((day.Date + s, true));
                    list.Add((day.Date + e, false));
                }
            }
        }
        return list.OrderBy(x => x.Time);
    }

    public bool IsPeakAt(DateTime t)
    {
        var events = DayEvents(t.Date)
            .Concat(DayEvents(t.Date.AddDays(1)))
            .OrderBy(x => x.Time)
            .ToList();
        var cur = events[0];
        foreach (var ev in events)
        {
            if (ev.Time <= t) cur = ev;
        }
        return cur.IsPeak;
    }

    public ScheduleInfo Current()
    {
        var now = ScheduleNow;
        var events = DayEvents(now.Date)
            .Concat(DayEvents(now.Date.AddDays(1)))
            .Concat(DayEvents(now.Date.AddDays(2)))
            .OrderBy(x => x.Time)
            .ToList();
        var cur = events[0];
        (DateTime Time, bool IsPeak)? nxt = null;
        foreach (var ev in events)
        {
            if (ev.Time <= now)
            {
                cur = ev;
            }
            else if (nxt is null && ev.IsPeak != cur.IsPeak)
            {
                nxt = ev;
            }
        }
        return new ScheduleInfo
        {
            Now = now,
            IsPeak = cur.IsPeak,
            SegmentStart = cur.Time,
            NextTime = nxt?.Time,
            NextIsPeak = nxt?.IsPeak ?? !cur.IsPeak,
            AllDayValley = nxt is null && !cur.IsPeak,
        };
    }

    public List<(DateTime Time, bool IsPeak)> NextTransitions(ScheduleInfo phase, int count)
    {
        var now = phase.Now;
        var events = DayEvents(now.Date)
            .Concat(DayEvents(now.Date.AddDays(1)))
            .Concat(DayEvents(now.Date.AddDays(2)))
            .OrderBy(x => x.Time)
            .ToList();
        var list = new List<(DateTime, bool)>();
        var lastIsPeak = phase.IsPeak;
        foreach (var ev in events)
        {
            if (ev.Time <= now) continue;
            if (ev.IsPeak != lastIsPeak)
            {
                list.Add(ev);
                lastIsPeak = ev.IsPeak;
                if (list.Count >= count) break;
            }
        }
        return list;
    }

    public bool IsPeakHour(int hour)
    {
        var day = ScheduleNow.Date;
        if (DayAllValley(day)) return false;
        var minOfDay = hour * 60;
        foreach (var w in _cfg.PeakWindows)
        {
            if (TimeSpan.TryParse(w.Start, out var s) && TimeSpan.TryParse(w.End, out var e))
            {
                var sh = (int)s.TotalMinutes;
                var eh = (int)e.TotalMinutes;
                if (minOfDay >= sh && minOfDay < eh) return true;
            }
        }
        return false;
    }
}
