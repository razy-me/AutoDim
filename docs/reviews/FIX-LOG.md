# Fix Log
**Date**: 2026-11-26 | **Source**: `docs/reviews/r-review-findings.md`

| ID | Severity | File | Status | Regression Test |
|---|---|---|---|---|
| R-01 | MINOR | `src/AutoDim.Core/Config/NormalizedRect.cs` | FIXED | `ConfigTests.NormalizedRect_NaNWidthNearRightEdge_ClampsToRemainingSpace` |
| R-02 | MINOR | `src/AutoDim.Core/Config/DimConfig.cs` | FIXED | `ConfigTests.DimConfig_ValueEquality_ComparesCollectionsBySequence` |
| P2-01 | CRITICAL | `src/AutoDim.App/Overlay/OverlayCoordinator.cs`, `GpuOverlayDevice.cs`, `DirectCompositionOverlayWindow.cs` | FIXED | — (GPU/COM lifecycle) |
| P2-02 | MAJOR | `src/AutoDim.App/Overlay/OverlayCoordinator.cs` | FIXED | — (GPU recovery refresh) |
| P2-03 | MAJOR | `src/AutoDim.App/Tracking/WindowTracker.cs` | FIXED | — (Win32 WinEvent filter) |
| P2-04 | MINOR | `src/AutoDim.App/Tracking/WindowTracker.cs` | FIXED | — |
| P2-05 | MINOR | `src/AutoDim.App/Tracking/WindowTracker.cs` | FIXED | — |
| P2-06 | MINOR | `src/AutoDim.App/Overlay/OverlayCoordinator.cs` | FIXED | — |
| X-DPI-01 | MAJOR | `src/AutoDim.App/Tracking/WindowTracker.cs` | FIXED | — (Win32 self-window tracking) |
| X-DPI-02 | MAJOR | `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs`, `OverlayCoordinator.cs` | FIXED | — (Z-order topmost) |
| X-DPI-03 | MAJOR | `src/AutoDim.App/UI/ZonePickerWindow.xaml.cs` | FIXED | — (Mixed-DPI SetWindowPos) |
| P3-01 | MAJOR | `src/AutoDim.App/Shell/GlobalHotkeyHost.cs` | FIXED | — (Win32 RegisterHotKey rollback) |
| P3-02 | MAJOR | `src/AutoDim.App/App.xaml.cs` | FIXED | — (WPF Closed lifecycle) |
| P3-03 | MAJOR | `src/AutoDim.App/UI/SettingsWindow.xaml.cs` | FIXED | — (WPF Reset defaults) |
| P3-04 | MINOR | `src/AutoDim.App/UI/SettingsWindow.xaml.cs` | FIXED | — |
| P3-05 | MINOR | `src/AutoDim.App/UI/SettingsWindow.xaml.cs` | FIXED | — |
| P3-06 | MINOR | `src/AutoDim.App/Shell/GlobalHotkeyHost.cs` | FIXED | — |
| P1-01 | MAJOR | `src/AutoDim.Core/Engine/DimStateEngine.cs` | FIXED | `EngineTests.ExcludedDesktopWindow_DoesNotDimDesktop` |
| P1-02 | MAJOR | `src/AutoDim.Core/Engine/DimStateEngine.cs` | FIXED | `EngineTests.ExcludedBackgroundWindow_OccludedByUnfocusedForegroundWindow_DoesNotPunchCutout` |
| P1-03 | MINOR | `src/AutoDim.Core/Engine/DimStateEngine.cs` | FIXED | `EngineTests.DuplicateMonitorDeviceId_DoesNotThrowInUpdateScene` |
| X-STATE-01 | MINOR | `src/AutoDim.Core/Engine/DimStateEngine.cs` | FIXED | `EngineTests.AllMonitorsFullscreenPaused_SuppressesActiveAnimationsAndDeadlines` |
| L-01 | MINOR | `src/AutoDim.Core/Engine/DimStateEngine.cs` | FIXED | `EngineTests.FadingWindow_PreservesRoundedCornerSpandrelsWithoutPop` |
| L-02 | MINOR | `src/AutoDim.App/Overlay/DirectCompositionOverlayWindow.cs` | FIXED | — (constructor HWND cleanup) |
