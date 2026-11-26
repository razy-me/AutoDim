namespace AutoDim.Core.Engine;

/// <summary>
/// Partitions a monitor into non-overlapping (Rect, Alpha) draw commands from top-to-bottom Z-order
/// so overlapping translucent windows never double-darken.
/// </summary>
public static class OcclusionPartitioner
{
    private const float AlphaEpsilon = 0.0005f;

    public static IReadOnlyList<DimDrawRect> Partition(
        IntRect monitorBounds,
        IReadOnlyList<(IntRect Bounds, float Alpha)> topToBottomLayers,
        float baseDesktopAlpha)
    {
        if (monitorBounds.IsEmpty)
        {
            return [];
        }

        var result = new List<DimDrawRect>(topToBottomLayers.Count + 4);
        var remaining = new List<IntRect>(16);
        var nextRemaining = new List<IntRect>(16);
        PartitionInto(monitorBounds, topToBottomLayers, baseDesktopAlpha, result, remaining, nextRemaining);
        return result;
    }

    public static void PartitionInto(
        IntRect monitorBounds,
        IReadOnlyList<(IntRect Bounds, float Alpha)> topToBottomLayers,
        float baseDesktopAlpha,
        List<DimDrawRect> output,
        List<IntRect> remainingScratch,
        List<IntRect> nextRemainingScratch)
    {
        output.Clear();
        if (monitorBounds.IsEmpty)
        {
            return;
        }

        remainingScratch.Clear();
        remainingScratch.Add(monitorBounds);

        for (int i = 0; i < topToBottomLayers.Count; i++)
        {
            var (rawBounds, alpha) = topToBottomLayers[i];
            IntRect layer = rawBounds.Intersect(monitorBounds);
            if (layer.IsEmpty)
            {
                continue;
            }

            nextRemainingScratch.Clear();
            for (int r = 0; r < remainingScratch.Count; r++)
            {
                IntRect rem = remainingScratch[r];
                if (!rem.Intersects(layer))
                {
                    nextRemainingScratch.Add(rem);
                    continue;
                }

                IntRect hit = rem.Intersect(layer);
                if (alpha > AlphaEpsilon)
                {
                    output.Add(new DimDrawRect(hit, alpha));
                }

                SubtractInto(rem, hit, nextRemainingScratch);
            }

            (remainingScratch, nextRemainingScratch) = (nextRemainingScratch, remainingScratch);
            if (remainingScratch.Count == 0)
            {
                break;
            }
        }

        if (baseDesktopAlpha > AlphaEpsilon)
        {
            for (int r = 0; r < remainingScratch.Count; r++)
            {
                IntRect rem = remainingScratch[r];
                if (!rem.IsEmpty)
                {
                    output.Add(new DimDrawRect(rem, baseDesktopAlpha));
                }
            }
        }
    }

    private static void SubtractInto(IntRect source, IntRect cut, List<IntRect> output)
    {
        if (cut.Top > source.Top)
        {
            output.Add(new IntRect(source.Left, source.Top, source.Right, cut.Top));
        }

        if (cut.Bottom < source.Bottom)
        {
            output.Add(new IntRect(source.Left, cut.Bottom, source.Right, source.Bottom));
        }

        if (cut.Left > source.Left)
        {
            output.Add(new IntRect(source.Left, cut.Top, cut.Left, cut.Bottom));
        }

        if (cut.Right < source.Right)
        {
            output.Add(new IntRect(cut.Right, cut.Top, source.Right, cut.Bottom));
        }
    }
}
