# Fix Ledger: Overlay blocks mouse clicks across windows
Status: FIXED
Mode: i-debug

## Symptom
Expected: AutoDim overlays are completely click-through so the user can click any window or UI element underneath normally.
Actual: While AutoDim is running, almost nothing on screen can be clicked with the mouse; user had to navigate with `Tab` inside Antigravity to ask for AutoDim to be stopped.
Trigger: Launching `AutoDim.exe` (creates per-monitor `DirectCompositionOverlayWindow` instances).
Environment: Windows 10/11 x64, `.NET 10` (`net10.0-windows`), `HwndSource` + `DirectComposition`.
Success criterion: `WindowFromPoint` at any pixel covered by `DirectCompositionOverlayWindow` returns the underlying window (`hitSelf == False`), `GWL_EXSTYLE` contains `WS_EX_LAYERED | WS_EX_TRANSPARENT` (`0x00080020`), and mouse clicks pass through to all other processes while DirectComposition dimming continues to render.

## Repro
Command/script: PowerShell `Probe` creating `HwndSource` with `DirectCompositionOverlayWindow.cs:40-53` flags (`0x082000A8`) and calling Win32 `WindowFromPoint((250, 250))`.
Output (symptom reproduced: yes):
- Before fix: `NoLayered: actualStyle=0x96000000, actualEx=0x082000A8, hitSelf=True`
- After fix (live `AutoDim.exe`): `OverlayHWND: title=AutoDimOverlay_0_0, exStyle=0x082800A8`, `Point(300,300) -> isAutoDim=False`, `Point(960,540) -> isAutoDim=False`, `Point(1200,800) -> isAutoDim=False`.

## Attempts
| # | Source | Hypothesis | Change (files) | Result | Why it failed / learned |
|---|---|---|---|---|---|
| 1 | i-debug (non-destructive probe) | `HwndSource` omits `WS_EX_LAYERED`, causing `WS_EX_TRANSPARENT` not to pass clicks cross-process | Read-only PowerShell probe | Root cause `[VERIFIED]` | `actualEx=0x082000A8` (no `0x00080000`), `WindowFromPoint` hits overlay (`hitSelf=True`). |
| 2 | i-debug (approved minimal fix) | Replace WPF `HwndSource` with native Win32 `CreateWindowExW` (`WS_EX_NOREDIRECTIONBITMAP \| WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_TOPMOST \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`) | `src/AutoDim.App/Interop/NativeMethods.cs`, `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs` | FIXED (`[VERIFIED]`) | Live `AutoDim.exe` reports `exStyle=0x082800A8` and `WindowFromPoint` returns `isAutoDim=False` across all test points. |

## Established Facts (VERIFIED)
- `[VERIFIED]` `HwndSourceParameters` with `UsesPerPixelOpacity = false` stripped `WS_EX_LAYERED`, leaving `actualEx = 0x082000A8` and `WindowFromPoint -> hitSelf = True`.
- `[VERIFIED]` `CreateWindowExW` with `WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` preserves `exStyle = 0x082800A8` and yields `isAutoDim = False` on live `WindowFromPoint` checks.

## Ruled Out
- `WM_NCHITTEST -> HTTRANSPARENT` alone - insufficient across processes (`hitSelf=True`).
- `SetWindowLong` adding `WS_EX_LAYERED` after `HwndSource` creation - did not set `0x00080000`.

## Final
Root cause: WPF `HwndSource` stripped `WS_EX_LAYERED`, preventing `WS_EX_TRANSPARENT` from passing mouse clicks to other processes. | Fix: Replaced `HwndSource` in `DirectCompositionOverlayWindow` with native `CreateWindowExW("Static", ...)` + `SetLayeredWindowAttributes(..., 255, LWA_ALPHA)` + `DestroyWindow`. | Proof: `dotnet test AutoDim.slnx -c Release` (17/17 passed), `dotnet publish`, and live `AutoDim.exe` probe (`exStyle=0x082800A8`, `isAutoDim=False` at `(300,300)`, `(960,540)`, `(1200,800)`). | Other occurrences: Checked `ZonePickerWindow` (intentionally interactive, not click-through) and `GlobalHotkeyHost` (0x0 hidden `HWND_MESSAGE` window).
Not verified: Visual dimming opacity on physical monitor after `SetLayeredWindowAttributes` (pending user runtime check).
