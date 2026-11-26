using AutoDim.Core.Config;
using AutoDim.Core.Engine;

namespace AutoDim.App.Tracking;

public sealed record SceneSnapshot(
    IReadOnlyList<MonitorSnapshot> Monitors,
    IReadOnlyList<WindowSnapshot> ZOrderedWindows,
    IntPoint CursorPos,
    long NowMs,
    nint ShellFlyoutInsertAfterHwnd = 0);

public interface IWindowTracker : IDisposable
{
    event Action? SceneInvalidated;
    event Action? DisplayOrPowerChanged;

    void Start();
    SceneSnapshot CaptureScene(DimConfig config);
}
