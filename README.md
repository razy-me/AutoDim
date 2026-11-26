# AutoDim

Lightweight, GPU-accelerated Win32/DirectComposition overlay for Windows 10/11 that gently dims inactive windows, the taskbar, desktop, and custom static UI zones on AMOLED/OLED panels to save battery, reduce burn-in, and keep focus on the active window.

## Features
- **Presence & Focus-Based Dimming**:
  - Focused window stays bright.
  - Inactive windows dim after `20s` (configurable) when unfocused.
  - Hover dwell (`350 ms` default) prevents accidental brightening when sweeping the mouse across windows; stays bright while hovered and dims `10s` after cursor exit.
  - Taskbar, desktop, and custom static UI zones use the presence-based hover timer.
- **Zero-Overdraw GPU Compositing**:
  - Per-monitor click-through `DirectComposition` + `Direct2D1` overlays (`WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`).
  - Disjoint Z-order rectangle partitioning prevents double-darkening on overlapping windows.
  - Windows 11 rounded-corner spandrel dimming (`8 DIP` scaled per monitor DPI) & configurable frame inset (`FrameInsetPx`).
  - `0%` render wakeups when settled.
- **System Tray, Hotkey & Visual Zone Picker**:
  - Single-instance tray app with dark-mode WPF Settings (`%AppData%\AutoDim\config.json`), global pause hotkey (`Ctrl+Alt+D`), process exclusions & per-process opacity multipliers, fullscreen auto-pause, `WDA_EXCLUDEFROMCAPTURE` toggle, and cross-monitor Visual Zone Picker.

## Build & Run
Requires **.NET 10 SDK** on Windows 10 (2004+) / Windows 11 x64:

```powershell
dotnet test AutoDim.slnx -c Release
dotnet publish src/AutoDim.App/AutoDim.App.csproj -c Release -r win-x64 --no-self-contained -o publish/win-x64
.\publish\win-x64\AutoDim.exe
```
