# Review Findings — P2: Win32 Interop, Tracking & GPU Overlay

### [CRITICAL] [OPEN] P2-01 Partial failure in `HandleDeviceLost` leaves disposed GPU/swapchain resources and crashes on next render
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Overlay/OverlayCoordinator.cs:176-190` (+ `src/AutoDim.App/Overlay/GpuOverlayDevice.cs:32-36,65-74`, `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs:101-107,249-258`)
- **Source**: `r-review` (2026-11-26) | `lens: F1b`
- **Confidence**: HIGH
- **Category**: robustness
- **Scenario**: During a GPU driver reset (TDR) or display topology change, `RenderMonitorsById` detects device loss and calls `HandleDeviceLost()`. Either `_gpu.Recreate()` throws in `Initialize()` after `ReleaseResources()` already disposed `D2DContext`/`DxgiFactory`/`DCompDevice`, or `_gpu.Recreate()` succeeds and `overlay.RecreateDeviceResources()` throws on one monitor after `ReleaseCompositionResources()` already disposed `_swapChain` and set `_targetBitmap = null`. `HandleDeviceLost()` catches and swallows the exception. On the next `RequestSceneRefresh()` or `_animTimer` tick, `Render()` accesses the disposed `_gpu.D2DContext` or calls `BindSwapChainTarget()` on the disposed `_swapChain` (or renders with an old-device bitmap/brush), throwing an unhandled exception on the UI dispatcher and crashing the application.
- **Why**: `HandleDeviceLost()` swallows exceptions without tracking a `_deviceLost` state, `ReleaseResources()` / `ReleaseCompositionResources()` leave disposed COM wrappers in fields without nulling them, and `_gpu.Recreate()` runs while overlays still hold resources bound to the old device.
- **Fix**:
```diff
--- a/src/AutoDim.App/Overlay/OverlayCoordinator.cs
+++ b/src/AutoDim.App/Overlay/OverlayCoordinator.cs
@@ -17,6 +17,7 @@ public sealed class OverlayCoordinator : IDisposable
     private readonly DispatcherTimer _animTimer;
     private readonly DispatcherTimer _deadlineTimer;
     private bool _sceneUpdateQueued;
+    private bool _deviceLost;
     private bool _disposed;
@@ -80,6 +81,12 @@ public void RequestSceneRefresh()
             _sceneUpdateQueued = false;
             if (_disposed) return;
 
+            if (_deviceLost)
+            {
+                HandleDeviceLost();
+                if (_deviceLost) return;
+            }
+
             DimConfig config = _getConfig();
             SceneSnapshot scene = _tracker.CaptureScene(config);
@@ -91,7 +98,7 @@ public void RequestSceneRefresh()
     private void RenderFrameOnly()
     {
-        if (_disposed) return;
+        if (_disposed || _deviceLost) return;
         DimConfig config = _getConfig();
         long nowMs = Environment.TickCount64;
@@ -175,18 +182,25 @@ private void ScheduleTimers(long nowMs)
 
     private void HandleDeviceLost()
     {
+        _deviceLost = true;
         try
         {
+            foreach (var overlay in _overlays.Values)
+            {
+                overlay.ReleaseDeviceResources();
+            }
             _gpu.Recreate();
             foreach (var overlay in _overlays.Values)
             {
                 overlay.RecreateDeviceResources();
             }
+            _deviceLost = false;
         }
         catch
         {
             // Next scene refresh will retry
         }
     }
```
```diff
--- a/src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs
+++ b/src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs
@@ -100,9 +100,15 @@ public void Resize(IntRect monitorBounds)
     }
 
+    public void ReleaseDeviceResources()
+    {
+        if (_disposed) return;
+        ReleaseCompositionResources();
+        _hasRenderedOnce = false;
+    }
+
     public void RecreateDeviceResources()
     {
         if (_disposed) return;
         ReleaseCompositionResources();
         CreateCompositionResources();
         _hasRenderedOnce = false;
     }
@@ -249,13 +255,16 @@ private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool hand
     private void ReleaseCompositionResources()
     {
         _brush?.Dispose();
         _brush = null;
         _targetBitmap?.Dispose();
         _targetBitmap = null;
         _dcompVisual?.Dispose();
+        _dcompVisual = null!;
         _dcompTarget?.Dispose();
+        _dcompTarget = null!;
         _swapChain?.Dispose();
+        _swapChain = null!;
     }
```
```diff
--- a/src/AutoDim.App/Overlay/GpuOverlayDevice.cs
+++ b/src/AutoDim.App/Overlay/GpuOverlayDevice.cs
@@ -65,12 +65,19 @@ private void Initialize()
     private void ReleaseResources()
     {
         DCompDevice?.Dispose();
+        DCompDevice = null!;
         D2DContext?.Dispose();
+        D2DContext = null!;
         D2DDevice?.Dispose();
+        D2DDevice = null!;
         D2DFactory?.Dispose();
+        D2DFactory = null!;
         DxgiFactory?.Dispose();
+        DxgiFactory = null!;
         DxgiDevice?.Dispose();
+        DxgiDevice = null!;
         D3DDevice?.Dispose();
+        D3DDevice = null!;
     }
```

### [MAJOR] [OPEN] P2-02 `RenderMonitorsById` does not re-render overlays after `HandleDeviceLost()` recovery
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Overlay/OverlayCoordinator.cs:130-148`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: A GPU device-lost event occurs while no fade animation is running (e.g., user is reading a static window). `overlay.Render(...)` returns `false`, `RenderMonitorsById` breaks out of the loop, and `HandleDeviceLost()` recreates `_gpu` and the swap chains (with blank surfaces and `_hasRenderedOnce = false`). Because `RenderMonitorsById` returns without re-rendering or scheduling a refresh, all monitors remain un-dimmed until an unrelated window or cursor event occurs.
- **Why**: `HandleDeviceLost()` recreates empty swap chains and resets `_hasRenderedOnce`, but `RenderMonitorsById` never re-renders the current frame after successful recovery.
- **Fix**:
```diff
--- a/src/AutoDim.App/Overlay/OverlayCoordinator.cs
+++ b/src/AutoDim.App/Overlay/OverlayCoordinator.cs
@@ -143,6 +143,10 @@ private void RenderMonitorsById(IEnumerable<string> monitorIds, long nowMs, DimC
 
         if (deviceLost)
         {
             HandleDeviceLost();
+            if (!_deviceLost)
+            {
+                RequestSceneRefresh();
+            }
         }
     }
```

### [MAJOR] [OPEN] P2-03 `WindowTracker.OnWinEvent` triggers full scene refreshes for non-window child UI elements
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Tracking/WindowTracker.cs:271-279`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: performance
- **Scenario**: Any running application creates, shows, or hides a child UI element (tooltip, menu item, scrollbar, caret, or browser element), emitting `EVENT_OBJECT_DESTROY`, `EVENT_OBJECT_SHOW`, or `EVENT_OBJECT_HIDE` with `idObject != 0` or `idChild != 0`. `OnWinEvent` passes these through to `SceneInvalidated`, triggering redundant `EnumWindows` + `DwmGetWindowAttribute` + `EnumDisplayMonitors` passes on the UI thread.
- **Why**: `OnWinEvent` only checks `idObject != 0 || idChild != 0` for `EVENT_OBJECT_LOCATIONCHANGE`, omitting the same top-level window filter for `EVENT_OBJECT_DESTROY`, `EVENT_OBJECT_SHOW`, and `EVENT_OBJECT_HIDE`.
- **Fix**:
```diff
--- a/src/AutoDim.App/Tracking/WindowTracker.cs
+++ b/src/AutoDim.App/Tracking/WindowTracker.cs
@@ -270,7 +270,7 @@ private void InstallHook(uint eventMin, uint eventMax)
 
     private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
     {
-        if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE && (idObject != 0 || idChild != 0))
+        if (idObject != 0 || idChild != 0)
         {
             return;
         }
```

### [MINOR] [OPEN] P2-04 `_pidNameCache` in `WindowTracker` never invalidates exited PIDs, causing misattribution on PID reuse
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Tracking/WindowTracker.cs:93-149,219-253`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: An excluded process (e.g., `vlc.exe`) runs with PID 4096 and is cached in `_pidNameCache[4096] = "vlc.exe"`. After `vlc.exe` exits, Windows reuses PID 4096 for `chrome.exe` before `_pidNameCache.Count` exceeds 256. `GetProcessName(4096)` returns the stale `"vlc.exe"` entry, causing `chrome.exe` to be falsely treated as excluded from dimming (or vice versa).
- **Why**: `_pidNameCache` keys solely by `uint pid` and only clears when `Count > 256`, never pruning PIDs of processes that no longer have visible windows.
- **Fix**:
```diff
--- a/src/AutoDim.App/Tracking/WindowTracker.cs
+++ b/src/AutoDim.App/Tracking/WindowTracker.cs
@@ -93,6 +93,7 @@ public SceneSnapshot CaptureScene(DimConfig config)
     private List<WindowSnapshot> EnumerateWindows(nint foregroundHwnd, HashSet<string> excludedProcesses)
     {
         var list = new List<WindowSnapshot>(32);
+        var activePids = new HashSet<uint>();
 
         NativeMethods.EnumWindows((hwnd, _) =>
         {
@@ -106,6 +107,7 @@ private List<WindowSnapshot> EnumerateWindows(nint foregroundHwnd, HashSet<strin
             {
                 return true;
             }
+            activePids.Add(pid);
 
             int exStyle = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);
             if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0)
@@ -145,6 +147,14 @@ private List<WindowSnapshot> EnumerateWindows(nint foregroundHwnd, HashSet<strin
             return true;
         }, 0);
 
+        foreach (uint cachedPid in _pidNameCache.Keys)
+        {
+            if (!activePids.Contains(cachedPid))
+            {
+                _pidNameCache.Remove(cachedPid);
+            }
+        }
+
         return list;
     }
```

### [MINOR] [OPEN] P2-05 Unbounded `new string(info.szDevice)` on fixed 32-char buffer in `EnumerateMonitors`
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Tracking/WindowTracker.cs:166`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: MEDIUM
- **Category**: robustness
- **Scenario**: If `GetMonitorInfoW` ever writes a 32-character device name into `MONITORINFOEXW.szDevice` without a trailing null terminator, `new string(info.szDevice)` invokes `new string(char*)` and scans past the 32-char fixed buffer on the stack.
- **Why**: `new string(char*)` relies on null termination rather than bounding the read to the 32-element `fixed char szDevice[32]` buffer.
- **Fix**:
```diff
--- a/src/AutoDim.App/Tracking/WindowTracker.cs
+++ b/src/AutoDim.App/Tracking/WindowTracker.cs
@@ -163,7 +163,9 @@ private static List<MonitorSnapshot> EnumerateMonitors(IReadOnlyList<WindowSnaps
             }
 
             var bounds = new IntRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
-            string deviceId = new string(info.szDevice).TrimEnd('\0');
+            var deviceSpan = new ReadOnlySpan<char>(info.szDevice, 32);
+            int nullIdx = deviceSpan.IndexOf('\0');
+            string deviceId = new string(nullIdx >= 0 ? deviceSpan[..nullIdx] : deviceSpan);
 
             double dpiScale = 1.0;
```

### [MINOR] [OPEN] P2-06 Non-atomic `_sceneUpdateQueued` flag in `OverlayCoordinator.RequestSceneRefresh()`
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Overlay/OverlayCoordinator.cs:19,73-81`
- **Source**: `r-review` (2026-11-26) | `lens: F1b`
- **Confidence**: MEDIUM
- **Category**: robustness
- **Scenario**: If `RequestSceneRefresh()` is called from a non-UI thread while the UI thread is concurrently inside the `BeginInvoke` callback, the caller can observe `_sceneUpdateQueued == true` and return early right before the UI thread sets `_sceneUpdateQueued = false` and captures a stale scene, dropping the refresh.
- **Why**: `_sceneUpdateQueued` is a plain `bool` without atomic `Interlocked` synchronization.
- **Fix**:
```diff
--- a/src/AutoDim.App/Overlay/OverlayCoordinator.cs
+++ b/src/AutoDim.App/Overlay/OverlayCoordinator.cs
@@ -16,7 +16,7 @@ public sealed class OverlayCoordinator : IDisposable
 
     private readonly DispatcherTimer _animTimer;
     private readonly DispatcherTimer _deadlineTimer;
-    private bool _sceneUpdateQueued;
+    private int _sceneUpdateQueued;
     private bool _disposed;
 
     public event Action<bool>? PauseStateChanged;
@@ -72,12 +72,12 @@ public void Start()
 
     public void RequestSceneRefresh()
     {
-        if (_disposed || _sceneUpdateQueued) return;
-        _sceneUpdateQueued = true;
+        if (_disposed || Interlocked.Exchange(ref _sceneUpdateQueued, 1) != 0) return;
 
         _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
         {
-            _sceneUpdateQueued = false;
+            Interlocked.Exchange(ref _sceneUpdateQueued, 0);
             if (_disposed) return;
 
             DimConfig config = _getConfig();
```
