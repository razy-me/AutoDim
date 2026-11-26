# TODO
Plan Status: APPROVED 2026-11-26

## Stack (with justification)
- C# / `.NET 10` (`net10.0-windows`) with `AutoDim.Core` (pure logic + `xUnit` tests) and `AutoDim.App` (WPF UI + Tray + `Vortice.Windows` `DirectComposition`/`Direct2D1` GPU overlays). See [DECISIONS.md](DECISIONS.md) (`D-001`–`D-003`).

## Architecture
`SetWinEventHook` + lightweight cursor polling -> `WindowTracker.CaptureScene()` -> `DimStateEngine.UpdateScene()` -> `OcclusionPartitioner.Partition()` -> `MonitorOverlayHost` (`DirectComposition` + `Direct2D1`)

## Folder Layout
- `AutoDim.sln`
- `src/AutoDim.Core/` (Config, Engine, Occlusion)
- `src/AutoDim.App/` (Interop, Tracking, Overlay, Shell, UI)
- `tests/AutoDim.Core.Tests/` (xUnit unit tests)

## Work Packages
### WP-01 [i] SDK Prerequisite & Solution Scaffolding  [x] status: approved
- Covers: —
- Owns: `AutoDim.sln`, `src/AutoDim.Core/**`, `src/AutoDim.App/**`, `tests/AutoDim.Core.Tests/**`, `docs/agents/CONTEXT.md`
- Depends on: —
- Acceptance: `.NET 10 SDK` installed; `dotnet build` & `dotnet test` green; `CONTEXT.md` created

### WP-02 [r] Configuration & Atomic JSON Persistence  [x] status: approved
- Covers: R5, R9
- Owns: `src/AutoDim.Core/Config/**`, `tests/AutoDim.Core.Tests/ConfigTests.cs`
- Depends on: WP-01
- Acceptance: `dotnet test --filter ConfigTests` passes (round-trip, clamping, corrupt fallback)

### WP-03 [r] DimStateEngine & OcclusionPartitioner  [x] status: approved
- Covers: R1, R2, R3, R4, R5
- Owns: `src/AutoDim.Core/Engine/**`, `tests/AutoDim.Core.Tests/EngineTests.cs`, `tests/AutoDim.Core.Tests/OcclusionTests.cs`
- Depends on: WP-02
- Acceptance: `dotnet test` passes 100% of timer, hover-presence, occlusion, and deadline tests

### WP-04 [i] Win32 Window, Hover & Fullscreen Tracker  [x] status: approved
- Covers: R1, R2, R3, R6, R7
- Owns: `src/AutoDim.App/Interop/**`, `src/AutoDim.App/Tracking/**`
- Depends on: WP-01, WP-03
- Acceptance: Accurate Z-order (`DWMWA_EXTENDED_FRAME_BOUNDS`, `DWMWA_CLOAKED`), hover tracking, fullscreen auto-pause

### WP-05 [i] Per-Monitor DirectComposition GPU Overlay  [x] status: approved
- Covers: R4, R6, R7
- Owns: `src/AutoDim.App/Overlay/**`
- Depends on: WP-03, WP-04
- Acceptance: Click-through pure-black per-monitor overlays render without double-darkening; 0% CPU when settled

### WP-06 [r] System Tray, Hotkey, Mutex & Autostart  [x] status: approved
- Covers: R8
- Owns: `src/AutoDim.App/Shell/**`
- Depends on: WP-02, WP-05
- Acceptance: Single-instance tray app, `Ctrl+Alt+D` pause toggle, `HKCU\...\Run` autostart toggle

### WP-07 [i] WPF Dark-Mode Settings & Visual Zone Picker  [x] status: approved
- Covers: R5, R9, R10
- Owns: `src/AutoDim.App/UI/**`
- Depends on: WP-02, WP-05, WP-06
- Acceptance: Settings sliders/exclusions/zones persist and update live; drag-select Zone Picker works across monitors

### WP-08 [r] Integration Verification & Release Packaging  [x] status: approved
- Covers: R1..R10
- Owns: `tests/**`, `docs/agents/CONTEXT.md`
- Depends on: WP-04, WP-05, WP-06, WP-07
- Acceptance: Full `dotnet test`, multi-monitor/DPI/sleep checklist verified, `dotnet publish -c Release` green

## Phase Status
- [x] Phase 1 Plan  - [x] Phase 2 Build  - [x] Phase 3 Test  - [x] Phase 4 Review
