# Fix Ledger: Overlay clicks pass through, dimming blocked by QUNS_BUSY, bright window edge borders
Status: FIXED
Mode: i-debug

## Symptom
1. Overlay did not visibly dim after 30s due to `QUNS_BUSY (2)` triggered by AutoDim's own `HWND_TOPMOST` fullscreen overlay.
2. Once dimming worked, 1–7 px bright halos appeared around active floating windows (`DWMWA_EXTENDED_FRAME_BOUNDS` includes rounded corners / translucent border edges on Windows 11 at 150% DPI).

## Attempts
| # | Source | Hypothesis | Change (files) | Result | Why it failed / learned |
|---|---|---|---|---|---|
| 1 | i-debug | `SHQueryUserNotificationState` returns `QUNS_BUSY (2)` for AutoDim's own overlay | `src/AutoDim.App/Tracking/WindowTracker.cs` (only pause on `QUNS` 3/4 + bounds) | FIXED (`[VERIFIED]`) | Dimming works reliably while real fullscreen apps still pause. |
| 2 | i-debug (user confirmed) | Configurable `FrameInsetPx` (`0..16 px`, default `3 px`) shrinks non-monitor-flush window bounds | `DimConfig.cs`, `DimStateEngine.cs`, `SettingsWindow.xaml*`, `EngineTests.cs` | FIXED (`18/18` tests green) | Live slider lets user tune exact px cutoff; monitor-flush edges stay pixel-exact. |

## Final
Root cause: (1) `QUNS_BUSY (2)` self-triggered by AutoDim overlay; (2) 1–7 px DWM border/rounded-corner bleed outside visible opaque window edges. | Fix: Ignore `QUNS_BUSY (2)` in `WindowTracker.cs`; add `FrameInsetPx` (default `3 px`, live slider `0..16 px`) in `DimConfig` + `DimStateEngine.ApplyFrameInset` + `SettingsWindow`; removed temporary `DiagLog`. | Proof: `dotnet test AutoDim.slnx -c Release` (`18/18` passed), `dotnet publish` clean.
