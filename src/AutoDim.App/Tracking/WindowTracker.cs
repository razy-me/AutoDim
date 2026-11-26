using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AutoDim.App.Interop;
using AutoDim.Core.Config;
using AutoDim.Core.Engine;
using Microsoft.Win32;

namespace AutoDim.App.Tracking;

public sealed unsafe class WindowTracker : IWindowTracker
{
    private const int CursorPollIntervalMs = 75;
    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private static readonly HashSet<string> DesktopClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW"
    };

    private static readonly HashSet<string> SystemShellFlyouts = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost.exe",
        "SearchHost.exe",
        "ShellExperienceHost.exe",
        "TextInputHost.exe",
        "LockApp.exe"
    };

    private readonly Dictionary<uint, string> _pidNameCache = new();
    private readonly HashSet<uint> _activePidsScratch = new();
    private readonly List<uint> _stalePidsScratch = new();
    private readonly List<nint> _hooks = new();
    private readonly DispatcherTimer _cursorTimer;
    private IReadOnlyList<string>? _lastExcludedList;
    private HashSet<string> _excludedSet = new(StringComparer.OrdinalIgnoreCase);
    private NativeMethods.WinEventProc? _winEventDelegate;

    private IntPoint _lastCursorPos;
    private bool _started;
    private bool _disposed;

    public event Action? SceneInvalidated;
    public event Action? DisplayOrPowerChanged;

    public WindowTracker(Dispatcher dispatcher)
    {
        _cursorTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(CursorPollIntervalMs)
        };
        _cursorTimer.Tick += OnCursorTick;
    }

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        _winEventDelegate = OnWinEvent;
        InstallHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND);
        InstallHook(0x000A, NativeMethods.EVENT_SYSTEM_MOVESIZEEND); // MOVESIZESTART..MOVESIZEEND
        InstallHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND);
        InstallHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_HIDE);
        InstallHook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE);

        NativeMethods.GetCursorPos(out var pt);
        _lastCursorPos = new IntPoint(pt.X, pt.Y);
        _cursorTimer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public SceneSnapshot CaptureScene(DimConfig config)
    {
        long nowMs = Environment.TickCount64;
        NativeMethods.GetCursorPos(out var cursorPt);
        var cursorPos = new IntPoint(cursorPt.X, cursorPt.Y);
        _lastCursorPos = cursorPos;

        nint foregroundHwnd = NativeMethods.GetForegroundWindow();
        if (!ReferenceEquals(config.ExcludedProcesses, _lastExcludedList))
        {
            _lastExcludedList = config.ExcludedProcesses;
            _excludedSet = new HashSet<string>(config.ExcludedProcesses, StringComparer.OrdinalIgnoreCase);
        }

        var windows = EnumerateWindows(foregroundHwnd, _excludedSet, out nint shellFlyoutInsertAfterHwnd);
        var monitors = EnumerateMonitors(windows, foregroundHwnd);

        return new SceneSnapshot(monitors, windows, cursorPos, nowMs, shellFlyoutInsertAfterHwnd);
    }

    private List<WindowSnapshot> EnumerateWindows(nint foregroundHwnd, HashSet<string> excludedProcesses, out nint shellFlyoutInsertAfterHwnd)
    {
        var list = new List<WindowSnapshot>(32);
        _activePidsScratch.Clear();
        nint lowestShellFlyoutHwnd = 0;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

            int exStyle = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0)
            {
                return true;
            }

            int cloaked = 0;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            if (!TryGetExtendedBounds(hwnd, out IntRect bounds) || bounds.IsEmpty)
            {
                return true;
            }

            string className = GetWindowClassName(hwnd);
            bool isTaskbar = TaskbarClasses.Contains(className);
            bool isDesktop = DesktopClasses.Contains(className);
            _activePidsScratch.Add(pid);
            string procName = GetProcessName(pid);

            bool isTopmost = (exStyle & (int)NativeMethods.WS_EX_TOPMOST) != 0;
            bool isExplorerFlyout = isTopmost &&
                !isTaskbar &&
                !isDesktop &&
                string.Equals(procName, "explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(className, "CabinetWClass", StringComparison.OrdinalIgnoreCase);

            if (isTopmost && (isExplorerFlyout || SystemShellFlyouts.Contains(procName)))
            {
                // Keep Windows 11 tray/shell XAML flyouts above AutoDim's overlay without punching oversized rectangular holes.
                lowestShellFlyoutHwnd = hwnd;
                return true;
            }

            bool isExcluded = excludedProcesses.Contains(procName) ||
                (!string.IsNullOrEmpty(procName) && excludedProcesses.Contains(Path.GetFileNameWithoutExtension(procName))) ||
                SystemShellFlyouts.Contains(procName);
            bool isFocused = hwnd == foregroundHwnd;

            list.Add(new WindowSnapshot(
                Handle: hwnd,
                ProcessName: procName,
                ClassName: className,
                Bounds: bounds,
                IsFocused: isFocused,
                IsTaskbar: isTaskbar,
                IsDesktop: isDesktop,
                IsExcluded: isExcluded));

            return true;
        }, 0);

        shellFlyoutInsertAfterHwnd = lowestShellFlyoutHwnd;

        if (_pidNameCache.Count > _activePidsScratch.Count)
        {
            _stalePidsScratch.Clear();
            foreach (uint cachedPid in _pidNameCache.Keys)
            {
                if (!_activePidsScratch.Contains(cachedPid))
                {
                    _stalePidsScratch.Add(cachedPid);
                }
            }
            for (int i = 0; i < _stalePidsScratch.Count; i++)
            {
                _pidNameCache.Remove(_stalePidsScratch[i]);
            }
        }

        return list;
    }

    private static List<MonitorSnapshot> EnumerateMonitors(IReadOnlyList<WindowSnapshot> windows, nint foregroundHwnd)
    {
        var monitors = new List<MonitorSnapshot>(4);
        int qunsHr = NativeMethods.SHQueryUserNotificationState(out int qunsState);
        // Ignore QUNS_BUSY (2) because AutoDim's own fullscreen topmost overlay triggers QUNS_BUSY in Windows.
        bool globalFullscreen = qunsHr == 0 && qunsState is 3 or 4;
        WindowSnapshot? fgWindow = windows.FirstOrDefault(w => w.Handle == foregroundHwnd && !w.IsDesktop && !w.IsTaskbar);

        NativeMethods.EnumDisplayMonitors(0, null, (hMon, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEXW { cbSize = (uint)sizeof(NativeMethods.MONITORINFOEXW) };
            if (!NativeMethods.GetMonitorInfoW(hMon, ref info))
            {
                return true;
            }

            var bounds = new IntRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
            var deviceSpan = new ReadOnlySpan<char>(info.szDevice, 32);
            int nullIdx = deviceSpan.IndexOf('\0');
            string deviceId = new string(nullIdx >= 0 ? deviceSpan[..nullIdx] : deviceSpan);

            double dpiScale = 1.0;
            if (NativeMethods.GetDpiForMonitor(hMon, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                dpiScale = dpiX / 96.0;
            }

            bool boundsFullscreen = fgWindow is not null &&
                fgWindow.Bounds.Left <= bounds.Left &&
                fgWindow.Bounds.Top <= bounds.Top &&
                fgWindow.Bounds.Right >= bounds.Right &&
                fgWindow.Bounds.Bottom >= bounds.Bottom;
            bool hasFullscreen = globalFullscreen || boundsFullscreen;

            monitors.Add(new MonitorSnapshot(deviceId, bounds, dpiScale, hasFullscreen));
            return true;
        }, 0);

        return monitors;
    }

    private static bool TryGetExtendedBounds(nint hwnd, out IntRect bounds)
    {
        NativeMethods.RECT rect = default;
        int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(NativeMethods.RECT));
        if (hr != 0 && !NativeMethods.GetWindowRect(hwnd, out rect))
        {
            bounds = default;
            return false;
        }

        bounds = new IntRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return !bounds.IsEmpty;
    }

    private static string GetWindowClassName(nint hwnd)
    {
        char* buffer = stackalloc char[256];
        int len = NativeMethods.GetClassNameW(hwnd, buffer, 256);
        return len > 0 ? new string(buffer, 0, len) : string.Empty;
    }

    private string GetProcessName(uint pid)
    {
        if (_pidNameCache.TryGetValue(pid, out string? cached))
        {
            return cached;
        }

        if (_pidNameCache.Count > 256)
        {
            _pidNameCache.Clear();
        }

        string name = string.Empty;
        nint hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc != 0)
        {
            try
            {
                char* buffer = stackalloc char[512];
                uint size = 512;
                if (NativeMethods.QueryFullProcessImageNameW(hProc, 0, buffer, ref size) && size > 0)
                {
                    string fullPath = new string(buffer, 0, (int)size);
                    name = Path.GetFileName(fullPath);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }

        _pidNameCache[pid] = name;
        return name;
    }

    private void InstallHook(uint eventMin, uint eventMax)
    {
        nint hook = NativeMethods.SetWinEventHook(
            eventMin,
            eventMax,
            0,
            _winEventDelegate!,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        if (hook != 0)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0)
        {
            return;
        }

        SceneInvalidated?.Invoke();
    }

    private void OnCursorTick(object? sender, EventArgs e)
    {
        if (NativeMethods.GetCursorPos(out var pt))
        {
            var current = new IntPoint(pt.X, pt.Y);
            if (current != _lastCursorPos)
            {
                _lastCursorPos = current;
                SceneInvalidated?.Invoke();
            }
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => DisplayOrPowerChanged?.Invoke();
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) => DisplayOrPowerChanged?.Invoke();
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => DisplayOrPowerChanged?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cursorTimer.Stop();
        foreach (nint hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }
        _hooks.Clear();

        if (_started)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
    }
}
