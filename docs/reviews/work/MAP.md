# AutoDim Architecture Map
- **Stack**: C# / `.NET 10` (`net10.0` Core/Tests, `net10.0-windows` WPF + WinForms + `Vortice.* 3.8.3`).
- **Entry Point**: `src/AutoDim.App/App.xaml.cs` (`SingleInstanceGuard` mutex -> `JsonConfigStore.Load` -> `DimStateEngine` -> `WindowTracker` -> `OverlayCoordinator` -> `GlobalHotkeyHost` + `TrayController`).
- **Trust Boundaries**: `%AppData%\AutoDim\config.json` (JSON deserialization + validation), Win32 `HWND` / process-name enumeration (`WindowTracker`), `HKCU\...\Run` registry write (`StartupManager`), global hotkey (`RegisterHotKey`).
- **Shared Modules**: `AutoDim.Core.Config` (`DimConfig`, `ZoneDefinition`, `NormalizedRect`, `JsonConfigStore`), `AutoDim.Core.Engine` (`DimStateEngine`, `OcclusionPartitioner`, `IntRect`).
- **GPU / Win32 Layer**: `NativeMethods` -> `WindowTracker` (`SetWinEventHook` + `75ms` cursor poll) -> `OverlayCoordinator` -> `DirectCompositionOverlayWindow` (`D3D11`/`DXGI`/`D2D1`/`DComp`).
- **UI Layer**: `SettingsWindow` (live-save sliders/exclusions/zones/hotkey) & `ZonePickerWindow` (per-monitor translucent drag selector).
