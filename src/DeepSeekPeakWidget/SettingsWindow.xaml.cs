using DeepSeekPeakWidget.Models;
using DeepSeekPeakWidget.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeepSeekPeakWidget;

public sealed partial class SettingsWindow : Window
{
    private readonly ConfigService _configService;
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _onSave;

    public SettingsWindow(AppConfig config, ConfigService configService, Action<AppConfig> onSave)
    {
        InitializeComponent();
        _configService = configService;
        _config = config;
        _onSave = onSave;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(SettingsTitleBar);
        if (AppWindow is not null)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(470, 720));
        }

        var light = _config.Window.ThemeMode == "light" ||
                    (_config.Window.ThemeMode == "system" && MainWindow.IsSystemLightTheme());
        RootGrid.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        RootGrid.Background = null;

        SelectComboByTag(ModeCombo, _config.Window.Mode);
        SelectComboByTag(ThemeModeCombo, _config.Window.ThemeMode);
        SelectComboByTag(BackdropCombo, _config.Window.Backdrop);
        SelectComboByTag(ThemeCombo, _config.Window.Theme);
        OpacitySlider.Value = Math.Clamp(_config.Window.Opacity, 0.2, 1.0);
        OpacityText.Text = OpacitySlider.Value.ToString("0.00");
        TopmostChk.IsChecked = _config.Window.AlwaysOnTop;
        PinLockChk.IsChecked = _config.Window.PinLock;
        WidthBox.Text = ((int)_config.Window.Width).ToString();
        HeightBox.Text = ((int)_config.Window.Height).ToString();
        ApiKeyBox.Text = _config.ApiKey ?? "";
        BalanceRefreshBox.Text = _config.BalanceRefreshSeconds.ToString();

        OffsetBox.Text = _config.TimezoneOffsetHours.ToString("0.#");
        if (_config.PeakWindows.Count > 0)
        {
            Peak1StartBox.Text = _config.PeakWindows[0].Start;
            Peak1EndBox.Text = _config.PeakWindows[0].End;
        }
        if (_config.PeakWindows.Count > 1)
        {
            Peak2StartBox.Text = _config.PeakWindows[1].Start;
            Peak2EndBox.Text = _config.PeakWindows[1].End;
        }
        var week = _config.WeekValleyDays;
        for (var i = 0; i < 7; i++)
        {
            if (RootGrid.FindName($"WeekChk{i}") is CheckBox cb)
            {
                cb.IsChecked = week is { Count: 7 } && week[i];
            }
        }
        RefreshBox.Text = _config.RefreshMinutes.ToString();
        NotifyChk.IsChecked = _config.Notify.Enabled;
        AdvanceBox.Text = _config.Notify.AdvanceMinutes.ToString();
        ChangeChk.IsChecked = _config.Notify.OnChange;
        HolidaysBox.Text = string.Join(Environment.NewLine, _config.Holidays ?? new List<string>());
        HolidayAutoChk.IsChecked = _config.HolidayAutoUpdate;
        UpdateHolidayStatusText();

        OpacitySlider.ValueChanged += (_, _) =>
        {
            OpacityText.Text = OpacitySlider.Value.ToString("0.00");
        };
        BackdropCombo.SelectionChanged += (_, _) => UpdateOpacityEnabled();
        UpdateOpacityEnabled();
    }

    /// <summary>显示上次联网更新时间与来源；没有记录时提示使用内置清单。</summary>
    private void UpdateHolidayStatusText()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_config.HolidayLastSync))
            {
                HolidaySyncText.Text = $"上次更新：{_config.HolidayLastSync}" +
                                       (string.IsNullOrWhiteSpace(_config.HolidaySource)
                                           ? ""
                                           : $" · 来源 {_config.HolidaySource}");
            }
            else
            {
                HolidaySyncText.Text = "尚未联网更新（当前使用内置清单）";
            }
        }
        catch { }
    }

    private async void HolidaySyncBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HolidaySyncBtn.IsEnabled = false;
            HolidaySyncText.Text = "正在联网更新…";
            var year = DateTime.Now.Year;
            var result = await Services.HolidayService.FetchAsync(new[] { year, year + 1 });
            if (!result.Success)
            {
                HolidaySyncText.Text = "更新失败（可稍后再试，或手动编辑清单）：" +
                                       (result.Error ?? "未知错误");
                return;
            }

            var changed = Services.HolidayService.MergeInto(_config, result);
            if (changed)
            {
                HolidaysBox.Text = string.Join(Environment.NewLine, _config.Holidays ?? new List<string>());
            }
            _configService.Save(_config);
            HolidaySyncText.Text = "已更新：" + Services.HolidayService.Describe(result);
            if (changed)
            {
                _onSave(_config);
            }
        }
        catch (Exception ex)
        {
            HolidaySyncText.Text = "更新失败：" + ex.Message;
        }
        finally
        {
            HolidaySyncBtn.IsEnabled = true;
        }
    }

    private static void SelectComboByTag(ComboBox combo, string? tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _config.Window.Mode = (ModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "full";
            _config.Window.ThemeMode = (ThemeModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "dark";
            _config.Window.Backdrop = (BackdropCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "acrylic";
            _config.Window.Theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "blue";
            _config.Window.Opacity = Math.Round(Math.Clamp(OpacitySlider.Value, 0.2, 1.0), 2);
            _config.Window.AlwaysOnTop = TopmostChk.IsChecked == true;
            _config.Window.PinLock = PinLockChk.IsChecked == true;
            _config.Window.Width = Math.Max(280, int.TryParse(WidthBox.Text, out var w) ? w : 320);
            _config.Window.Height = Math.Max(320, int.TryParse(HeightBox.Text, out var h) ? h : 600);
            _config.ApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Text)
                ? null
                : ApiKeyBox.Text.Trim();
            _config.BalanceRefreshSeconds = Math.Max(0,
                int.TryParse(BalanceRefreshBox.Text, out var br) ? br : 300);

            _config.TimezoneOffsetHours = double.TryParse(OffsetBox.Text, out var off) ? off : 8;

            var windows = new List<PeakWindow>();
            var pairs = new[]
            {
                (Peak1StartBox.Text, Peak1EndBox.Text),
                (Peak2StartBox.Text, Peak2EndBox.Text),
            };
            foreach (var (s, en) in pairs)
            {
                if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(en)) continue;
                if (TimeSpan.TryParse(s.Trim(), out var st) && TimeSpan.TryParse(en.Trim(), out var et))
                {
                    windows.Add(new PeakWindow
                    {
                        Start = DateTime.Today.Add(st).ToString("HH:mm"),
                        End = DateTime.Today.Add(et).ToString("HH:mm"),
                    });
                }
                else
                {
                    ShowInvalid();
                    return;
                }
            }
            _config.PeakWindows = windows;
            var week = new List<bool>();
            for (var i = 0; i < 7; i++)
            {
                week.Add(RootGrid.FindName($"WeekChk{i}") is CheckBox cb && cb.IsChecked == true);
            }
            _config.WeekValleyDays = week;
            _config.RefreshMinutes = Math.Max(0,
                int.TryParse(RefreshBox.Text, out var r) ? r : 30);
            _config.Notify.Enabled = NotifyChk.IsChecked == true;
            _config.Notify.AdvanceMinutes = Math.Max(0,
                int.TryParse(AdvanceBox.Text, out var adv) ? adv : 10);
            _config.Notify.OnChange = ChangeChk.IsChecked == true;

            // 法定节假日：每行一个 yyyy-MM-dd，去重并按日期排序
            var holidays = new List<string>();
            foreach (var raw in (HolidaysBox.Text ?? "").Split(new[] { '\r', '\n', ',', '，', ';', '；' },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!DateTime.TryParseExact(raw, "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var hd))
                {
                    ShowInvalid();
                    return;
                }
                var normalized = hd.ToString("yyyy-MM-dd");
                if (!holidays.Contains(normalized)) holidays.Add(normalized);
            }
            holidays.Sort(StringComparer.Ordinal);
            _config.Holidays = holidays;
            _config.HolidayAutoUpdate = HolidayAutoChk.IsChecked == true;

            _configService.Save(_config);
            _onSave(_config);
            Close();
        }
        catch
        {
            ShowInvalid();
        }
    }

    private void UpdateOpacityEnabled()
    {
        var mode = (BackdropCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "acrylic";
        var enabled = mode != "none";
        OpacitySlider.IsEnabled = enabled;
        OpacityText.Text = enabled ? OpacitySlider.Value.ToString("0.00") : "仅亚克力生效";
    }

    private void WeekendDefaultBtn_Click(object sender, RoutedEventArgs e)
    {
        for (var i = 0; i < 5; i++)
        {
            if (RootGrid.FindName($"WeekChk{i}") is CheckBox cb) cb.IsChecked = false;
        }
        for (var i = 5; i < 7; i++)
        {
            if (RootGrid.FindName($"WeekChk{i}") is CheckBox cb) cb.IsChecked = true;
        }
    }

    private void HolidayDefaultBtn_Click(object sender, RoutedEventArgs e)
    {
        HolidaysBox.Text = string.Join(Environment.NewLine, new AppConfig().Holidays);
    }

    private void ShowInvalid()
    {
        var dlg = new ContentDialog
        {
            Title = "输入有误",
            Content = "请检查时段格式（HH:mm）与数值。",
            CloseButtonText = "确定",
            XamlRoot = RootGrid.XamlRoot,
        };
        _ = dlg.ShowAsync();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
