# SPEC
## Original Prompt (verbatim)
Ein leichtgewichtiges Win32-Overlay, das inaktive Fenster oder statische UI-Bereiche (wie die Taskleiste oder Sidebars) nach ein paar Sekunden Inaktivität sanft abdunkelt. Auf dem AMOLED-Panel spart das real Akku, schont die Pixel und lenkt den Fokus direkt aufs aktive Fenster.

## Requirements
- `R1` (Focused Window Always Bright): The active foreground window remains un-dimmed (`alpha = 0`), except inside explicitly overlapping, enabled custom static zones. [functional]
- `R2` (Unfocus Grace Timer): When a window loses focus while the cursor is not inside it, a configurable unfocus timer (default `20s`) starts before smoothly fading to `InactiveWindowOpacity` (default `0.80`). Regaining focus or hovering cancels the timer and brightens it immediately. [functional]
- `R3` (Presence-Based Hover & 10s Exit Timer): While the cursor hovers over an inactive window, taskbar (`Shell_TrayWnd` / `Shell_SecondaryTrayWnd`), or static UI zone, that target stays un-dimmed (`alpha = 0`). When the cursor exits the target, a configurable hover-exit timer (default `10s`) starts before smoothly fading to its dim opacity. [functional]
- `R4` (GPU Zero-Overdraw Click-Through Overlay): Renders pure `#000000` (configurable tint) per monitor via `DirectComposition` + `Direct2D1` (`WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`), partitioning Z-order into disjoint rectangles so overlapping windows never double-darken, and stopping frame ticks (`0%` render CPU/GPU) once fades settle. [non-functional: performance]
- `R5` (Taskbar, Desktop & Static UI Zones): Independently configurable dimming for primary/secondary taskbars, desktop background, and user-defined static zones (monitor-relative bounds + optional process filter). [functional]
- `R6` (Fullscreen Auto-Pause, Exclusions & Capture Protection): Automatically pauses dimming when fullscreen apps/games/video (`SHQueryUserNotificationState` or full-monitor foreground `RECT`) are active; supports process/window exclusions and optional `WDA_EXCLUDEFROMCAPTURE` so screenshots/screen-shares stay bright. [functional]
- `R7` (Multi-Monitor, DPIv2 & Power/Display Resilience): Per-Monitor DPIv2 aware; cleanly recreates/resizes overlay surfaces on `WM_DISPLAYCHANGE` / `WM_DPICHANGED`, and resets/pauses across sleep/resume (`WM_POWERBROADCAST`) and session lock/unlock (`WM_WTSSESSION_CHANGE`). [non-functional: reliability]
- `R8` (System Tray, Global Hotkey & Autostart): Single-instance process with Tray icon (Enable/Pause toggle, Settings, Visual Zone Picker, Exit), configurable `RegisterHotKey` pause toggle (default `Ctrl+Alt+D`), and optional `HKCU\...\Run` autostart toggle. [functional]
- `R9` (WPF Dark-Mode Settings & Config Persistence): Clean dark-mode WPF settings UI for timers, opacities, fade speeds, exclusions, zones, and hotkey, persisted atomically as JSON in `%AppData%\AutoDim\config.json`. [functional]
- `R10` (Visual Interactive Zone Picker): Translucent cross-monitor drag-selection overlay with live dimension readout and edge/window snapping to create or edit static zones visually. [functional]

## Assumptions
- `A1`: Target OS is Windows 10 (2004+) / Windows 11 x64 (`WDA_EXCLUDEFROMCAPTURE` and `DirectComposition` supported).
- `A2`: Desktop runtimes (`.NET 6/8/10`) are installed on the host, while the `.NET 10 SDK` (`Microsoft.DotNet.SDK.10`) must be installed once during `WP-01` before `dotnet build` runs.
- `A3`: Empty desktop wallpaper (`Progman` / `WorkerW`) follows the presence-based `10s` hover-exit timer (bright while hovered or focused, dims `10s` after cursor leaves).

## Out of Scope
- Hardware monitor backlight / DDC/CI brightness control.
- Injecting DLLs into third-party processes or hooking into exclusive DirectX/Vulkan game render pipelines.
- Modifying internal dark/light themes of third-party applications.
