# Review Findings — P1 Core Config & Engine

### [MAJOR] [OPEN] P1-01 Excluded desktop window still dims desktop at DesktopOpacity
- **Status**: OPEN
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:211-223` (also `src/AutoDim.Core/Engine/DimStateEngine.cs:272-277`)
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User adds `explorer.exe` to `DimConfig.ExcludedProcesses`. `WindowSnapshot` for the desktop (`IsDesktop = true`, `ProcessName = "explorer.exe"`) has `IsExcluded = true`. `UpdateWindowEntry` sets the desktop `FadeEntry` target to `0f`, but `ComputeDrawRectsForMonitor` still emits `desktopAlpha = _config.DesktopOpacity` (`0.85f`), dimming the desktop.
- **Why**: `ComputeDrawRectsForMonitor` initializes `desktopAlpha` to `_config.DimDesktop ? (float)_config.DesktopOpacity : 0f` and then checks `if (win.IsExcluded) continue;` before `if (win.IsDesktop)`. As a result, an excluded desktop window is skipped and `dEntry.SampleAlpha(nowMs)` (`0f`) is never read.
- **Fix**:
```diff
         float desktopAlpha = _config.DimDesktop ? (float)_config.DesktopOpacity : 0f;
         foreach (var win in _zOrderedWindows)
         {
-            if (win.IsExcluded)
-            {
-                continue;
-            }
-
             if (win.IsDesktop)
             {
                 if (_windows.TryGetValue(win.Handle, out var dEntry))
                 {
                     desktopAlpha = dEntry.SampleAlpha(nowMs);
                 }
                 continue;
             }
+
+            if (win.IsExcluded)
+            {
+                continue;
+            }
```

### [MAJOR] [OPEN] P1-02 Excluded background window punches bright cutout through unfocused foreground window
- **Status**: OPEN
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:188-195`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User has an excluded app (e.g., `vlc.exe`) open in the background behind an unfocused regular window (e.g., `notepad.exe`) while focused on a third window. `ComputeDrawRectsForMonitor` places all excluded windows at the very top of `layers` with `alpha = 0f` regardless of Z-order, so `OcclusionPartitioner` assigns `0f` to `vlc.exe`'s bounds first and leaves a bright `vlc.exe`-shaped cutout inside the dimmed `notepad.exe` window.
- **Why**: Hoisting every excluded window above step 2 (static zones) and step 3 (regular windows) ignores non-excluded windows that sit above the excluded window in `_zOrderedWindows`. Only the unoccluded portion of an excluded window (not covered by higher-Z non-excluded windows) should block static zones and lower windows.
- **Fix**:
```diff
-        // 1. Excluded windows sit at top with alpha = 0 so static zones never dim over excluded apps (e.g., video players)
+        // 1. Visible (unoccluded by higher-Z non-excluded windows) parts of excluded windows sit at top with alpha = 0
+        var occluders = new List<IntRect>();
         foreach (var win in _zOrderedWindows)
         {
-            if (win.IsExcluded && !win.IsDesktop)
+            if (win.IsDesktop)
             {
-                layers.Add((win.Bounds, 0f));
+                continue;
             }
+
+            if (!win.IsExcluded)
+            {
+                occluders.Add(win.Bounds);
+                continue;
+            }
+
+            foreach (var visibleBounds in SubtractOccluders(win.Bounds, occluders))
+            {
+                layers.Add((visibleBounds, 0f));
+            }
         }
```
*(With a private `SubtractOccluders(IntRect target, List<IntRect> occluders)` helper reusing `OcclusionPartitioner`'s rectangle subtraction.)*

### [MINOR] [OPEN] P1-03 Duplicate MonitorSnapshot.DeviceId crashes UpdateScene
- **Status**: OPEN
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:130`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: MEDIUM
- **Category**: robustness
- **Scenario**: `UpdateScene` receives a `monitors` list containing two `MonitorSnapshot` entries with the same `DeviceId` (e.g., empty string or duplicate adapter name during a display topology change). `monitors.ToDictionary(m => m.DeviceId, StringComparer.OrdinalIgnoreCase)` throws `ArgumentException`.
- **Why**: `Enumerable.ToDictionary` throws on duplicate keys instead of ignoring or keeping the first entry.
- **Fix**:
```diff
-        var monitorById = monitors.ToDictionary(m => m.DeviceId, StringComparer.OrdinalIgnoreCase);
+        var monitorById = new Dictionary<string, MonitorSnapshot>(monitors.Count, StringComparer.OrdinalIgnoreCase);
+        foreach (var m in monitors)
+        {
+            monitorById.TryAdd(m.DeviceId, m);
+        }
```
