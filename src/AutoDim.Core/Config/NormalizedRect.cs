namespace AutoDim.Core.Config;

/// <summary>
/// Monitor-relative normalized rectangle with coordinates in [0, 1].
/// </summary>
public readonly record struct NormalizedRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public NormalizedRect Normalized()
    {
        double left = ClampFinite(Left, 0.0, 0.0, 0.99);
        double top = ClampFinite(Top, 0.0, 0.0, 0.99);
        double width = ClampFinite(Width, 0.1, 0.01, 1.0 - left);
        double height = ClampFinite(Height, 0.1, 0.01, 1.0 - top);
        return new NormalizedRect(left, top, width, height);
    }

    internal static double ClampFinite(double value, double fallback, double min, double max) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);
}
