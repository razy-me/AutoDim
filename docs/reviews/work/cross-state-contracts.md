### [MINOR] [OPEN] X-STATE-01 `HasActiveAnimations` and `NextDeadlineMs` ignore `PauseOnFullscreen`, spinning the 16 ms animation timer while fullscreen-paused
- **Status**: OPEN
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:179-184,234-255`, `src/AutoDim.App/Overlay/OverlayCoordinator.cs:150-174`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: performance
- **Scenario**: On a single-monitor setup (or when all monitors have a fullscreen app) with `PauseOnFullscreen == true`, a background window's `UnfocusDelayMs` or `HoverExitDelayMs` expires. `ComputeDrawRectsForMonitor` returns `[]` before calling `EvaluateDeadlines(nowMs)`, but `OverlayCoordinator.ScheduleTimers` then calls `HasActiveAnimations(nowMs)`, which calls `EvaluateDeadlines(nowMs)`, starts the 450 ms fade on the hidden entry, and runs `_animTimer` at 16 ms intervals for the entire fade duration while every render tick produces `[]`.
- **Why**: `ComputeDrawRectsForMonitor` checks `monitor.HasFullscreenApp && _config.PauseOnFullscreen`, whereas `HasActiveAnimations` and `NextDeadlineMs` only check `IsPaused`, allowing deadline evaluation and animation timer scheduling to run even when all monitors are fullscreen-paused.
- **Fix**:
  ```diff
  --- a/src/AutoDim.Core/Engine/DimStateEngine.cs
  +++ b/src/AutoDim.Core/Engine/DimStateEngine.cs
  @@ -231,17 +231,20 @@ public sealed class DimStateEngine : IDimStateEngine
           return OcclusionPartitioner.Partition(monitor.Bounds, layers, desktopAlpha);
       }
  
  +    private bool AreAllMonitorsPausedByFullscreen() =>
  +        _config.PauseOnFullscreen && _monitors.Count > 0 && _monitors.All(m => m.HasFullscreenApp);
  +
       public bool HasActiveAnimations(long nowMs)
       {
  -        if (IsPaused) return false;
  +        if (IsPaused || AreAllMonitorsPausedByFullscreen()) return false;
           EvaluateDeadlines(nowMs);
           return _windows.Values.Any(e => e.IsAnimating(nowMs)) ||
                  _zones.Values.Any(e => e.IsAnimating(nowMs));
       }
  
       public long? NextDeadlineMs(long nowMs)
       {
  -        if (IsPaused) return null;
  +        if (IsPaused || AreAllMonitorsPausedByFullscreen()) return null;
  
           long? earliest = null;
           foreach (var entry in _windows.Values.Concat(_zones.Values))
  ```
