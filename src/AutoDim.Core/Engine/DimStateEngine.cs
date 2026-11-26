using AutoDim.Core.Config;

namespace AutoDim.Core.Engine;

public interface IDimStateEngine
{
    bool IsPaused { get; set; }
    bool HasActiveAnimations(long nowMs);
    long? NextDeadlineMs(long nowMs);

    void UpdateScene(
        IReadOnlyList<MonitorSnapshot> monitors,
        IReadOnlyList<WindowSnapshot> zOrderedWindows,
        IntPoint cursorPos,
        long nowMs,
        DimConfig config);

    IReadOnlyList<DimDrawRect> ComputeDrawRectsForMonitor(string monitorDeviceId, long nowMs);
    MonitorDrawPlan ComputeDrawPlanForMonitor(string monitorDeviceId, long nowMs);
}

public sealed class DimStateEngine : IDimStateEngine
{
    private sealed class FadeEntry
    {
        public float TargetAlpha;
        public float DimmedTargetAlpha;
        public float AnimStartAlpha;
        public long AnimStartMs;
        public int AnimDurationMs;
        public long? DimAtMs;
        public long? HoverBrightenAtMs;
        public bool WasFocused;
        public bool WasHovered;
        public bool Initialized;

        public float SampleAlpha(long nowMs)
        {
            if (AnimDurationMs <= 0 || nowMs >= AnimStartMs + AnimDurationMs)
            {
                return TargetAlpha;
            }

            if (nowMs <= AnimStartMs)
            {
                return AnimStartAlpha;
            }

            float t = Math.Clamp((float)(nowMs - AnimStartMs) / AnimDurationMs, 0f, 1f);
            float eased = 1f - MathF.Pow(1f - t, 3f);
            return AnimStartAlpha + (TargetAlpha - AnimStartAlpha) * eased;
        }

        public bool IsAnimating(long nowMs) =>
            AnimDurationMs > 0 && nowMs < AnimStartMs + AnimDurationMs &&
            MathF.Abs(TargetAlpha - AnimStartAlpha) > 0.0001f;

        public void SetTarget(float newTarget, long nowMs, int fadeOutMs, int fadeInMs, long? animStartOverrideMs = null)
        {
            if (MathF.Abs(newTarget - TargetAlpha) < 0.0001f)
            {
                return;
            }

            float current = SampleAlpha(nowMs);
            AnimStartAlpha = current;
            AnimStartMs = animStartOverrideMs ?? nowMs;
            TargetAlpha = newTarget;
            AnimDurationMs = newTarget > current ? fadeOutMs : fadeInMs;
        }
    }

    private readonly Dictionary<nint, FadeEntry> _windows = new();
    private readonly Dictionary<Guid, FadeEntry> _zones = new();
    private readonly HashSet<nint> _liveHandles = new();
    private readonly List<nint> _staleHandles = new();
    private readonly HashSet<Guid> _liveZones = new();
    private readonly List<Guid> _staleZones = new();
    private readonly Dictionary<string, MonitorSnapshot> _monitorById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Guid Id, string MonitorId, IntRect Bounds)> _activeZones = [];
    private readonly List<(IntRect Bounds, float Alpha)> _layersScratch = [];
    private readonly List<(IntRect Bounds, int LayerIndex, float Alpha)> _roundedCandidatesScratch = [];
    private readonly List<IntRect> _occludersScratch = [];
    private readonly List<IntRect> _occluderRemScratch = [];
    private readonly List<IntRect> _occluderNextScratch = [];
    private readonly List<DimDrawRect> _rectsScratch = new(32);
    private readonly List<DimCornerSpandrel> _cornersScratch = new(16);
    private readonly List<IntRect> _partRemScratch = new(24);
    private readonly List<IntRect> _partNextScratch = new(24);

    private DimConfig? _lastRawConfig;
    private DimConfig _config = DimConfig.Default;
    private IReadOnlyList<MonitorSnapshot> _monitors = [];
    private IReadOnlyList<WindowSnapshot> _zOrderedWindows = [];

    public bool IsPaused { get; set; }

    public void UpdateScene(
        IReadOnlyList<MonitorSnapshot> monitors,
        IReadOnlyList<WindowSnapshot> zOrderedWindows,
        IntPoint cursorPos,
        long nowMs,
        DimConfig config)
    {
        if (!ReferenceEquals(config, _lastRawConfig))
        {
            _lastRawConfig = config;
            _config = config.Validated();
        }

        _monitors = monitors;
        _zOrderedWindows = zOrderedWindows;

        // Top-most non-desktop window under cursor gets hover presence
        nint hoveredHandle = 0;
        string? focusedProcess = null;
        for (int i = 0; i < zOrderedWindows.Count; i++)
        {
            var win = zOrderedWindows[i];
            if (win.IsFocused && !win.IsDesktop)
            {
                focusedProcess ??= win.ProcessName;
            }

            if (hoveredHandle == 0 && !win.IsDesktop && win.Bounds.Contains(cursorPos))
            {
                hoveredHandle = win.Handle;
            }
        }

        _liveHandles.Clear();
        for (int i = 0; i < zOrderedWindows.Count; i++)
        {
            var win = zOrderedWindows[i];
            _liveHandles.Add(win.Handle);
            if (!_windows.TryGetValue(win.Handle, out var entry))
            {
                entry = new FadeEntry();
                _windows[win.Handle] = entry;
            }

            bool isHovered = win.Handle == hoveredHandle || (win.IsDesktop && hoveredHandle == 0 && win.Bounds.Contains(cursorPos));
            UpdateWindowEntry(entry, win, isHovered, nowMs);
        }

        if (_windows.Count > _liveHandles.Count)
        {
            _staleHandles.Clear();
            foreach (nint key in _windows.Keys)
            {
                if (!_liveHandles.Contains(key)) _staleHandles.Add(key);
            }
            for (int i = 0; i < _staleHandles.Count; i++)
            {
                _windows.Remove(_staleHandles[i]);
            }
        }

        // Resolve active custom zones on current monitors
        _activeZones.Clear();
        _liveZones.Clear();
        _monitorById.Clear();
        for (int i = 0; i < monitors.Count; i++)
        {
            _monitorById.TryAdd(monitors[i].DeviceId, monitors[i]);
        }

        var zones = _config.Zones;
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (!zone.Enabled || !_monitorById.TryGetValue(zone.MonitorDeviceId, out var mon))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(zone.ProcessNameFilter) &&
                !string.Equals(focusedProcess, zone.ProcessNameFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IntRect pixelBounds = IntRect.FromNormalized(mon.Bounds, zone.Bounds);
            if (pixelBounds.IsEmpty)
            {
                continue;
            }

            _liveZones.Add(zone.Id);
            _activeZones.Add((zone.Id, mon.DeviceId, pixelBounds));

            if (!_zones.TryGetValue(zone.Id, out var zEntry))
            {
                zEntry = new FadeEntry();
                _zones[zone.Id] = zEntry;
            }

            float dimOpacity = (float)(zone.OpacityOverride ?? _config.ZoneOpacity);
            bool zHovered = pixelBounds.Contains(cursorPos);
            UpdateHoverOnlyEntry(zEntry, isFocused: false, isHovered: zHovered, isEnabled: true, dimOpacity, _config.HoverExitDelayMs, nowMs);
        }

        if (_zones.Count > _liveZones.Count)
        {
            _staleZones.Clear();
            foreach (Guid key in _zones.Keys)
            {
                if (!_liveZones.Contains(key)) _staleZones.Add(key);
            }
            for (int i = 0; i < _staleZones.Count; i++)
            {
                _zones.Remove(_staleZones[i]);
            }
        }
    }

    public IReadOnlyList<DimDrawRect> ComputeDrawRectsForMonitor(string monitorDeviceId, long nowMs) =>
        ComputeDrawPlanForMonitor(monitorDeviceId, nowMs).Rects;

    public MonitorDrawPlan ComputeDrawPlanForMonitor(string monitorDeviceId, long nowMs)
    {
        if (IsPaused)
        {
            return new MonitorDrawPlan([], []);
        }

        MonitorSnapshot? monitor = null;
        for (int i = 0; i < _monitors.Count; i++)
        {
            if (string.Equals(_monitors[i].DeviceId, monitorDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                monitor = _monitors[i];
                break;
            }
        }

        if (monitor is null || (monitor.HasFullscreenApp && _config.PauseOnFullscreen))
        {
            return new MonitorDrawPlan([], []);
        }

        EvaluateDeadlines(nowMs);

        _layersScratch.Clear();
        _occludersScratch.Clear();

        // 1. Only unoccluded portions of excluded windows (not covered by higher-Z non-excluded windows) sit at top with alpha = 0
        for (int i = 0; i < _zOrderedWindows.Count; i++)
        {
            var win = _zOrderedWindows[i];
            if (win.IsDesktop)
            {
                continue;
            }

            IntRect effectiveBounds = win.IsTaskbar ? win.Bounds : ApplyFrameInset(win.Bounds, monitor.Bounds, _config.FrameInsetPx);
            if (effectiveBounds.IsEmpty)
            {
                continue;
            }

            if (!win.IsExcluded)
            {
                _occludersScratch.Add(effectiveBounds);
                continue;
            }

            SubtractOccludersInto(effectiveBounds, _occludersScratch, _layersScratch);
        }

        // 2. Enabled custom static zones sit above regular windows
        for (int i = 0; i < _activeZones.Count; i++)
        {
            var (zoneId, zoneMonId, bounds) = _activeZones[i];
            if (string.Equals(zoneMonId, monitor.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                _zones.TryGetValue(zoneId, out var zEntry))
            {
                _layersScratch.Add((bounds, zEntry.SampleAlpha(nowMs)));
            }
        }

        // 3. Regular non-excluded windows in top-to-bottom Z-order
        float desktopAlpha = _config.DimDesktop ? (float)_config.DesktopOpacity : 0f;
        _roundedCandidatesScratch.Clear();
        for (int i = 0; i < _zOrderedWindows.Count; i++)
        {
            var win = _zOrderedWindows[i];
            if (win.IsDesktop)
            {
                if (_windows.TryGetValue(win.Handle, out var dEntry))
                {
                    desktopAlpha = dEntry.SampleAlpha(nowMs);
                }
                continue;
            }

            if (win.IsExcluded)
            {
                continue;
            }

            if (_windows.TryGetValue(win.Handle, out var entry))
            {
                IntRect effectiveBounds = win.IsTaskbar ? win.Bounds : ApplyFrameInset(win.Bounds, monitor.Bounds, _config.FrameInsetPx);
                if (!effectiveBounds.IsEmpty)
                {
                    float alpha = entry.SampleAlpha(nowMs);
                    int idx = _layersScratch.Count;
                    _layersScratch.Add((effectiveBounds, alpha));
                    if (!win.IsTaskbar && alpha < 0.98f)
                    {
                        _roundedCandidatesScratch.Add((effectiveBounds, idx, alpha));
                    }
                }
            }
        }

        OcclusionPartitioner.PartitionInto(monitor.Bounds, _layersScratch, desktopAlpha, _rectsScratch, _partRemScratch, _partNextScratch);
        DimDrawRect[] rects = _rectsScratch.Count == 0 ? [] : _rectsScratch.ToArray();
        var corners = ComputeRoundedCorners(monitor, _layersScratch, _roundedCandidatesScratch, desktopAlpha);
        return new MonitorDrawPlan(rects, corners);
    }

    private IReadOnlyList<DimCornerSpandrel> ComputeRoundedCorners(
        MonitorSnapshot monitor,
        List<(IntRect Bounds, float Alpha)> layers,
        List<(IntRect Bounds, int LayerIndex, float Alpha)> candidates,
        float desktopAlpha)
    {
        int radiusPx = (int)Math.Round(_config.CornerRadiusDip * monitor.DpiScale);
        if (radiusPx < 2 || candidates.Count == 0)
        {
            return [];
        }

        _cornersScratch.Clear();
        for (int c = 0; c < candidates.Count; c++)
        {
            var (bounds, layerIdx, winAlpha) = candidates[c];
            if (bounds.Width < radiusPx * 2 || bounds.Height < radiusPx * 2) continue;

            TryAddCorner(CornerKind.TopLeft, new IntRect(bounds.Left, bounds.Top, bounds.Left + radiusPx, bounds.Top + radiusPx), new IntPoint(bounds.Left, bounds.Top), bounds.Left > monitor.Bounds.Left && bounds.Top > monitor.Bounds.Top);
            TryAddCorner(CornerKind.TopRight, new IntRect(bounds.Right - radiusPx, bounds.Top, bounds.Right, bounds.Top + radiusPx), new IntPoint(bounds.Right - 1, bounds.Top), bounds.Right < monitor.Bounds.Right && bounds.Top > monitor.Bounds.Top);
            TryAddCorner(CornerKind.BottomLeft, new IntRect(bounds.Left, bounds.Bottom - radiusPx, bounds.Left + radiusPx, bounds.Bottom), new IntPoint(bounds.Left, bounds.Bottom - 1), bounds.Left > monitor.Bounds.Left && bounds.Bottom < monitor.Bounds.Bottom);
            TryAddCorner(CornerKind.BottomRight, new IntRect(bounds.Right - radiusPx, bounds.Bottom - radiusPx, bounds.Right, bounds.Bottom), new IntPoint(bounds.Right - 1, bounds.Bottom - 1), bounds.Right < monitor.Bounds.Right && bounds.Bottom < monitor.Bounds.Bottom);

            void TryAddCorner(CornerKind kind, IntRect cornerRect, IntPoint samplePt, bool insideMonitor)
            {
                if (!insideMonitor || !monitor.Bounds.Contains(samplePt)) return;

                for (int i = 0; i < layerIdx; i++)
                {
                    if (layers[i].Bounds.Contains(samplePt)) return;
                }

                float bgAlpha = desktopAlpha;
                for (int i = layerIdx + 1; i < layers.Count; i++)
                {
                    if (layers[i].Bounds.Contains(samplePt))
                    {
                        bgAlpha = layers[i].Alpha;
                        break;
                    }
                }

                if (bgAlpha > winAlpha + 0.001f)
                {
                    float remainingAlpha = Math.Clamp((bgAlpha - winAlpha) / (1f - winAlpha), 0f, 0.99f);
                    _cornersScratch.Add(new DimCornerSpandrel(cornerRect, kind, radiusPx, remainingAlpha));
                }
            }
        }

        return _cornersScratch.Count == 0 ? [] : _cornersScratch.ToArray();
    }

    private static IntRect ApplyFrameInset(IntRect bounds, IntRect monitorBounds, int insetPx)
    {
        if (insetPx <= 0 || bounds.IsEmpty) return bounds;

        int l = bounds.Left > monitorBounds.Left ? bounds.Left + insetPx : bounds.Left;
        int t = bounds.Top > monitorBounds.Top ? bounds.Top + insetPx : bounds.Top;
        int r = bounds.Right < monitorBounds.Right ? bounds.Right - insetPx : bounds.Right;
        int b = bounds.Bottom < monitorBounds.Bottom ? bounds.Bottom - insetPx : bounds.Bottom;
        return r > l && b > t ? new IntRect(l, t, r, b) : default;
    }

    public bool HasActiveAnimations(long nowMs)
    {
        if (IsPaused || AreAllMonitorsPausedByFullscreen()) return false;
        EvaluateDeadlines(nowMs);

        foreach (var entry in _windows.Values)
        {
            if (entry.IsAnimating(nowMs)) return true;
        }
        foreach (var entry in _zones.Values)
        {
            if (entry.IsAnimating(nowMs)) return true;
        }
        return false;
    }

    public long? NextDeadlineMs(long nowMs)
    {
        if (IsPaused || AreAllMonitorsPausedByFullscreen()) return null;

        long? earliest = null;
        foreach (var entry in _windows.Values)
        {
            if (entry.DimAtMs.HasValue && entry.DimAtMs.Value > nowMs)
                earliest = earliest.HasValue ? Math.Min(earliest.Value, entry.DimAtMs.Value) : entry.DimAtMs.Value;
            if (entry.HoverBrightenAtMs.HasValue && entry.HoverBrightenAtMs.Value > nowMs)
                earliest = earliest.HasValue ? Math.Min(earliest.Value, entry.HoverBrightenAtMs.Value) : entry.HoverBrightenAtMs.Value;
        }
        foreach (var entry in _zones.Values)
        {
            if (entry.DimAtMs.HasValue && entry.DimAtMs.Value > nowMs)
                earliest = earliest.HasValue ? Math.Min(earliest.Value, entry.DimAtMs.Value) : entry.DimAtMs.Value;
            if (entry.HoverBrightenAtMs.HasValue && entry.HoverBrightenAtMs.Value > nowMs)
                earliest = earliest.HasValue ? Math.Min(earliest.Value, entry.HoverBrightenAtMs.Value) : entry.HoverBrightenAtMs.Value;
        }
        return earliest;
    }

    private bool AreAllMonitorsPausedByFullscreen()
    {
        if (!_config.PauseOnFullscreen || _monitors.Count == 0) return false;
        for (int i = 0; i < _monitors.Count; i++)
        {
            if (!_monitors[i].HasFullscreenApp) return false;
        }
        return true;
    }

    private void SubtractOccludersInto(IntRect bounds, List<IntRect> occluders, List<(IntRect Bounds, float Alpha)> output)
    {
        if (bounds.IsEmpty) return;
        if (occluders.Count == 0)
        {
            output.Add((bounds, 0f));
            return;
        }

        _occluderRemScratch.Clear();
        _occluderRemScratch.Add(bounds);

        for (int i = 0; i < occluders.Count; i++)
        {
            var occ = occluders[i];
            _occluderNextScratch.Clear();
            for (int r = 0; r < _occluderRemScratch.Count; r++)
            {
                var rem = _occluderRemScratch[r];
                if (!rem.Intersects(occ))
                {
                    _occluderNextScratch.Add(rem);
                    continue;
                }

                IntRect cut = rem.Intersect(occ);
                if (cut.Top > rem.Top) _occluderNextScratch.Add(new IntRect(rem.Left, rem.Top, rem.Right, cut.Top));
                if (cut.Bottom < rem.Bottom) _occluderNextScratch.Add(new IntRect(rem.Left, cut.Bottom, rem.Right, rem.Bottom));
                if (cut.Left > rem.Left) _occluderNextScratch.Add(new IntRect(rem.Left, cut.Top, cut.Left, cut.Bottom));
                if (cut.Right < rem.Right) _occluderNextScratch.Add(new IntRect(cut.Right, cut.Top, rem.Right, cut.Bottom));
            }

            _occluderRemScratch.Clear();
            _occluderRemScratch.AddRange(_occluderNextScratch);
            if (_occluderRemScratch.Count == 0) break;
        }

        for (int i = 0; i < _occluderRemScratch.Count; i++)
        {
            output.Add((_occluderRemScratch[i], 0f));
        }
    }

    private void EvaluateDeadlines(long nowMs)
    {
        foreach (var entry in _windows.Values)
        {
            if (entry.HoverBrightenAtMs.HasValue && nowMs >= entry.HoverBrightenAtMs.Value)
            {
                long dwellMs = entry.HoverBrightenAtMs.Value;
                entry.HoverBrightenAtMs = null;
                entry.DimAtMs = null;
                entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs, animStartOverrideMs: dwellMs);
            }
            else if (entry.DimAtMs.HasValue && nowMs >= entry.DimAtMs.Value)
            {
                long deadlineMs = entry.DimAtMs.Value;
                entry.DimAtMs = null;
                entry.SetTarget(entry.DimmedTargetAlpha, nowMs, _config.FadeOutMs, _config.FadeInMs, animStartOverrideMs: deadlineMs);
            }
        }
        foreach (var entry in _zones.Values)
        {
            if (entry.HoverBrightenAtMs.HasValue && nowMs >= entry.HoverBrightenAtMs.Value)
            {
                long dwellMs = entry.HoverBrightenAtMs.Value;
                entry.HoverBrightenAtMs = null;
                entry.DimAtMs = null;
                entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs, animStartOverrideMs: dwellMs);
            }
            else if (entry.DimAtMs.HasValue && nowMs >= entry.DimAtMs.Value)
            {
                long deadlineMs = entry.DimAtMs.Value;
                entry.DimAtMs = null;
                entry.SetTarget(entry.DimmedTargetAlpha, nowMs, _config.FadeOutMs, _config.FadeInMs, animStartOverrideMs: deadlineMs);
            }
        }
    }

    private void UpdateWindowEntry(FadeEntry entry, WindowSnapshot win, bool isHovered, long nowMs)
    {
        if (win.IsDesktop)
        {
            UpdateHoverOnlyEntry(
                entry,
                isFocused: win.IsFocused,
                isHovered: isHovered,
                isEnabled: _config.DimDesktop && !win.IsExcluded,
                dimOpacity: (float)_config.DesktopOpacity,
                hoverExitDelayMs: _config.HoverExitDelayMs,
                nowMs: nowMs);
            return;
        }

        if (win.IsTaskbar)
        {
            UpdateHoverOnlyEntry(
                entry,
                isFocused: win.IsFocused,
                isHovered: isHovered,
                isEnabled: _config.DimTaskbar && !win.IsExcluded,
                dimOpacity: (float)_config.TaskbarOpacity,
                hoverExitDelayMs: _config.HoverExitDelayMs,
                nowMs: nowMs);
            return;
        }

        if (win.IsExcluded || win.IsFocused)
        {
            entry.HoverBrightenAtMs = null;
            entry.DimAtMs = null;
            entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs);
        }
        else if (isHovered)
        {
            ApplyHoverDwell(entry, nowMs);
        }
        else
        {
            entry.HoverBrightenAtMs = null;
            entry.DimmedTargetAlpha = ResolveWindowDimOpacity(win);
            if (!entry.Initialized || entry.WasFocused)
            {
                entry.DimAtMs = nowMs + _config.UnfocusDelayMs;
                entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs);
            }
            else if (entry.WasHovered && entry.TargetAlpha <= 0.001f)
            {
                entry.DimAtMs = nowMs + _config.HoverExitDelayMs;
                entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs);
            }
            else if (entry.DimAtMs.HasValue && nowMs >= entry.DimAtMs.Value)
            {
                entry.DimAtMs = null;
                entry.SetTarget(entry.DimmedTargetAlpha, nowMs, _config.FadeOutMs, _config.FadeInMs);
            }
            else if (!entry.DimAtMs.HasValue)
            {
                entry.SetTarget(entry.DimmedTargetAlpha, nowMs, _config.FadeOutMs, _config.FadeInMs);
            }
        }

        entry.WasFocused = win.IsFocused;
        entry.WasHovered = isHovered;
        entry.Initialized = true;
    }

    private void UpdateHoverOnlyEntry(
        FadeEntry entry,
        bool isFocused,
        bool isHovered,
        bool isEnabled,
        float dimOpacity,
        int hoverExitDelayMs,
        long nowMs)
    {
        if (!isEnabled || isFocused)
        {
            entry.HoverBrightenAtMs = null;
            entry.DimAtMs = null;
            entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs);
        }
        else if (isHovered)
        {
            ApplyHoverDwell(entry, nowMs);
        }
        else
        {
            entry.HoverBrightenAtMs = null;
            entry.DimmedTargetAlpha = dimOpacity;
            if (!entry.Initialized || entry.WasFocused || (entry.WasHovered && entry.TargetAlpha <= 0.001f))
            {
                entry.DimAtMs = nowMs + hoverExitDelayMs;
                entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs);
            }
            else if (entry.DimAtMs.HasValue && nowMs >= entry.DimAtMs.Value)
            {
                entry.DimAtMs = null;
                entry.SetTarget(dimOpacity, nowMs, _config.FadeOutMs, _config.FadeInMs);
            }
            else if (!entry.DimAtMs.HasValue)
            {
                entry.SetTarget(dimOpacity, nowMs, _config.FadeOutMs, _config.FadeInMs);
            }
        }

        entry.WasFocused = isFocused;
        entry.WasHovered = isHovered;
        entry.Initialized = true;
    }

    private void ApplyHoverDwell(FadeEntry entry, long nowMs)
    {
        if (entry.TargetAlpha <= 0.001f || _config.HoverEnterDelayMs <= 0)
        {
            entry.HoverBrightenAtMs = null;
            entry.DimAtMs = null;
            entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs);
            return;
        }

        if (!entry.HoverBrightenAtMs.HasValue)
        {
            entry.HoverBrightenAtMs = nowMs + _config.HoverEnterDelayMs;
        }
        else if (nowMs >= entry.HoverBrightenAtMs.Value)
        {
            long dwellMs = entry.HoverBrightenAtMs.Value;
            entry.HoverBrightenAtMs = null;
            entry.DimAtMs = null;
            entry.SetTarget(0f, nowMs, _config.FadeOutMs, _config.FadeInMs, animStartOverrideMs: dwellMs);
        }
    }

    private float ResolveWindowDimOpacity(WindowSnapshot win)
    {
        var mults = _config.ProcessOpacityMultipliers;
        if (mults is not null && mults.Count > 0 && !string.IsNullOrEmpty(win.ProcessName))
        {
            if (mults.TryGetValue(win.ProcessName, out double m))
            {
                return (float)(_config.InactiveWindowOpacity * m);
            }

            string noExt = Path.GetFileNameWithoutExtension(win.ProcessName);
            if (!string.IsNullOrEmpty(noExt) && mults.TryGetValue(noExt, out m))
            {
                return (float)(_config.InactiveWindowOpacity * m);
            }
        }

        return (float)_config.InactiveWindowOpacity;
    }
}
