using System.Windows;
using System.Windows.Controls;
using AutoDim.App.Shell;
using AutoDim.Core.Config;

namespace AutoDim.App.UI;

public partial class SettingsWindow : Window
{
    public sealed class ZoneListItem
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }
        public required string Summary { get; init; }
        public bool Enabled { get; set; }
    }

    private readonly Func<DimConfig> _getConfig;
    private readonly Action<DimConfig> _applyConfig;
    private readonly Func<string, bool> _tryApplyHotkey;
    private readonly Action _launchZonePicker;
    private bool _loading;

    public SettingsWindow(
        Func<DimConfig> getConfig,
        Action<DimConfig> applyConfig,
        Func<string, bool> tryApplyHotkey,
        Action launchZonePicker)
    {
        _getConfig = getConfig;
        _applyConfig = applyConfig;
        _tryApplyHotkey = tryApplyHotkey;
        _launchZonePicker = launchZonePicker;

        InitializeComponent();
        LoadFromConfig(_getConfig());
    }

    public void RefreshFromConfig() => LoadFromConfig(_getConfig());

    private void LoadFromConfig(DimConfig cfg)
    {
        _loading = true;
        try
        {
            InactiveOpacitySlider.Value = Math.Round(cfg.InactiveWindowOpacity * 100);
            TaskbarOpacitySlider.Value = Math.Round(cfg.TaskbarOpacity * 100);
            ZoneOpacitySlider.Value = Math.Round(cfg.ZoneOpacity * 100);
            DesktopOpacitySlider.Value = Math.Round(cfg.DesktopOpacity * 100);

            DimTaskbarCheck.IsChecked = cfg.DimTaskbar;
            DimDesktopCheck.IsChecked = cfg.DimDesktop;

            HoverEnterDelaySlider.Value = cfg.HoverEnterDelayMs;
            HoverDelaySlider.Value = Math.Round(cfg.HoverExitDelayMs / 1000.0);
            UnfocusDelaySlider.Value = Math.Round(cfg.UnfocusDelayMs / 1000.0);
            FadeOutSlider.Value = cfg.FadeOutMs;
            FadeInSlider.Value = cfg.FadeInMs;
            FrameInsetSlider.Value = cfg.FrameInsetPx;

            PauseFullscreenCheck.IsChecked = cfg.PauseOnFullscreen;
            ExcludeCaptureCheck.IsChecked = cfg.ExcludeFromCapture;
            AutostartCheck.IsChecked = cfg.StartWithWindows;
            HotkeyBox.Text = cfg.ToggleHotkey;
            HotkeyStatusText.Text = string.Empty;

            ExcludedList.ItemsSource = cfg.ExcludedProcesses.ToList();
            ZonesList.ItemsSource = cfg.Zones.Select(z => new ZoneListItem
            {
                Id = z.Id,
                Name = z.Name,
                Enabled = z.Enabled,
                Summary = $"{z.MonitorDeviceId} • {(int)Math.Round(z.Bounds.Width * 100)}%×{(int)Math.Round(z.Bounds.Height * 100)}%" +
                          (string.IsNullOrEmpty(z.ProcessNameFilter) ? string.Empty : $" • {z.ProcessNameFilter}")
            }).ToList();

            UpdateLabels();
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateLabels()
    {
        InactiveOpacityLabel.Text = $"{(int)InactiveOpacitySlider.Value}%";
        TaskbarOpacityLabel.Text = $"{(int)TaskbarOpacitySlider.Value}%";
        ZoneOpacityLabel.Text = $"{(int)ZoneOpacitySlider.Value}%";
        DesktopOpacityLabel.Text = $"{(int)DesktopOpacitySlider.Value}%";
        HoverEnterDelayLabel.Text = $"{(int)HoverEnterDelaySlider.Value} ms";
        HoverDelayLabel.Text = $"{(int)HoverDelaySlider.Value} s";
        UnfocusDelayLabel.Text = $"{(int)UnfocusDelaySlider.Value} s";
        FadeOutLabel.Text = $"{(int)FadeOutSlider.Value} ms";
        FadeInLabel.Text = $"{(int)FadeInSlider.Value} ms";
        FrameInsetLabel.Text = $"{(int)FrameInsetSlider.Value} px";
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        UpdateLabels();

        DimConfig current = _getConfig();
        DimConfig updated = current with
        {
            InactiveWindowOpacity = InactiveOpacitySlider.Value / 100.0,
            TaskbarOpacity = TaskbarOpacitySlider.Value / 100.0,
            ZoneOpacity = ZoneOpacitySlider.Value / 100.0,
            DesktopOpacity = DesktopOpacitySlider.Value / 100.0,
            DimTaskbar = DimTaskbarCheck.IsChecked == true,
            DimDesktop = DimDesktopCheck.IsChecked == true,
            HoverEnterDelayMs = (int)HoverEnterDelaySlider.Value,
            HoverExitDelayMs = (int)HoverDelaySlider.Value * 1000,
            UnfocusDelayMs = (int)UnfocusDelaySlider.Value * 1000,
            FadeOutMs = (int)FadeOutSlider.Value,
            FadeInMs = (int)FadeInSlider.Value,
            FrameInsetPx = (int)FrameInsetSlider.Value,
            PauseOnFullscreen = PauseFullscreenCheck.IsChecked == true,
            ExcludeFromCapture = ExcludeCaptureCheck.IsChecked == true,
            StartWithWindows = AutostartCheck.IsChecked == true
        };

        if (updated.StartWithWindows != current.StartWithWindows)
        {
            StartupManager.SetEnabled(updated.StartWithWindows);
        }
        _applyConfig(updated);
    }

    private void OnApplyHotkeyClick(object sender, RoutedEventArgs e)
    {
        string text = HotkeyBox.Text.Trim();
        if (_tryApplyHotkey(text))
        {
            HotkeyStatusText.Text = "Aktiv";
            _applyConfig(_getConfig() with { ToggleHotkey = text });
        }
        else
        {
            HotkeyStatusText.Text = "Ungültig / belegt";
        }
    }

    private void OnAddProcessClick(object sender, RoutedEventArgs e)
    {
        string proc = ProcessInputBox.Text.Trim();
        if (string.IsNullOrEmpty(proc)) return;

        DimConfig cfg = _getConfig();
        var next = cfg.ExcludedProcesses.Append(proc).ToArray();
        ProcessInputBox.Clear();
        _applyConfig(cfg with { ExcludedProcesses = next });
        LoadFromConfig(_getConfig());
    }

    private void OnRemoveProcessClick(object sender, RoutedEventArgs e)
    {
        if (ExcludedList.SelectedItem is not string selected) return;
        DimConfig cfg = _getConfig();
        var next = cfg.ExcludedProcesses.Where(p => !string.Equals(p, selected, StringComparison.OrdinalIgnoreCase)).ToArray();
        _applyConfig(cfg with { ExcludedProcesses = next });
        LoadFromConfig(_getConfig());
    }

    private void OnAddZoneVisualClick(object sender, RoutedEventArgs e)
    {
        _launchZonePicker();
    }

    private void OnZoneToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not System.Windows.Controls.CheckBox cb || cb.DataContext is not ZoneListItem item) return;
        DimConfig cfg = _getConfig();
        var existing = cfg.Zones.FirstOrDefault(z => z.Id == item.Id);
        if (existing is null || existing.Enabled == item.Enabled) return;
        var next = cfg.Zones.Select(z => z.Id == item.Id ? z with { Enabled = item.Enabled } : z).ToArray();
        _applyConfig(cfg with { Zones = next });
    }

    private void OnDeleteZoneClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not Guid id) return;
        DimConfig cfg = _getConfig();
        var next = cfg.Zones.Where(z => z.Id != id).ToArray();
        _applyConfig(cfg with { Zones = next });
        LoadFromConfig(_getConfig());
    }

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        StartupManager.SetEnabled(DimConfig.Default.StartWithWindows);
        _tryApplyHotkey(DimConfig.Default.ToggleHotkey);
        _applyConfig(DimConfig.Default);
        LoadFromConfig(_getConfig());
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
