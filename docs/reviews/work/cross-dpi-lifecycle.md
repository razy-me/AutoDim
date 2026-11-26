### [MAJOR] [OPEN] X-DPI-01 `WindowTracker.EnumerateWindows` skips all `_selfPid` windows, causing AutoDim overlays to dim over `SettingsWindow`
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Tracking/WindowTracker.cs:104-114`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User opens `SettingsWindow` (`AutoDim — Einstellungen`) from the tray icon while `DimDesktop = true` or over an unfocused background window. Because `SettingsWindow` is not `Topmost` and belongs to `_selfPid`, `EnumerateWindows` skips it at `if (pid == _selfPid) return true;`. `DimStateEngine` never sees `SettingsWindow` as focused or present, so the `HWND_TOPMOST` `DirectCompositionOverlayWindow` renders a dim rectangle (up to `DesktopOpacity` = `0.85` or `InactiveWindowOpacity` = `0.65`) directly on top of `SettingsWindow` while the user is interacting with it.
- **Why**: `EnumerateWindows` filters out the entire process (`pid == _selfPid`) to avoid tracking AutoDim's own overlay windows, even though overlay windows are already excluded by the `WS_EX_TRANSPARENT` check immediately below (line 111) and `GlobalHotkeyHost`'s message window is hidden (`IsWindowVisible == false`).
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

### [MAJOR] [OPEN] X-DPI-02 `DirectCompositionOverlayWindow` never re-asserts `HWND_TOPMOST` after creation, allowing taskbar or topmost windows to permanently cover the overlay
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs:72-99`, `src/AutoDim.App/Overlay/OverlayCoordinator.cs:111-121`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User has `DimTaskbar = true` (default). On startup, `DirectCompositionOverlayWindow` sits at the top of the `HWND_TOPMOST` band. When the user clicks the Windows Taskbar (`Shell_TrayWnd`, also `WS_EX_TOPMOST`), opens the Start Menu, or activates another topmost window, Windows raises that window above `DirectCompositionOverlayWindow` in the topmost band. When the user returns focus to a normal window, `Shell_TrayWnd` stays above `DirectCompositionOverlayWindow`, so the taskbar is no longer dimmed for the rest of the session until a display change or resize occurs.
- **Why**: `SetWindowPos(_hwnd, HWND_TOPMOST, ...)` is only called in the constructor and `Resize()`, never during scene refreshes (`SyncOverlayWindows`), so any Z-order promotion of another topmost window permanently places it above the dim overlay.
- **Fix**:
  ```diff
  // DirectCompositionOverlayWindow.cs:
  +    public void EnsureTopmost()
  +    {
  +        if (_disposed) return;
  +        NativeMethods.SetWindowPos(
  +            _hwnd,
  +            NativeMethods.HWND_TOPMOST,
  +            0, 0, 0, 0,
  +            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
  +    }

  // OverlayCoordinator.cs (inside SyncOverlayWindows):
               if (_overlays.TryGetValue(mon.DeviceId, out var existing))
               {
                   existing.Resize(mon.Bounds);
  +                existing.EnsureTopmost();
               }
  ```

### [MAJOR] [OPEN] X-DPI-03 `ZonePickerWindow.OnSourceInitialized` single-call `SetWindowPos` across mixed-DPI monitors triggers `WM_DPICHANGED` scaling distortion
- **Status**: OPEN
- **Where**: `src/AutoDim.App/UI/ZonePickerWindow.xaml.cs:33-44`
- **Source**: `r-review` (2026-11-26)
- **Confidence**: MEDIUM
- **Category**: bug
- **Scenario**: On a mixed-DPI multi-monitor setup (e.g., primary monitor at 100% DPI, secondary monitor at 150% DPI), the user opens the Zone Picker. `Window.Show()` creates the HWND on the primary monitor (100% DPI), and `OnSourceInitialized` calls `SetWindowPos` once with `_monitor.Bounds` (physical pixels for the 150% monitor). Crossing the DPI boundary causes Windows (`PerMonitorV2`) to synchronously send `WM_DPICHANGED` with a suggested `RECT` scaled by `150% / 100% = 1.5x`, which WPF's `WM_DPICHANGED` handler applies—resizing `ZonePickerWindow` to `1.5x` the monitor size (or `0.67x` when going 150% to 100%), misaligning the picker and distorting the normalized `ZoneDefinition.Bounds` saved on selection.
- **Why**: Moving and sizing a `PerMonitorV2` WPF `Window` across DPI boundaries in a single `SetWindowPos` call triggers `WM_DPICHANGED` with a DPI-rescaled suggested rect that overwrites the requested physical pixel dimensions.
- **Fix**:
  ```diff
       private void OnSourceInitialized(object? sender, EventArgs e)
       {
           nint hwnd = new WindowInteropHelper(this).Handle;
  +        // Move first so WM_DPICHANGED settles on the target monitor's DPI before setting exact pixel bounds.
  +        NativeMethods.SetWindowPos(
  +            hwnd,
  +            NativeMethods.HWND_TOPMOST,
  +            _monitor.Bounds.Left,
  +            _monitor.Bounds.Top,
  +            0,
  +            0,
  +            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
           NativeMethods.SetWindowPos(
               hwnd,
               NativeMethods.HWND_TOPMOST,
               _monitor.Bounds.Left,
               _monitor.Bounds.Top,
               Math.Max(1, _monitor.Bounds.Width),
               Math.Max(1, _monitor.Bounds.Height),
               NativeMethods.SWP_SHOWWINDOW);
       }
  ```
