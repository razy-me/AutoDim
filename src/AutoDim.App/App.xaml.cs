using System.Windows;
using AutoDim.App.Overlay;
using AutoDim.App.Shell;
using AutoDim.App.Tracking;
using AutoDim.App.UI;
using AutoDim.Core.Config;
using AutoDim.Core.Engine;

namespace AutoDim.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;
    private JsonConfigStore? _configStore;
    private DimConfig _currentConfig = DimConfig.Default;
    private DimStateEngine? _engine;
    private WindowTracker? _tracker;
    private OverlayCoordinator? _coordinator;
    private GlobalHotkeyHost? _hotkeyHost;
    private TrayController? _tray;
    private SettingsWindow? _settingsWindow;
    private readonly List<ZonePickerWindow> _activePickers = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }

        _configStore = new JsonConfigStore();
        _currentConfig = _configStore.Load();

        _engine = new DimStateEngine();
        _tracker = new WindowTracker(Dispatcher);
        _coordinator = new OverlayCoordinator(Dispatcher, _tracker, _engine, () => _currentConfig);

        _hotkeyHost = new GlobalHotkeyHost();
        _hotkeyHost.TryRegister(_currentConfig.ToggleHotkey);
        _hotkeyHost.HotkeyPressed += () => _coordinator.TogglePause();

        _tray = new TrayController();
        _tray.TogglePauseRequested += () => _coordinator.TogglePause();
        _tray.OpenSettingsRequested += OpenSettings;
        _tray.OpenZonePickerRequested += LaunchZonePicker;
        _tray.ExitRequested += () => Shutdown();
        _coordinator.PauseStateChanged += paused => _tray.UpdatePauseState(paused);

        StartupManager.SetEnabled(_currentConfig.StartWithWindows);
        _coordinator.Start();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            () => _currentConfig,
            ApplyConfig,
            hotkey => _hotkeyHost?.TryRegister(hotkey) ?? false,
            LaunchZonePicker);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyConfig(DimConfig updated)
    {
        _currentConfig = updated.Validated();
        _configStore?.Save(_currentConfig);
        _coordinator?.RequestSceneRefresh();
    }

    private void LaunchZonePicker()
    {
        if (_activePickers.Count > 0 || _tracker is null || _coordinator is null) return;

        var scene = _tracker.CaptureScene(_currentConfig);
        if (scene.Monitors.Count == 0) return;

        bool wasPaused = _coordinator.IsPaused;
        _coordinator.IsPaused = true;

        int defaultIdx = _currentConfig.Zones.Count + 1;
        bool finalized = false;

        void FinishPicker(ZoneDefinition? zone)
        {
            if (finalized) return;
            finalized = true;
            ClosePickers();
            if (zone is not null)
            {
                var nextZones = _currentConfig.Zones.Append(zone).ToArray();
                ApplyConfig(_currentConfig with { Zones = nextZones });
                _settingsWindow?.RefreshFromConfig();
            }
            _coordinator.IsPaused = wasPaused;
        }

        foreach (var mon in scene.Monitors)
        {
            var picker = new ZonePickerWindow(mon, defaultIdx, FinishPicker);
            picker.Closed += (_, _) => FinishPicker(null);
            _activePickers.Add(picker);
            picker.Show();
        }
    }

    private void ClosePickers()
    {
        if (_activePickers.Count == 0) return;
        var pickers = _activePickers.ToArray();
        _activePickers.Clear();
        foreach (var p in pickers)
        {
            p.Close();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ClosePickers();
        _settingsWindow?.Close();
        _tray?.Dispose();
        _hotkeyHost?.Dispose();
        _coordinator?.Dispose();
        _tracker?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
