using AutoDim.Core.Engine;
using Xunit;

namespace AutoDim.Core.Tests;

public class OcclusionTests
{
    [Fact]
    public void Partition_OverlappingWindows_ProducesDisjointRectanglesWithNoDoubleDarkening()
    {
        var monitor = new IntRect(0, 0, 1920, 1080);
        var topFocused = new IntRect(100, 100, 900, 700);     // alpha 0
        var middleInactive = new IntRect(400, 200, 1400, 900); // alpha 0.80

        var rects = OcclusionPartitioner.Partition(
            monitor,
            [(topFocused, 0f), (middleInactive, 0.80f)],
            baseDesktopAlpha: 0.85f);

        // Verify no two emitted rectangles overlap
        for (int i = 0; i < rects.Count; i++)
        {
            for (int j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].Bounds.Intersects(rects[j].Bounds),
                    $"Rect {i} ({rects[i].Bounds}) overlaps Rect {j} ({rects[j].Bounds})");
            }
        }

        // Verify focused window area is completely uncovered (no draw rects intersect it)
        foreach (var r in rects)
        {
            Assert.False(r.Bounds.Intersects(topFocused));
        }

        // Verify total area covered + focused area equals monitor area
        long coveredArea = rects.Sum(r => (long)r.Bounds.Width * r.Bounds.Height);
        long focusedArea = (long)topFocused.Width * topFocused.Height;
        Assert.Equal((long)monitor.Width * monitor.Height, coveredArea + focusedArea);
    }

    [Fact]
    public void Partition_WindowOutsideMonitor_ClipsToMonitorBounds()
    {
        var monitor = new IntRect(1920, 0, 3840, 1080);
        var spanningWindow = new IntRect(1500, 100, 2500, 500);

        var rects = OcclusionPartitioner.Partition(
            monitor,
            [(spanningWindow, 0.5f)],
            baseDesktopAlpha: 0f);

        var single = Assert.Single(rects);
        Assert.Equal(new IntRect(1920, 100, 2500, 500), single.Bounds);
        Assert.Equal(0.5f, single.Alpha);
    }
}
