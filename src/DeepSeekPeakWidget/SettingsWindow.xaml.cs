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
            AppWindow.Resize(new Windows.Graphics.SizeInt32(470, 640));
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
        WeekendChk.IsChecked = _config.WeekendAllValley;
        RefreshBox.Text = _config.RefreshMinutes.ToString();
        NotifyChk.IsChecked = _config.Notify.Enabled;
        AdvanceBox.Text = _config.Notify.AdvanceMinutes.ToString();
        ChangeChk.IsChecked = _config.Notify.OnChange;

        OpacitySlider.ValueChanged += (_, _) =>
        {
            OpacityText.Text = OpacitySlider.Value.ToString("0.00");
        };
        BackdropCombo.SelectionChanged += (_, _) => UpdateOpacityEnabled();
        UpdateOpacityEnabled();
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
            _config.WeekendAllValley = WeekendChk.IsChecked == true;
            _config.RefreshMinutes = Math.Max(0,
                int.TryParse(RefreshBox.Text, out var r) ? r : 30);
            _config.Notify.Enabled = NotifyChk.IsChecked == true;
            _config.Notify.AdvanceMinutes = Math.Max(0,
                int.TryParse(AdvanceBox.Text, out var adv) ? adv : 10);
            _config.Notify.OnChange = ChangeChk.IsChecked == true;

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
