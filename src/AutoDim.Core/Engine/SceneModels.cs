namespace AutoDim.Core.Engine;

public enum CornerKind
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed record WindowSnapshot(
    nint Handle,
    string ProcessName,
    string ClassName,
    IntRect Bounds,
    bool IsFocused,
    bool IsTaskbar,
    bool IsDesktop,
    bool IsExcluded);

public sealed record MonitorSnapshot(
    string DeviceId,
    IntRect Bounds,
    double DpiScale,
    bool HasFullscreenApp);

public readonly record struct DimDrawRect(IntRect Bounds, float Alpha);

public readonly record struct DimCornerSpandrel(
    IntRect CornerBounds,
    CornerKind Kind,
    float Radius,
    float Alpha);

public sealed record MonitorDrawPlan(
    IReadOnlyList<DimDrawRect> Rects,
    IReadOnlyList<DimCornerSpandrel> Corners);
