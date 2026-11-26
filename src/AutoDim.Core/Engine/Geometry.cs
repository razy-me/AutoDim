using AutoDim.Core.Config;

namespace AutoDim.Core.Engine;

public readonly record struct IntPoint(int X, int Y);

public readonly record struct IntRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public bool Contains(IntPoint pt) =>
        pt.X >= Left && pt.X < Right && pt.Y >= Top && pt.Y < Bottom;

    public bool Intersects(IntRect other) =>
        !IsEmpty && !other.IsEmpty &&
        Left < other.Right && Right > other.Left &&
        Top < other.Bottom && Bottom > other.Top;

    public IntRect Intersect(IntRect other)
    {
        int l = Math.Max(Left, other.Left);
        int t = Math.Max(Top, other.Top);
        int r = Math.Min(Right, other.Right);
        int b = Math.Min(Bottom, other.Bottom);
        return r > l && b > t ? new IntRect(l, t, r, b) : default;
    }

    public static IntRect FromNormalized(IntRect monitorBounds, NormalizedRect norm)
    {
        NormalizedRect n = norm.Normalized();
        int l = monitorBounds.Left + (int)Math.Round(n.Left * monitorBounds.Width);
        int t = monitorBounds.Top + (int)Math.Round(n.Top * monitorBounds.Height);
        int r = monitorBounds.Left + (int)Math.Round(n.Right * monitorBounds.Width);
        int b = monitorBounds.Top + (int)Math.Round(n.Bottom * monitorBounds.Height);
        return new IntRect(l, t, r, b).Intersect(monitorBounds);
    }
}
