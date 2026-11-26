# DECISIONS (append-only)

## D-001 Stack & GPU Overlay Rendering Architecture (Planning, i-plan)
- Decision: Build AutoDim in C# / .NET 10 (`net10.0-windows`) with a clean core library (`AutoDim.Core`), WPF for the Settings UI and Visual Zone Picker, and native per-monitor Win32 click-through GPU overlays using `DirectComposition` + `Direct2D1` + `DXGI` (via `Vortice.Windows`, `WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`).
- Alternatives considered:
  - C#/.NET + WPF + CPU-based `UpdateLayeredWindow` bitmaps (higher CPU/battery overhead during 60fps fade animations on high-DPI AMOLED panels).
  - Pure C++20 Win32 + DirectComposition (minimal <10 MB RAM and builds immediately with local Clang, but significantly higher UI boilerplate for Settings sliders, exclusion lists, and visual zone picking).
- Reason / trade-offs: Matches user preference for fast UI iteration (WPF dark-mode settings + screen zone picker) while keeping AMOLED fade animations GPU-accelerated and idle CPU/GPU usage near 0%. Requires one-time installation of the `.NET 10 SDK` before building (desktop runtimes 6/8/10 are already installed).

## D-002 Per-Window & Zone Dimming State Machine (Planning, i-plan)
- Decision:
  - **Focused window**: Always un-dimmed (`alpha = 0`), except optional custom static zones overlapping inside it.
  - **Inactive windows**: Stay bright while hovered by the cursor (presence-based). When the cursor leaves an inactive window, a `10s` hover-exit timer starts before fading to dim. When a window loses focus (and cursor is not hovering it), a `20s` unfocus timer starts before fading to dim.
  - **Taskbar, desktop & custom static zones**: Always bright while hovered; start a `10s` timer when the cursor leaves, then smoothly fade to dim.
  - **Fullscreen auto-pause**: Automatically suspend dimming when a fullscreen app/game/video (`QUNS` or monitor-covering foreground window) is active.
- Alternatives considered:
  - Activity-based hover timer (resetting 10s on movement even if cursor stays parked) or global-idle dimming on the focused window.
- Reason / trade-offs: Explicitly chosen by the user so reading/watching in the active window or parked-cursor window never dims unexpectedly, while unused windows, taskbar, and static sidebars turn down AMOLED pixel load reliably.

## D-003 Disjoint Z-Order Rectangle Partitioning (Planning, i-plan)
- Decision: Resolve visible window regions in `AutoDim.Core` by walking top-to-bottom Z-order (using `DWMWA_EXTENDED_FRAME_BOUNDS` and skipping `DWMWA_CLOAKED` windows) and subtracting higher-Z bounds to produce non-overlapping `(Rect, Alpha)` draw commands per monitor.
- Alternatives considered:
  - Spawning one separate Win32 layered `HWND` per dimmed window (causes Z-order flicker, frame lag during drags, and double-darkening where translucent windows overlap).
  - Single `SetWindowRgn` cutout window (binary mask cannot render independent per-window fade alphas).
- Reason / trade-offs: Guarantees zero double-darkening overlap, smooth independent per-window/per-zone fade animations, and 100% deterministic unit testability in `AutoDim.Core.Tests`.
