# Plan: AutoDim - Win32 AMOLED Dimming Overlay
**Date**: 2026-11-26 | **Mode**: `i-plan` (Interactive Co-Design) | **Status**: APPROVED 2026-11-26

## 1. Goal & Requirements
- **Outcome**: A lightweight, GPU-accelerated Win32/WPF utility that dims inactive windows, the taskbar, and custom static UI zones with pure black `#000000` overlays after presence/focus grace periods to save AMOLED battery, reduce burn-in, and focus attention on the active window.
- **Requirements**:
  - `R1` (**Focused Window Always Bright**): The active foreground window remains un-dimmed (`alpha = 0`), except inside explicitly overlapping, enabled custom static zones.
  - `R2` (**Unfocus Grace Timer**): When a window loses focus while the cursor is not inside it, a configurable unfocus timer (default `20s`) starts before smoothly fading to `InactiveWindowOpacity` (default `0.80`). Regaining focus or hovering cancels the timer and brightens it immediately.
  - `R3` (**Presence-Based Hover & 10s Exit Timer**): While the cursor hovers over an inactive window, taskbar (`Shell_TrayWnd` / `Shell_SecondaryTrayWnd`), or static UI zone, that target stays un-dimmed (`alpha = 0`). When the cursor exits the target, a configurable hover-exit timer (default `10s`) starts before smoothly fading to its dim opacity.
  - `R4` (**GPU Zero-Overdraw Click-Through Overlay**): Renders pure `#000000` (configurable tint) per monitor via `DirectComposition` + `Direct2D1` (`WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`), partitioning Z-order into disjoint rectangles so overlapping windows never double-darken, and stopping frame ticks (`0%` render CPU/GPU) once fades settle.
  - `R5` (**Taskbar, Desktop & Static UI Zones**): Independently configurable dimming for primary/secondary taskbars, desktop background, and user-defined static zones (monitor-relative bounds + optional process filter).
  - `R6` (**Fullscreen Auto-Pause, Exclusions & Capture Protection**): Automatically pauses dimming when fullscreen apps/games/video (`SHQueryUserNotificationState` or full-monitor foreground `RECT`) are active; supports process/window exclusions and optional `WDA_EXCLUDEFROMCAPTURE` so screenshots/screen-shares stay bright.
  - `R7` (**Multi-Monitor, DPIv2 & Power/Display Resilience**): Per-Monitor DPIv2 aware; cleanly recreates/resizes overlay surfaces on `WM_DISPLAYCHANGE` / `WM_DPICHANGED`, and resets/pauses across sleep/resume (`WM_POWERBROADCAST`) and session lock/unlock (`WM_WTSSESSION_CHANGE`).
  - `R8` (**System Tray, Global Hotkey & Autostart**): Single-instance process with Tray icon (Enable/Pause toggle, Settings, Visual Zone Picker, Exit), configurable `RegisterHotKey` pause toggle (default `Ctrl+Alt+D`), and optional `HKCU\...\Run` autostart toggle.
  - `R9` (**WPF Dark-Mode Settings & Config Persistence**): Clean dark-mode WPF settings UI for timers, opacities, fade speeds, exclusions, zones, and hotkey, persisted atomically as JSON in `%AppData%\AutoDim\config.json`.
  - `R10` (**Visual Interactive Zone Picker**): Translucent cross-monitor drag-selection overlay with live dimension readout and edge/window snapping to create or edit static zones visually.
- **Non-Goals**:
  - Hardware monitor backlight / DDC/CI brightness control.
  - Injecting DLLs into third-party processes or hooking into exclusive DirectX/Vulkan game render pipelines.
  - Modifying internal dark/light themes of third-party applications.
- **Assumptions**:
  - `A1`: Target OS is Windows 10 (2004+) / Windows 11 x64 (`WDA_EXCLUDEFROMCAPTURE` and `DirectComposition` supported).
  - `A2`: Desktop runtimes (`.NET 6/8/10`) are already installed on the host, while the `.NET 10 SDK` (`Microsoft.DotNet.SDK.10`) must be installed once during `WP-01` before `dotnet build` runs.
  - `A3`: Empty desktop wallpaper (`Progman` / `WorkerW`) dims at `DesktopOpacity` unless the desktop itself is clicked/focused, preventing wallpaper flicker when moving the mouse quickly across gaps between windows.

## 2. Technical Design & Contracts
- **Chosen Approach**: Build a C# / `.NET 10` (`net10.0-windows`) solution split into a pure platform-agnostic core (`AutoDim.Core`, fully covered by headless `xUnit` tests) and a Windows desktop host (`AutoDim.App`) combining WPF (Tray/Settings/Zone Picker) with native per-monitor `DirectComposition` + `Direct2D1` click-through overlays (`Vortice.Windows`). Z-order occlusion is resolved into disjoint `(Rect, Alpha)` draw commands so overlapping translucent windows never double-darken, and animations run only during active transitions or pending deadlines. See [DECISIONS.md](../agents/DECISIONS.md) (`D-001`–`D-003`).
- **Interfaces / Types / Signatures**:
  - `AutoDim.Core.Config`:
    - `record DimConfig(double InactiveWindowOpacity = 0.80, double TaskbarOpacity = 0.85, double ZoneOpacity = 0.85, double DesktopOpacity = 0.85, int HoverExitDelayMs = 10_000, int UnfocusDelayMs = 20_000, int FadeOutMs = 450, int FadeInMs = 140, uint OverlayColorRgb = 0x000000, bool DimDesktop = true, bool DimTaskbar = true, bool PauseOnFullscreen = true, bool ExcludeFromCapture = true, bool StartWithWindows = false, string ToggleHotkey = "Ctrl+Alt+D", List<string> ExcludedProcesses = ..., List<ZoneDefinition> Zones = ...)`
    - `record ZoneDefinition(Guid Id, string Name, string MonitorDeviceId, NormalizedRect Bounds, string? ProcessNameFilter, double? OpacityOverride, bool Enabled)`
    - `interface IConfigStore { DimConfig Load(); void Save(DimConfig config); }`
  - `AutoDim.Core.Engine`:
    - `record WindowSnapshot(nint Handle, string ProcessName, string ClassName, IntRect Bounds, bool IsFocused, bool IsTaskbar, bool IsDesktop, bool IsExcluded)`
    - `record MonitorSnapshot(string DeviceId, IntRect Bounds, double DpiScale, bool HasFullscreenApp)`
    - `record DimDrawRect(IntRect Bounds, float Alpha)`
    - `interface IDimStateEngine { void UpdateScene(IReadOnlyList<MonitorSnapshot> monitors, IReadOnlyList<WindowSnapshot> zOrderedWindows, IntPoint cursorPos, long nowMs, DimConfig config); IReadOnlyList<DimDrawRect> ComputeDrawRectsForMonitor(string monitorDeviceId, long nowMs); bool HasActiveAnimations { get; } long? NextDeadlineMs { get; } }`
    - `static class OcclusionPartitioner { static IReadOnlyList<DimDrawRect> Partition(IntRect monitorBounds, IReadOnlyList<(IntRect Bounds, float Alpha)> topToBottomLayers, float baseDesktopAlpha); }`
  - `AutoDim.App.Interop & Overlay`:
    - `interface IWindowTracker : IDisposable { event Action SceneInvalidated; SceneSnapshot CaptureScene(); }`
    - `interface IOverlaySurface : IDisposable { void Resize(IntRect monitorBounds); void Render(IReadOnlyList<DimDrawRect> rects, uint rgbColor, bool excludeFromCapture); }`
- **Data / State Flow**:
  1. `SetWinEventHook` + lightweight cursor/idle monitor detects focus, Z-order, window move/resize, display changes, or cursor entry/exit -> fires `SceneInvalidated` (coalesced per frame/tick).
  2. `WindowTracker.CaptureScene()` enumerates monitors and top-to-bottom visible non-cloaked `HWND`s (`DWMWA_EXTENDED_FRAME_BOUNDS`), detects fullscreen/exclusions, and reads `GetCursorPos()`.
  3. `DimStateEngine.UpdateScene(...)` updates each window/taskbar/zone state machine (`Focused` / `Hovered` -> `HoverExitGrace(10s)` or `UnfocusGrace(20s)` -> `FadingToDim` -> `Dimmed` -> `FadingToBright`).
  4. `OcclusionPartitioner.Partition(...)` subtracts top-to-bottom visible bounds per monitor into disjoint `(IntRect, Alpha)` rectangles.
  5. `MonitorOverlayHost` pushes rects to `Direct2D1` + `DirectComposition` SwapChain (`Commit()`), scheduling the next timer either at 60Hz (while `HasActiveAnimations`) or sleeping until `NextDeadlineMs` (`0%` CPU/GPU steady state).

## 3. File Touchpoints & Work Breakdown
| ID | Tag | Action | File / Module | Change Summary | Depends On | Verify / Test Criterion |
|---|---|---|---|---|---|---|
| WP-01 | `[i]` | ADD | `AutoDim.sln`, `src/AutoDim.Core/`, `src/AutoDim.App/`, `tests/AutoDim.Core.Tests/`, `docs/agents/CONTEXT.md` | Check/install `.NET 10 SDK` (`Microsoft.DotNet.SDK.10`), scaffold solution, `net10.0-windows` WPF + Win32 manifest (`PerMonitorV2`), Vortice packages, and xUnit test project. | - | `dotnet build` and `dotnet test` succeed cleanly with zero warnings/errors. |
| WP-02 | `[r]` | ADD | `src/AutoDim.Core/Config/*`, `tests/AutoDim.Core.Tests/ConfigTests.cs` | Define `DimConfig`, `ZoneDefinition`, validation/clamping, atomic JSON `JsonConfigStore` (`%AppData%\AutoDim\config.json`), and defaults (`10s` hover exit, `20s` unfocus, `0.80`/`0.85` opacity). | WP-01 | `dotnet test --filter ConfigTests` passes (round-trip, corrupt-file fallback, clamping). |
| WP-03 | `[r]` | ADD | `src/AutoDim.Core/Engine/*`, `tests/AutoDim.Core.Tests/EngineTests.cs`, `OcclusionTests.cs` | Implement `DimStateEngine` (20s unfocus, presence-based hover + 10s exit grace, taskbar/zone independent timers, fade easing, `NextDeadlineMs`), and `OcclusionPartitioner` disjoint rect subtraction. | WP-02 | `dotnet test` passes 100% of state transition, dual-timer overlap, zero-overdraw, and deadline tests. |
| WP-04 | `[i]` | ADD | `src/AutoDim.App/Interop/*`, `src/AutoDim.App/Tracking/*` | Implement Win32 P/Invoke (`SetWinEventHook`, `EnumWindows`, `DwmGetWindowAttribute`, `GetCursorPos`, `GetLastInputInfo`, `SHQueryUserNotificationState`), `WindowTracker`, fullscreen detector, and display/power change listener. | WP-01, WP-03 | Live console/diagnostic verification logs accurate Z-order, `DWMWA_EXTENDED_FRAME_BOUNDS`, hover detection, and fullscreen pause. |
| WP-05 | `[i]` | ADD | `src/AutoDim.App/Overlay/*` | Implement `Direct3D11` / `DXGI` / `DirectComposition` / `Direct2D1` per-monitor click-through overlay (`WS_EX_NOREDIRECTIONBITMAP \| WS_EX_TRANSPARENT \| WS_EX_TOPMOST \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`, `WDA_EXCLUDEFROMCAPTURE`), coordinated by `OverlayCoordinator`. | WP-03, WP-04 | Visual & automated smoke check: overlays dim inactive windows/taskbar smoothly, clicks pass through, zero double-darkening, `0%` CPU when settled. |
| WP-06 | `[r]` | ADD | `src/AutoDim.App/Shell/*` | Implement Single-Instance Mutex, System Tray icon + menu (Active/Paused, Settings, Add Zone, Exit), configurable `RegisterHotKey` pause toggle, and `HKCU\...\Run` autostart helper. | WP-02, WP-05 | App launches to tray, second launch exits cleanly, hotkey toggles pause state immediately, autostart key toggles cleanly. |
| WP-07 | `[i]` | ADD | `src/AutoDim.App/UI/*` | Build WPF dark-mode Settings window (sliders for timers/opacities/fades, process exclusion picker, zone manager, hotkey box) and cross-monitor translucent Visual Zone Picker with mouse drag + edge/window snapping. | WP-02, WP-05, WP-06 | User can adjust sliders with live overlay preview, exclude an app, drag-create a static zone on screen, and persist across restart. |
| WP-08 | `[r]` | ADD | `tests/`, `docs/agents/CONTEXT.md` | Full test run, multi-monitor/DPI/display-change/sleep-resume checklist verification, idle CPU/RAM measurement, and self-contained/framework-dependent release publish check (`dotnet publish`). | WP-04..WP-07 | `dotnet test` green, `dotnet publish -c Release` succeeds, idle CPU ~0% confirmed. |

## 4. Premortem (Edge Cases, Security, Performance)
- **Callers / Breaking Impact**: Greenfield project (`c:\Tools\AutoDim`); no existing callers. Keep `AutoDim.Core` free of Win32/WPF dependencies so unit tests execute fast in CI/headless shells.
- **Edge Cases & Error Handling**:
  - *Double-darkening on overlapping windows*: Mitigated by `OcclusionPartitioner` subtracting higher-Z `RECT`s from lower-Z `RECT`s before drawing on a cleared `RGBA(0,0,0,0)` swapchain.
  - *Invisible DWM drop-shadow borders*: Use `DWMWA_EXTENDED_FRAME_BOUNDS` (falling back to `GetWindowRect`) so overlays match visible window edges instead of extending 7px past borders.
  - *Hidden UWP / other-virtual-desktop windows*: Check `DWMWA_CLOAKED` (`!= 0` -> skip) and `IsWindowVisible` / `IsIconic` so invisible background frames don't block lower windows.
  - *Elevated / UAC / Start Menu / AutoDim own UI*: Never fight `CoreWindow`/`SearchHost` Z-order; exclude AutoDim's own PID from dimming and handle `DXGI_ERROR_DEVICE_REMOVED` / `DXGI_ERROR_DEVICE_RESET` by recreating D3D/DComp device resources cleanly.
  - *Focus loss while still hovering*: If a window loses focus while the cursor stays inside it, presence-based hover keeps it un-dimmed until the cursor exits (`10s` grace starts on exit).
  - *Display hotplug / Sleep / Lock*: Suspend timers on `WTS_SESSION_LOCK` / `PBT_APMSUSPEND`, rebuild per-monitor overlays on `WM_DISPLAYCHANGE` / `WM_DPICHANGED`, and gracefully clear overlays on exit/crash.
- **Security & Performance**:
  - *No global mouse hook (`WH_MOUSE_LL`)*: Avoids input lag and Windows hook timeout drops; uses `SetWinEventHook` + lightweight `GetLastInputInfo`/`GetCursorPos` polling (`~50–100ms`, skipped when cursor hasn't moved and scene hasn't changed).
  - *Zero-idle power draw*: Animation frame timer (`CompositionTarget` / high-res timer) stops completely once `HasActiveAnimations == false`, waking only on WinEvents, cursor movement across window/zone boundaries, or `NextDeadlineMs`.
  - *Config safety*: Atomic write (`config.json.tmp` -> `File.Move(..., overwrite: true)`) + fallback to defaults with backup if `config.json` is malformed.

## 5. Verification Plan
- **Verification Commands**:
  - `dotnet build AutoDim.sln -c Debug`
  - `dotnet test tests/AutoDim.Core.Tests/AutoDim.Core.Tests.csproj`
  - `dotnet publish src/AutoDim.App/AutoDim.App.csproj -c Release -r win-x64 --no-self-contained`
- **Key Scenarios**:
  1. *Unfocus 20s timer*: Focus switches from Window A to Window B (cursor in B) -> B stays `alpha=0`; A stays `alpha=0` for `20s`, then fades smoothly to `0.80`.
  2. *Presence hover + 10s exit*: Cursor moves over dimmed Window A or Taskbar -> immediately fades to `0` and stays `0` while cursor remains inside; cursor exits -> stays `0` for `10s`, then fades back to dim.
  3. *Custom static zone inside active window*: Zone defined over sidebar inside focused IDE -> dims after `10s` of cursor absence even while typing in the editor; brightens immediately on hover.
  4. *Fullscreen pause*: Launch fullscreen video/game -> all dim overlays immediately clear (`alpha=0`) until exiting fullscreen.
  5. *Multi-monitor & DPI change*: Move windows across monitors with different DPI scales and unplug/replug display -> overlays match exact extended bounds with no orphaned black rectangles.
