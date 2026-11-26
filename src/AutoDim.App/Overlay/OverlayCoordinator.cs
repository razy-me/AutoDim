using System.Windows.Threading;
using AutoDim.App.Tracking;
using AutoDim.Core.Config;
using AutoDim.Core.Engine;

namespace AutoDim.App.Overlay;

public sealed class OverlayCoordinator : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IWindowTracker _tracker;
    private readonly IDimStateEngine _engine;
    private readonly Func<DimConfig> _getConfig;
    private readonly GpuOverlayDevice _gpu;
    private readonly Dictionary<string, DirectCompositionOverlayWindow> _overlays = new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherTimer _animTimer;
    private readonly DispatcherTimer _deadlineTimer;
    private int _sceneUpdateQueued;
    private bool _deviceLost;
    private bool _disposed;

    public event Action<bool>? PauseStateChanged;

    public bool IsPaused
    {
        get => _engine.IsPaused;
        set
        {
            if (_engine.IsPaused == value) return;
            _engine.IsPaused = value;
            PauseStateChanged?.Invoke(value);
            RequestSceneRefresh();
        }
    }

    public OverlayCoordinator(
        Dispatcher dispatcher,
        IWindowTracker tracker,
        IDimStateEngine engine,
        Func<DimConfig> getConfig)
    {
        _dispatcher = dispatcher;
        _tracker = tracker;
        _engine = engine;
        _getConfig = getConfig;
        _gpu = new GpuOverlayDevice();

        _animTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animTimer.Tick += (_, _) => RenderFrameOnly();

        _deadlineTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
        _deadlineTimer.Tick += (_, _) =>
        {
            _deadlineTimer.Stop();
            RenderFrameOnly();
        };

        _tracker.SceneInvalidated += RequestSceneRefresh;
        _tracker.DisplayOrPowerChanged += OnDisplayOrPowerChanged;
    }

    public void Start()
    {
        _tracker.Start();
        RequestSceneRefresh();
    }

    public void TogglePause() => IsPaused = !IsPaused;

    public void RequestSceneRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _sceneUpdateQueued, 1) != 0) return;

        _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
        {
            Interlocked.Exchange(ref _sceneUpdateQueued, 0);
            if (_disposed) return;

            if (_deviceLost)
            {
                HandleDeviceLost();
                if (_deviceLost) return;
            }

            DimConfig config = _getConfig();
            SceneSnapshot scene = _tracker.CaptureScene(config);
            _engine.UpdateScene(scene.Monitors, scene.ZOrderedWindows, scene.CursorPos, scene.NowMs, config);
            SyncOverlayWindows(scene.Monitors, config.ExcludeFromCapture, scene.ShellFlyoutInsertAfterHwnd);
            RenderAndScheduleTimers(scene.Monitors, scene.NowMs, config);
        });
    }

    private void RenderFrameOnly()
    {
        if (_disposed || _deviceLost) return;
        DimConfig config = _getConfig();
        long nowMs = Environment.TickCount64;
        var monitors = _overlays.Keys.ToArray();
        RenderMonitorsById(monitors, nowMs, config);
        ScheduleTimers(nowMs);
    }

    private void SyncOverlayWindows(IReadOnlyList<MonitorSnapshot> monitors, bool excludeFromCapture, nint shellFlyoutInsertAfterHwnd)
    {
        var activeIds = new HashSet<string>(monitors.Select(m => m.DeviceId), StringComparer.OrdinalIgnoreCase);

        foreach (string staleId in _overlays.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _overlays[staleId].Dispose();
            _overlays.Remove(staleId);
        }

        foreach (var mon in monitors)
        {
            if (_overlays.TryGetValue(mon.DeviceId, out var existing))
            {
                existing.Resize(mon.Bounds);
                existing.EnsureTopmost(shellFlyoutInsertAfterHwnd);
            }
            else
            {
                var created = new DirectCompositionOverlayWindow(_gpu, mon.Bounds, excludeFromCapture);
                created.EnsureTopmost(shellFlyoutInsertAfterHwnd);
                _overlays[mon.DeviceId] = created;
            }
        }
    }

    private void RenderAndScheduleTimers(IReadOnlyList<MonitorSnapshot> monitors, long nowMs, DimConfig config)
    {
        RenderMonitorsById(monitors.Select(m => m.DeviceId), nowMs, config);
        ScheduleTimers(nowMs);
    }

    private void RenderMonitorsById(IEnumerable<string> monitorIds, long nowMs, DimConfig config)
    {
        bool deviceLost = false;
        foreach (string id in monitorIds)
        {
            if (!_overlays.TryGetValue(id, out var overlay)) continue;
            var plan = _engine.ComputeDrawPlanForMonitor(id, nowMs);
            if (!overlay.Render(plan, config.OverlayColorRgb, config.ExcludeFromCapture))
            {
                deviceLost = true;
                break;
            }
        }

        if (deviceLost)
        {
            HandleDeviceLost();
            if (!_deviceLost)
            {
                RequestSceneRefresh();
            }
        }
    }

    private void ScheduleTimers(long nowMs)
    {
        if (_engine.HasActiveAnimations(nowMs))
        {
            _deadlineTimer.Stop();
            if (!_animTimer.IsEnabled)
            {
                _animTimer.Start();
            }
            return;
        }

        _animTimer.Stop();
        long? nextMs = _engine.NextDeadlineMs(nowMs);
        if (nextMs.HasValue)
        {
            long delayMs = Math.Max(10, nextMs.Value - nowMs);
            _deadlineTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
            _deadlineTimer.Start();
        }
        else
        {
            _deadlineTimer.Stop();
        }
    }

    private void HandleDeviceLost()
    {
        _deviceLost = true;
        try
        {
            foreach (var overlay in _overlays.Values)
            {
                overlay.ReleaseDeviceResources();
            }
            _gpu.Recreate();
            foreach (var overlay in _overlays.Values)
            {
                overlay.RecreateDeviceResources();
            }
            _deviceLost = false;
        }
        catch
        {
            // Next scene refresh will retry
        }
    }

    private void OnDisplayOrPowerChanged()
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() =>
        {
            foreach (var overlay in _overlays.Values)
            {
                overlay.Dispose();
            }
            _overlays.Clear();
            RequestSceneRefresh();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tracker.SceneInvalidated -= RequestSceneRefresh;
        _tracker.DisplayOrPowerChanged -= OnDisplayOrPowerChanged;

        _animTimer.Stop();
        _deadlineTimer.Stop();

        foreach (var overlay in _overlays.Values)
        {
            overlay.Dispose();
        }
        _overlays.Clear();
        _gpu.Dispose();
    }
}
