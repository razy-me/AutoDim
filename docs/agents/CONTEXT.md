# CONTEXT
Last Indexed: uninitialized (greenfield)

## Stack & Versions
- **Runtime / SDK**: `.NET 10.0` (`SDK 10.0.404` at `%USERPROFILE%\.dotnet\dotnet.exe`; `net10.0` for Core/Tests, `net10.0-windows` for App)
- **UI**: WPF + WinForms (`NotifyIcon` / `Screen`) with `PerMonitorV2` DPI manifest
- **GPU Overlay**: `Vortice.Direct2D1`, `Vortice.Direct3D11`, `Vortice.DXGI`, `Vortice.DirectComposition` (`3.8.3`)
- **Tests**: `xUnit 2.9.3`

## Commands
build: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build AutoDim.slnx`
test: `& "$env:USERPROFILE\.dotnet\dotnet.exe" test AutoDim.slnx`
typecheck: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build AutoDim.slnx --no-restore`
publish: `& "$env:USERPROFILE\.dotnet\dotnet.exe" publish src/AutoDim.App/AutoDim.App.csproj -c Release -r win-x64 --no-self-contained`

## Folder Map
- `AutoDim.slnx`: Solution file
- `src/AutoDim.Core/`: Pure domain logic (config, state engine, occlusion partitioning)
- `src/AutoDim.App/`: Windows host (Win32 P/Invoke, window tracker, DirectComposition overlay, tray, WPF settings & zone picker)
- `tests/AutoDim.Core.Tests/`: xUnit unit tests for `AutoDim.Core`

## Conventions
- Keep `AutoDim.Core` free of Win32/WPF/Vortice dependencies for fast headless testing.
- Nullable reference types enabled (`<Nullable>enable</Nullable>`), zero compiler warnings.
- Config path: `%AppData%\AutoDim\config.json` (atomic temp-file + move writes).

## Module: Config (`src/AutoDim.Core/Config/`)
- `NormalizedRect(Left, Top, Width, Height)` — monitor-relative `[0..1]` bounds with `Normalized()`.
- `ZoneDefinition(Id, Name, MonitorDeviceId, Bounds, ProcessNameFilter?, OpacityOverride?, Enabled)` — static UI zone definition.
- `DimConfig` — immutable config record (`Default`, `Validated()`), clamping opacities `[0..0.98]`, delays `[0..3_600_000]`, fades `[0..10_000]`, `FrameInsetPx` `[0..20]`.
- `IConfigStore` / `JsonConfigStore` — atomic `%AppData%\AutoDim\config.json` persistence with corrupt-file backup & fallback.

## Module: Engine (`src/AutoDim.Core/Engine/`)
- `IntPoint(X, Y)`, `IntRect(Left, Top, Right, Bottom)` — pixel geometry + `FromNormalized()`.
- `WindowSnapshot`, `MonitorSnapshot`, `DimDrawRect` — immutable scene & draw models.
- `OcclusionPartitioner.Partition(monitorBounds, topToBottomLayers, baseDesktopAlpha)` — disjoint Z-order subtraction (no double-darkening).
- `IDimStateEngine` / `DimStateEngine` — 20s unfocus timer, presence-based hover + 10s exit timer, taskbar/zone timers, cubic-out fade easing, `HasActiveAnimations(nowMs)`, `NextDeadlineMs(nowMs)`.

## Module: Interop & Tracking (`src/AutoDim.App/Interop/`, `src/AutoDim.App/Tracking/`)
- `NativeMethods` — Win32 P/Invoke (`SetWinEventHook`, `EnumWindows`, `DwmGetWindowAttribute`, `SHQueryUserNotificationState`, `GetDpiForMonitor`, `RegisterHotKey`, `SetWindowDisplayAffinity`).
- `IWindowTracker` / `WindowTracker` — event-driven (`SetWinEventHook` + `75ms` cursor-delta poll + `SystemEvents`) Z-order, extended bounds, cloaked filtering, cached PID-to-process lookup, fullscreen detection.

## Module: Overlay (`src/AutoDim.App/Overlay/`)
- `GpuOverlayDevice` — shared D3D11/DXGI/Direct2D1/DirectComposition device with hardware->WARP fallback and `Recreate()`.
- `DirectCompositionOverlayWindow` — per-monitor topmost click-through native `CreateWindowExW` window (`WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`), `WDA_EXCLUDEFROMCAPTURE`, skip-redraw when rects unchanged.
- `OverlayCoordinator` — coalesces `SceneInvalidated`, runs 60Hz `_animTimer` only while `HasActiveAnimations`, sleeps via `_deadlineTimer` to `NextDeadlineMs`.

## Module: Shell (`src/AutoDim.App/Shell/`, `App.xaml.cs`)
- `SingleInstanceGuard` — `Local\AutoDim_SingleInstance_Mutex`.
- `StartupManager` — `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` toggle.
- `GlobalHotkeyHost` — `HWND_MESSAGE` + `RegisterHotKey` (`TryParseHotkey` supports `Ctrl/Alt/Shift/Win + Key`).
- `TrayController` — programmatic dark/paused icon, context menu (Toggle, Settings, Add Zone, Exit).

## Module: UI (`src/AutoDim.App/UI/`)
- `SettingsWindow` — dark-mode WPF window (`Timer & Helligkeit`, `Statische Zonen`, `Ausnahmen & System`) with live config persistence.
- `ZonePickerWindow` — per-monitor translucent drag-selector with edge snapping, live DPI-scaled pixel badge, and inline name/process filter confirmation.
