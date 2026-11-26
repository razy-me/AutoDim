# Review Findings

## Review: WP01 — SDK Prerequisite & Solution Scaffolding
**Date**: 2026-11-26 | **Tier**: 1 (Diff/Feature)
**Files**: 10 | **Agents Used**: 0

| CRITICAL | MAJOR | MINOR | ENHANCEMENT |
|---|---|---|---|
| 0 | 0 | 0 | 0 |

Verified `AutoDim.slnx`, `AutoDim.Core.csproj`, `AutoDim.App.csproj` (WPF/WinForms + `Vortice.* 3.8.3` + `PerMonitorV2` `app.manifest`), `AutoDim.Core.Tests.csproj`, `AutoDimInfo.cs`, `App.xaml`/`App.xaml.cs`, `SmokeTests.cs`, and `.gitignore` — clean scaffolding with zero defects.

## Review: WP01 & WP02 — Scaffolding, Config & Atomic JSON Persistence
**Date**: 2026-11-26 | **Tier**: 1 (Diff/Feature)
**Files**: 16 | **Agents Used**: 0

| CRITICAL | MAJOR | MINOR | ENHANCEMENT |
|---|---|---|---|
| 0 | 0 | 2 | 0 |

### Findings

### [MINOR] [FIXED] R-01 `NormalizedRect.ClampFinite` can exceed `max` when `value` is non-finite
- **Status**: FIXED
- **Where**: `src/AutoDim.Core/Config/NormalizedRect.cs:20-21`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: `new NormalizedRect(0.95, 0.95, double.NaN, double.NaN).Normalized()` -> `ClampFinite(double.NaN, 0.1, 0.01, 0.05)` returns `0.1` instead of `0.05`, producing `Right = 1.05` and `Bottom = 1.05` outside `[0, 1]`.
- **Why**: `ClampFinite` returns `fallback` directly without clamping it to `[min, max]`, so when `1.0 - left < 0.1`, a non-finite `Width`/`Height` overflows the monitor bounds.
- **Fix**:
  ```diff
  -    internal static double ClampFinite(double value, double fallback, double min, double max) =>
  -        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
  +    internal static double ClampFinite(double value, double fallback, double min, double max) =>
  +        Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);
  ```

### [MINOR] [FIXED] R-02 `DimConfig` record equality breaks on `ExcludedProcesses` and `Zones`
- **Status**: FIXED
- **Where**: `src/AutoDim.Core/Config/DimConfig.cs:28-29`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: contract
- **Scenario**: `DimConfig.Default.Validated() == DimConfig.Default` or comparing two `JsonConfigStore.Load()` results on the same `config.json` returns `false`.
- **Why**: C# synthesized record equality compares `IReadOnlyList<T>` properties (`ExcludedProcesses`, `Zones`) by reference rather than sequence contents, so `Validated()` or deserialization breaks value equality and dirty-checking.
- **Fix**:
  ```diff
  -public sealed record DimConfig
  +public sealed record DimConfig : IEquatable<DimConfig>
   {
  +    public bool Equals(DimConfig? other) =>
  +        other is not null &&
  +        SchemaVersion == other.SchemaVersion &&
  +        InactiveWindowOpacity == other.InactiveWindowOpacity &&
  +        TaskbarOpacity == other.TaskbarOpacity &&
  +        ZoneOpacity == other.ZoneOpacity &&
  +        DesktopOpacity == other.DesktopOpacity &&
  +        HoverExitDelayMs == other.HoverExitDelayMs &&
  +        UnfocusDelayMs == other.UnfocusDelayMs &&
  +        FadeOutMs == other.FadeOutMs &&
  +        FadeInMs == other.FadeInMs &&
  +        OverlayColorRgb == other.OverlayColorRgb &&
  +        DimDesktop == other.DimDesktop &&
  +        DimTaskbar == other.DimTaskbar &&
  +        PauseOnFullscreen == other.PauseOnFullscreen &&
  +        ExcludeFromCapture == other.ExcludeFromCapture &&
  +        StartWithWindows == other.StartWithWindows &&
  +        ToggleHotkey == other.ToggleHotkey &&
  +        ExcludedProcesses.SequenceEqual(other.ExcludedProcesses, StringComparer.OrdinalIgnoreCase) &&
  +        Zones.SequenceEqual(other.Zones);
  +
  +    public override int GetHashCode() => HashCode.Combine(SchemaVersion, InactiveWindowOpacity, TaskbarOpacity, ToggleHotkey, ExcludedProcesses.Count, Zones.Count);
  ```

### Coverage / Not Reviewed
- Reviewed: `WP01` (10 files) and `WP02` (`src/AutoDim.Core/Config/*`, `tests/AutoDim.Core.Tests/ConfigTests.cs`).
- Excluded / Not reviewed (and why): `docs/*` markdown state files.

## Review: WP01–WP08 — Full Codebase Audit
**Date**: 2026-11-26 | **Tier**: 2 (Medium)
**Files**: 34 (`src/`, `tests/`) | **Agents Used**: 4 (3 fraction reviewers + 1 F3 verifier)

| CRITICAL | MAJOR | MINOR | ENHANCEMENT |
|---|---|---|---|
| 1 | 7 | 7 | 0 |

### Fix First (top items)
1. [CRITICAL] [FIXED] P2-01 Partial failure in `HandleDeviceLost` leaves disposed GPU/swapchain resources and crashes on next render -- `src/AutoDim.App/Overlay/OverlayCoordinator.cs:176-190`
2. [MAJOR] [FIXED] P1-01 Excluded desktop window still dims desktop at DesktopOpacity -- `src/AutoDim.Core/Engine/DimStateEngine.cs:211-223`
3. [MAJOR] [FIXED] P1-02 Excluded background window punches bright cutout through unfocused foreground window -- `src/AutoDim.Core/Engine/DimStateEngine.cs:188-195`
4. [MAJOR] [FIXED] P2-02 `RenderMonitorsById` does not re-render overlays after `HandleDeviceLost()` recovery -- `src/AutoDim.App/Overlay/OverlayCoordinator.cs:130-148`
5. [MAJOR] [FIXED] P2-03 `WindowTracker.OnWinEvent` triggers full scene refreshes for non-window child UI elements -- `src/AutoDim.App/Tracking/WindowTracker.cs:271-279`
6. [MAJOR] [FIXED] P3-01 `GlobalHotkeyHost.TryRegister` destroys the active hotkey before validating or registering the replacement -- `src/AutoDim.App/Shell/GlobalHotkeyHost.cs:34-46`
7. [MAJOR] [FIXED] P3-02 Closing `ZonePickerWindow` via Alt+F4 permanently disables the picker and leaves AutoDim paused -- `src/AutoDim.App/App.xaml.cs:82-117`
8. [MAJOR] [FIXED] P3-03 `SettingsWindow.OnResetDefaultsClick` does not reset Windows autostart or the registered global hotkey -- `src/AutoDim.App/UI/SettingsWindow.xaml.cs:178-182`

### Findings

### [CRITICAL] [FIXED] P2-01 Partial failure in `HandleDeviceLost` leaves disposed GPU/swapchain resources and crashes on next render
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Overlay/OverlayCoordinator.cs:176-190`, `src/AutoDim.App/Overlay/GpuOverlayDevice.cs:32-36,65-74`, `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs:101-107,249-258`
- **Source**: `r-review` (2026-11-26) | `lens: F1b`
- **Confidence**: HIGH
- **Category**: robustness
- **Scenario**: During a GPU driver reset (TDR) or display topology change, `RenderMonitorsById` detects device loss and calls `HandleDeviceLost()`. Either `_gpu.Recreate()` throws after `ReleaseResources()` already disposed `D2DContext`/`DxgiFactory`/`DCompDevice`, or `overlay.RecreateDeviceResources()` throws on one monitor after `ReleaseCompositionResources()` disposed `_swapChain`. `HandleDeviceLost()` swallows the exception, and the next render accesses disposed COM objects on the UI thread, crashing the app.
- **Why**: `HandleDeviceLost()` swallows exceptions without tracking a `_deviceLost` state, `ReleaseResources()` / `ReleaseCompositionResources()` leave disposed COM wrappers in fields without nulling them, and `_gpu.Recreate()` runs while overlays still hold resources bound to the old device.
- **Fix**:
  ```diff
  // OverlayCoordinator.cs: add `private bool _deviceLost;`, guard RequestSceneRefresh & RenderFrameOnly,
  // release all overlay resources before _gpu.Recreate(), then recreate overlays and clear _deviceLost = false only on full success.
  // DirectCompositionOverlayWindow.cs: add ReleaseDeviceResources() and null disposed COM fields in ReleaseCompositionResources().
  // GpuOverlayDevice.cs: null disposed COM fields in ReleaseResources().
  ```

### [MAJOR] [FIXED] P1-01 Excluded desktop window still dims desktop at DesktopOpacity
- **Status**: FIXED
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:211-223`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User adds `explorer.exe` to `DimConfig.ExcludedProcesses`. Desktop `WindowSnapshot` (`IsDesktop = true`) has `IsExcluded = true`, so `ComputeDrawRectsForMonitor` hits `if (win.IsExcluded) continue;` before `if (win.IsDesktop)` and leaves `desktopAlpha` at `_config.DesktopOpacity` (`0.85f`).
- **Why**: `if (win.IsExcluded) continue;` runs before `if (win.IsDesktop)`, skipping `dEntry.SampleAlpha(nowMs)` (`0f`).
- **Fix**:
  ```diff
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

### [MAJOR] [FIXED] P1-02 Excluded background window punches bright cutout through unfocused foreground window
- **Status**: FIXED
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:188-195`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: An excluded app (e.g., `vlc.exe`) sits in the background behind an unfocused regular window (`notepad.exe`) while a third window has focus. `ComputeDrawRectsForMonitor` hoists all excluded windows to the top of `layers` with `alpha = 0f` regardless of Z-order, punching a bright `vlc.exe`-shaped cutout through the dimmed `notepad.exe` window.
- **Why**: Hoisting every excluded window above static zones and regular windows ignores higher-Z non-excluded windows that occlude it in `_zOrderedWindows`.
- **Fix**:
  ```diff
  -        // 1. Excluded windows sit at top with alpha = 0 so static zones never dim over excluded apps (e.g., video players)
  +        // 1. Only unoccluded portions of excluded windows (not covered by higher-Z non-excluded windows) sit at top with alpha = 0
  +        var occluders = new List<IntRect>();
           foreach (var win in _zOrderedWindows)
           {
  -            if (win.IsExcluded && !win.IsDesktop)
  -            {
  -                layers.Add((win.Bounds, 0f));
  -            }
  +            if (win.IsDesktop) continue;
  +            if (!win.IsExcluded) { occluders.Add(win.Bounds); continue; }
  +            foreach (var visibleBounds in SubtractOccluders(win.Bounds, occluders))
  +                layers.Add((visibleBounds, 0f));
           }
  ```

### [MAJOR] [FIXED] P2-02 `RenderMonitorsById` does not re-render overlays after `HandleDeviceLost()` recovery
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Overlay/OverlayCoordinator.cs:130-148`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: GPU device-lost happens when no fade animation is running. `HandleDeviceLost()` recreates blank swap chains and resets `_hasRenderedOnce = false`, then `RenderMonitorsById` returns without re-rendering, leaving all monitors un-dimmed until the next window/cursor event.
- **Why**: `RenderMonitorsById` breaks on `deviceLost` and calls `HandleDeviceLost()` without scheduling `RequestSceneRefresh()`.
- **Fix**:
  ```diff
           if (deviceLost)
           {
               HandleDeviceLost();
  +            if (!_deviceLost)
  +            {
  +                RequestSceneRefresh();
  +            }
           }
  ```

### [MAJOR] [FIXED] P2-03 `WindowTracker.OnWinEvent` triggers full scene refreshes for non-window child UI elements
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Tracking/WindowTracker.cs:271-279`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: performance
- **Scenario**: Any running app creates/shows/hides a child UI element (tooltip, menu item, scrollbar, caret), firing `EVENT_OBJECT_DESTROY`/`SHOW`/`HIDE` with `idObject != 0 || idChild != 0` and triggering full `EnumWindows` + `DwmGetWindowAttribute` + `EnumDisplayMonitors` refreshes on the UI thread.
- **Why**: `OnWinEvent` only filters `idObject != 0 || idChild != 0` for `EVENT_OBJECT_LOCATIONCHANGE`, not for `DESTROY`/`SHOW`/`HIDE`.
- **Fix**:
  ```diff
  -        if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE && (idObject != 0 || idChild != 0))
  +        if (idObject != 0 || idChild != 0)
           {
               return;
           }
  ```

### [MAJOR] [FIXED] P3-01 `GlobalHotkeyHost.TryRegister` destroys the active hotkey before validating or registering the replacement
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Shell/GlobalHotkeyHost.cs:34-46`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User enters an invalid or already-occupied hotkey in Settings and clicks **Übernehmen**. `TryRegister` calls `Unregister()` before `TryParseHotkey`/`RegisterHotKey`, returning `false` while leaving the previous working hotkey unregistered for the rest of the session.
- **Why**: `Unregister()` runs unconditionally at the start of `TryRegister` without parsing first or rolling back to the previous hotkey on failure.
- **Fix**:
  ```diff
  -        Unregister();
           if (!TryParseHotkey(hotkeyText, out uint modifiers, out uint vk)) return false;
  +        var previous = _activeHotkey;
  +        Unregister();
  +        if (NativeMethods.RegisterHotKey(_messageWindow.Handle, HotkeyId, modifiers | MOD_NOREPEAT, vk))
  +        { _registered = true; _activeHotkey = (modifiers, vk); return true; }
  +        if (previous is { } prev && NativeMethods.RegisterHotKey(_messageWindow.Handle, HotkeyId, prev.Modifiers | MOD_NOREPEAT, prev.VirtualKey))
  +        { _registered = true; _activeHotkey = prev; }
  +        return false;
  ```

### [MAJOR] [FIXED] P3-02 Closing `ZonePickerWindow` via Alt+F4 permanently disables the picker and leaves AutoDim paused
- **Status**: FIXED
- **Where**: `src/AutoDim.App/App.xaml.cs:82-117`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User opens Zone Picker and presses `Alt+F4` instead of `ESC`/right-click. `ZonePickerWindow` closes without invoking `onCompleted`, leaving `_activePickers` non-empty (blocking future `LaunchZonePicker()` calls) and `_coordinator.IsPaused == true`.
- **Why**: `LaunchZonePicker` only cleans up `_activePickers` and restores `IsPaused` inside `onCompleted`, with no `Closed` handler or `scene.Monitors.Count == 0` guard.
- **Fix**:
  ```diff
  +        if (scene.Monitors.Count == 0) return;
  +        bool finalized = false;
  +        void FinishPicker(ZoneDefinition? zone) { if (finalized) return; finalized = true; ClosePickers(); ... _coordinator.IsPaused = wasPaused; }
           foreach (var mon in scene.Monitors)
           {
  -            var picker = new ZonePickerWindow(mon, defaultIdx, ...);
  +            var picker = new ZonePickerWindow(mon, defaultIdx, FinishPicker);
  +            picker.Closed += (_, _) => FinishPicker(null);
               _activePickers.Add(picker);
               picker.Show();
           }
  ```

### [MAJOR] [FIXED] P3-03 `SettingsWindow.OnResetDefaultsClick` does not reset Windows autostart or the registered global hotkey
- **Status**: FIXED
- **Where**: `src/AutoDim.App/UI/SettingsWindow.xaml.cs:178-182`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User enables autostart and sets a custom hotkey, then clicks **Standardwerte**. `config.json` and the UI revert, but `HKCU\...\Run\AutoDim` stays in the registry and `GlobalHotkeyHost` keeps the old hotkey active.
- **Why**: `OnResetDefaultsClick` bypasses `StartupManager.SetEnabled` and `_tryApplyHotkey`.
- **Fix**:
  ```diff
       private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
       {
  +        StartupManager.SetEnabled(DimConfig.Default.StartWithWindows);
  +        _tryApplyHotkey(DimConfig.Default.ToggleHotkey);
           _applyConfig(DimConfig.Default);
           LoadFromConfig(_getConfig());
       }
  ```

### [MINOR] [FIXED] P1-03 Duplicate `MonitorSnapshot.DeviceId` crashes `UpdateScene`
- **Status**: FIXED
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:130`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: MEDIUM
- **Category**: robustness
- **Scenario**: Two `MonitorSnapshot` entries share the same `DeviceId` during a display topology change -> `monitors.ToDictionary(...)` throws `ArgumentException`.
- **Why**: `Enumerable.ToDictionary` throws on duplicate keys instead of using `TryAdd`.
- **Fix**:
  ```diff
  -        var monitorById = monitors.ToDictionary(m => m.DeviceId, StringComparer.OrdinalIgnoreCase);
  +        var monitorById = new Dictionary<string, MonitorSnapshot>(monitors.Count, StringComparer.OrdinalIgnoreCase);
  +        foreach (var m in monitors) monitorById.TryAdd(m.DeviceId, m);
  ```

### [MINOR] [FIXED] P2-04 `_pidNameCache` in `WindowTracker` never invalidates exited PIDs
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Tracking/WindowTracker.cs:93-149,219-253`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: An excluded process exits and Windows reuses its PID before `_pidNameCache.Count > 256`; the new process inherits the stale cached exclusion name.
- **Why**: `_pidNameCache` is keyed only by `uint pid` and never pruned against active PIDs.
- **Fix**: Track `activePids` during `EnumWindows` and remove inactive keys from `_pidNameCache` after enumeration.

### [MINOR] [FIXED] P2-05 Unbounded `new string(info.szDevice)` on fixed 32-char buffer
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Tracking/WindowTracker.cs:166`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: MEDIUM
- **Category**: robustness
- **Scenario**: If `MONITORINFOEXW.szDevice` fills all 32 chars without null termination, `new string(info.szDevice)` reads past the stack buffer.
- **Why**: `new string(char*)` relies on a null terminator.
- **Fix**:
  ```diff
  -            string deviceId = new string(info.szDevice).TrimEnd('\0');
  +            var deviceSpan = new ReadOnlySpan<char>(info.szDevice, 32);
  +            int nullIdx = deviceSpan.IndexOf('\0');
  +            string deviceId = new string(nullIdx >= 0 ? deviceSpan[..nullIdx] : deviceSpan);
  ```

### [MINOR] [FIXED] P2-06 Non-atomic `_sceneUpdateQueued` flag in `OverlayCoordinator.RequestSceneRefresh()`
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Overlay/OverlayCoordinator.cs:19,73-81`
- **Source**: `r-review` (2026-11-26) | `lens: F1b`
- **Confidence**: MEDIUM
- **Category**: robustness
- **Scenario**: A non-UI caller observes `_sceneUpdateQueued == true` right before the UI callback clears it and captures a stale scene, dropping the refresh.
- **Why**: `_sceneUpdateQueued` is a plain `bool` instead of `Interlocked.Exchange(ref _sceneUpdateQueued, ...)`.
- **Fix**: Use `private int _sceneUpdateQueued;` with `Interlocked.Exchange(ref _sceneUpdateQueued, 1) != 0` and reset to `0` at start of callback.

### [MINOR] [FIXED] P3-04 `SettingsWindow.OnSettingChanged` writes Windows `Run` registry key on every slider drag tick
- **Status**: FIXED
- **Where**: `src/AutoDim.App/UI/SettingsWindow.xaml.cs:95-119`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: performance
- **Scenario**: Dragging any slider calls `StartupManager.SetEnabled(...)` on every `ValueChanged` tick even when `StartWithWindows` did not change.
- **Why**: `StartupManager.SetEnabled` is called unconditionally inside `OnSettingChanged`.
- **Fix**: Guard with `if (updated.StartWithWindows != current.StartWithWindows)`.

### [MINOR] [FIXED] P3-05 `OnZoneToggleChanged` triggers redundant saves during `ZonesList` container binding
- **Status**: FIXED
- **Where**: `src/AutoDim.App/UI/SettingsWindow.xaml.cs:41-81,161-167`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: performance
- **Scenario**: Loading Settings or modifying exclusions rebinds `ZonesList`; WPF container generation fires `Checked` after `_loading = false`, saving config for every enabled zone.
- **Why**: `OnZoneToggleChanged` does not check whether `existing.Enabled == item.Enabled`.
- **Fix**: `var existing = cfg.Zones.FirstOrDefault(z => z.Id == item.Id); if (existing is null || existing.Enabled == item.Enabled) return;`

### [MINOR] [FIXED] P3-06 `GlobalHotkeyHost.TryParseHotkey` accepts undefined numeric values and modifier flags
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Shell/GlobalHotkeyHost.cs:96-100`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: MEDIUM
- **Category**: robustness
- **Scenario**: Input `"Ctrl+123"` or `"Ctrl+Shift"` passes `Enum.TryParse<Keys>` and sends invalid/modifier key codes to `RegisterHotKey`.
- **Why**: `Enum.TryParse<Keys>` accepts arbitrary integers and WinForms modifier masks.
- **Fix**: Reject numeric strings (`!int.TryParse(keyPart, out _)`) and check `Enum.IsDefined(parsedKey) && parsedKey is > Keys.None and < Keys.Modifiers`.

### Coverage / Not Reviewed
- Reviewed: `src/AutoDim.Core/**`, `src/AutoDim.App/**`, `tests/AutoDim.Core.Tests/**` (34 source/test files).
- Excluded / Not reviewed (and why): `docs/**` markdown files, `publish/**` build outputs.

## Review: WP01–WP08 — Fresh Cross-Cutting Pass (DPI, Z-Order, Lifecycle, Contracts)
**Date**: 2026-11-26 | **Tier**: 2 (Cross-Cutting `F2`)
**Files**: 34 | **Agents Used**: 2 (`F2` DPI/Lifecycle + `F2` State/Contracts)

| CRITICAL | MAJOR | MINOR | ENHANCEMENT |
|---|---|---|---|
| 0 | 3 | 1 | 0 |

### Fix First (top items)
1. [MAJOR] [FIXED] X-DPI-01 `WindowTracker.EnumerateWindows` skips all `_selfPid` windows, causing AutoDim overlays to dim over `SettingsWindow` -- `src/AutoDim.App/Tracking/WindowTracker.cs:104-114`
2. [MAJOR] [FIXED] X-DPI-02 `DirectCompositionOverlayWindow` never re-asserts `HWND_TOPMOST` after creation, allowing taskbar or topmost windows to permanently cover the overlay -- `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs:72-99`
3. [MAJOR] [FIXED] X-DPI-03 `ZonePickerWindow.OnSourceInitialized` single-call `SetWindowPos` across mixed-DPI monitors triggers `WM_DPICHANGED` scaling distortion -- `src/AutoDim.App/UI/ZonePickerWindow.xaml.cs:33-44`

### Findings

### [MAJOR] [FIXED] X-DPI-01 `WindowTracker.EnumerateWindows` skips all `_selfPid` windows, causing AutoDim overlays to dim over `SettingsWindow`
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Tracking/WindowTracker.cs:104-114`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User opens `SettingsWindow` (`AutoDim — Einstellungen`) over desktop or an unfocused window. Because `SettingsWindow` belongs to `_selfPid`, `EnumerateWindows` skips it (`if (pid == _selfPid) return true;`), so `DimStateEngine` never sees it and `DirectCompositionOverlayWindow` dims right on top of `SettingsWindow`.
- **Why**: `EnumerateWindows` filters the entire `_selfPid` even though overlay windows are already filtered by `WS_EX_TRANSPARENT` and `GlobalHotkeyHost` is hidden (`IsWindowVisible == false`).
- **Fix**:
  ```diff
               NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
  -            if (pid == _selfPid)
  -            {
  -                return true;
  -            }
  -
               int exStyle = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);
  ```

### [MAJOR] [FIXED] X-DPI-02 `DirectCompositionOverlayWindow` never re-asserts `HWND_TOPMOST` after creation, allowing taskbar or topmost windows to permanently cover the overlay
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs:72-99`, `src/AutoDim.App/Overlay/OverlayCoordinator.cs:111-121`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User clicks the Windows Taskbar (`Shell_TrayWnd`, `WS_EX_TOPMOST`) and then focuses a normal window. Windows promotes `Shell_TrayWnd` above `DirectCompositionOverlayWindow` in the topmost band, so the taskbar stops dimming for the rest of the session.
- **Why**: `SetWindowPos(_hwnd, HWND_TOPMOST, ...)` is only called in the constructor and `Resize()`, never during `SyncOverlayWindows`.
- **Fix**:
  ```diff
  // DirectCompositionOverlayWindow.cs: add EnsureTopmost() calling SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)
  // OverlayCoordinator.cs (SyncOverlayWindows): call existing.EnsureTopmost();
  ```

### [MAJOR] [FIXED] X-DPI-03 `ZonePickerWindow.OnSourceInitialized` single-call `SetWindowPos` across mixed-DPI monitors triggers `WM_DPICHANGED` scaling distortion
- **Status**: FIXED
- **Where**: `src/AutoDim.App/UI/ZonePickerWindow.xaml.cs:33-44`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: MEDIUM
- **Category**: bug
- **Scenario**: On a mixed-DPI setup (e.g., 100% primary, 150% secondary), `ZonePickerWindow` for the secondary monitor is moved & sized in one `SetWindowPos` call, triggering `WM_DPICHANGED` with a rescaled suggested rect that overwrites the requested physical dimensions and distorts saved `ZoneDefinition.Bounds`.
- **Why**: Moving and sizing a `PerMonitorV2` WPF `Window` across DPI boundaries in one `SetWindowPos` call triggers `WM_DPICHANGED` rescaling.
- **Fix**:
  ```diff
       private void OnSourceInitialized(object? sender, EventArgs e)
       {
           nint hwnd = new WindowInteropHelper(this).Handle;
  +        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _monitor.Bounds.Left, _monitor.Bounds.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
           NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _monitor.Bounds.Left, _monitor.Bounds.Top, Math.Max(1, _monitor.Bounds.Width), Math.Max(1, _monitor.Bounds.Height), NativeMethods.SWP_SHOWWINDOW);
       }
  ```

### [MINOR] [FIXED] X-STATE-01 `HasActiveAnimations` and `NextDeadlineMs` ignore `PauseOnFullscreen`, spinning the 16 ms animation timer while fullscreen-paused
- **Status**: FIXED
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:179-184,234-255`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: performance
- **Scenario**: On a fullscreen-paused monitor setup (`PauseOnFullscreen == true`), when a background window's timer expires, `HasActiveAnimations` evaluates deadlines and runs the 16 ms `_animTimer` for `FadeOutMs` even though every `ComputeDrawRectsForMonitor` returns `[]`.
- **Why**: `HasActiveAnimations` and `NextDeadlineMs` only check `IsPaused`, not `AreAllMonitorsPausedByFullscreen()`.
- **Fix**:
  ```diff
  +    private bool AreAllMonitorsPausedByFullscreen() => _config.PauseOnFullscreen && _monitors.Count > 0 && _monitors.All(m => m.HasFullscreenApp);
  -        if (IsPaused) return false; // and in NextDeadlineMs: if (IsPaused) return null;
  +        if (IsPaused || AreAllMonitorsPausedByFullscreen()) return false; // and return null;
  ```

## Review: Latest Changes & Fixes (Click-Through, QUNS_BUSY, FrameInsetPx, Rounded Corners, Fast Drag)
**Date**: 2026-11-26 | **Tier**: 1 (Diff/Feature)
**Files**: 10 | **Agents Used**: 0

| CRITICAL | MAJOR | MINOR | ENHANCEMENT |
|---|---|---|---|
| 0 | 0 | 2 | 0 |

### Findings

### [MINOR] [FIXED] L-01 Rounded corner spandrels pop from dark to bright when a window begins fading out
- **Status**: FIXED
- **Where**: `src/AutoDim.Core/Engine/DimStateEngine.cs:260-318`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: An unfocused window with `DesktopOpacity = 0.85` reaches its timer deadline and begins fading (`alpha` moves from `0f` to `0.05f`). Because `if (!win.IsTaskbar && alpha <= 0.001f)` becomes `false`, all 4 corner spandrels are omitted on the first fade frame, causing the corners to jump from `0.85` (dark) to `0.05` (bright) and fade down again.
- **Why**: Corner spandrels are gated on `alpha <= 0.001f` instead of blending the remaining `(bgAlpha - alpha) / (1f - alpha)` alpha while `bgAlpha > alpha + 0.001f`.
- **Fix**:
  ```diff
  -                    if (!win.IsTaskbar && alpha <= 0.001f)
  +                    if (!win.IsTaskbar && alpha < 0.98f)
                       {
  -                        roundedCandidates.Add((effectiveBounds, idx));
  +                        roundedCandidates.Add((effectiveBounds, idx, alpha));
                       }
  ```
  *(And in `ComputeRoundedCorners`, when `bgAlpha > winAlpha + 0.001f`, emit `Alpha = (bgAlpha - winAlpha) / (1f - winAlpha)`.)*

### [MINOR] [FIXED] L-02 Native overlay `_hwnd` leaks if `CreateCompositionResources()` throws in constructor
- **Status**: FIXED
- **Where**: `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs:52-84`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: robustness
- **Scenario**: During a transient GPU device reset when a new monitor overlay is constructed (`SyncOverlayWindows`), `CreateWindowExW` creates `_hwnd` and `CreateCompositionResources()` throws, leaving an orphaned `AutoDimOverlay_*` topmost window until process exit.
- **Why**: `DirectCompositionOverlayWindow` constructor lacks a `try/catch` cleanup around post-`CreateWindowExW` initialization.
- **Fix**:
  ```diff
  +        try
  +        {
               NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LWA_ALPHA);
               NativeMethods.SetWindowPos(...);
               ApplyCaptureExclusion(excludeFromCapture);
               CreateCompositionResources();
  +        }
  +        catch
  +        {
  +            ReleaseCompositionResources();
  +            NativeMethods.DestroyWindow(_hwnd);
  +            throw;
  +        }
  ```

### Coverage / Not Reviewed
- Reviewed: `NativeMethods.cs`, `DirectCompositionOverlayWindow.cs`, `OverlayCoordinator.cs`, `WindowTracker.cs`, `DimConfig.cs`, `SceneModels.cs`, `DimStateEngine.cs`, `SettingsWindow.xaml*`, `EngineTests.cs`.
