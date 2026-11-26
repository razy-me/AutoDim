using AutoDim.Core.Config;
using AutoDim.Core.Engine;
using Xunit;

namespace AutoDim.Core.Tests;

public class EngineTests
{
    private static readonly MonitorSnapshot Mon1 = new("DISPLAY1", new IntRect(0, 0, 1920, 1080), 1.0, false);
    private static readonly DimConfig DefaultCfg = DimConfig.Default with { FadeOutMs = 400, FadeInMs = 100, FrameInsetPx = 0 };

    [Fact]
    public void FocusedWindow_StaysBright_UnfocusedWindowDimsAfter20Seconds()
    {
        var engine = new DimStateEngine();
        var winA = new WindowSnapshot(1, "appA.exe", "ClassA", new IntRect(0, 0, 800, 600), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var winB = new WindowSnapshot(2, "appB.exe", "ClassB", new IntRect(900, 0, 1800, 600), IsFocused: false, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        // Initial scene: winA focused, cursor inside winA
        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(100, 100), nowMs: 0, DefaultCfg);

        // Switch focus to winB, cursor moves to winB at t=1000 -> winA starts 20s unfocus timer
        var winAUnfocused = winA with { IsFocused = false };
        var winBFocused = winB with { IsFocused = true };
        engine.UpdateScene([Mon1], [winBFocused, winAUnfocused], new IntPoint(1000, 100), nowMs: 1000, DefaultCfg);

        Assert.Equal(21_000, engine.NextDeadlineMs(1000));

        // At t=20_999: winA still bright
        var rectsBefore = engine.ComputeDrawRectsForMonitor("DISPLAY1", 20_999);
        Assert.DoesNotContain(rectsBefore, r => r.Bounds.Intersects(winA.Bounds));

        // At t=21_000 + 400 (after FadeOutMs): winA reaches 0.80
        var rectsAfter = engine.ComputeDrawRectsForMonitor("DISPLAY1", 21_400);
        Assert.Contains(rectsAfter, r => r.Bounds == winA.Bounds && Math.Abs(r.Alpha - 0.80f) < 0.01f);
        Assert.DoesNotContain(rectsAfter, r => r.Bounds.Intersects(winB.Bounds));
    }

    [Fact]
    public void PresenceHover_KeepsInactiveWindowBright_ThenStarts10sTimerOnExit()
    {
        var engine = new DimStateEngine();
        var winA = new WindowSnapshot(1, "appA.exe", "ClassA", new IntRect(0, 0, 800, 600), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var winB = new WindowSnapshot(2, "appB.exe", "ClassB", new IntRect(900, 0, 1800, 600), IsFocused: false, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        // Start at t=0, winB unfocused, cursor in winA; advance past 20s + 400ms fade so winB is dimmed
        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(100, 100), nowMs: 0, DefaultCfg);
        var dimmedRects = engine.ComputeDrawRectsForMonitor("DISPLAY1", 20_500);
        Assert.Contains(dimmedRects, r => r.Bounds == winB.Bounds && r.Alpha > 0.7f);

        // Hover over winB at t=25_000 -> brightens immediately & stays bright even 30s later while cursor remains inside
        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(1000, 100), nowMs: 25_000, DefaultCfg);
        var hoveredRects = engine.ComputeDrawRectsForMonitor("DISPLAY1", 55_000);
        Assert.DoesNotContain(hoveredRects, r => r.Bounds.Intersects(winB.Bounds));

        // Cursor exits winB back to winA at t=55_000 -> 10s hover-exit timer starts
        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(100, 100), nowMs: 55_000, DefaultCfg);
        Assert.Equal(65_000, engine.NextDeadlineMs(55_000));

        // Still bright at t=64_999
        Assert.DoesNotContain(engine.ComputeDrawRectsForMonitor("DISPLAY1", 64_999), r => r.Bounds.Intersects(winB.Bounds));

        // Dimmed after t=65_400
        var exitedRects = engine.ComputeDrawRectsForMonitor("DISPLAY1", 65_400);
        Assert.Contains(exitedRects, r => r.Bounds == winB.Bounds && Math.Abs(r.Alpha - 0.80f) < 0.01f);
    }

    [Fact]
    public void TaskbarAndStaticZone_Use10sHoverTimer_EvenOverFocusedWindow()
    {
        var engine = new DimStateEngine();
        var zoneId = Guid.NewGuid();
        var cfg = DefaultCfg with
        {
            Zones = [new ZoneDefinition(zoneId, "Sidebar", "DISPLAY1", new NormalizedRect(0.0, 0.0, 0.2, 1.0))]
        };
        var focusedFull = new WindowSnapshot(1, "code.exe", "Chrome", new IntRect(0, 0, 1920, 1040), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var taskbar = new WindowSnapshot(2, "explorer.exe", "Shell_TrayWnd", new IntRect(0, 1040, 1920, 1080), IsFocused: false, IsTaskbar: true, IsDesktop: false, IsExcluded: false);

        // Cursor in main editor area (x=600, y=500), outside sidebar zone & taskbar
        engine.UpdateScene([Mon1], [taskbar, focusedFull], new IntPoint(600, 500), nowMs: 0, cfg);
        Assert.Equal(10_000, engine.NextDeadlineMs(0));

        var rects = engine.ComputeDrawRectsForMonitor("DISPLAY1", 10_500);
        // Zone (0..384, 0..1080) and Taskbar (0..1040..1080) should both be dimmed at 0.85
        Assert.Contains(rects, r => r.Bounds.Left == 0 && r.Bounds.Right == 384 && Math.Abs(r.Alpha - 0.85f) < 0.01f);
        Assert.Contains(rects, r => r.Bounds.Top == 1040 && r.Bounds.Bottom == 1080 && Math.Abs(r.Alpha - 0.85f) < 0.01f);
        // Editor area (x >= 384, y < 1040) must remain un-dimmed
        Assert.DoesNotContain(rects, r => r.Bounds.Intersects(new IntRect(384, 0, 1920, 1040)));
    }

    [Fact]
    public void FullscreenMonitor_OrPausedEngine_ClearsAllDrawRects()
    {
        var engine = new DimStateEngine();
        var fullMon = Mon1 with { HasFullscreenApp = true };
        var winA = new WindowSnapshot(1, "game.exe", "Game", new IntRect(0, 0, 1920, 1080), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        engine.UpdateScene([fullMon], [winA], new IntPoint(500, 500), nowMs: 0, DefaultCfg);
        Assert.Empty(engine.ComputeDrawRectsForMonitor("DISPLAY1", 30_000));

        engine.UpdateScene([Mon1], [winA], new IntPoint(500, 500), nowMs: 0, DefaultCfg);
        engine.IsPaused = true;
        Assert.Empty(engine.ComputeDrawRectsForMonitor("DISPLAY1", 30_000));
    }

    [Fact]
    public void ExcludedDesktopWindow_DoesNotDimDesktop()
    {
        var engine = new DimStateEngine();
        var desktop = new WindowSnapshot(10, "explorer.exe", "Progman", new IntRect(0, 0, 1920, 1080), IsFocused: false, IsTaskbar: false, IsDesktop: true, IsExcluded: true);
        var cfg = DefaultCfg with { DimDesktop = true, DesktopOpacity = 0.85 };

        engine.UpdateScene([Mon1], [desktop], new IntPoint(500, 500), nowMs: 0, cfg);

        Assert.Empty(engine.ComputeDrawRectsForMonitor("DISPLAY1", 1_000));
    }

    [Fact]
    public void ExcludedBackgroundWindow_OccludedByUnfocusedForegroundWindow_DoesNotPunchCutout()
    {
        var engine = new DimStateEngine();
        var winFocused = new WindowSnapshot(1, "appA.exe", "ClassA", new IntRect(0, 0, 400, 400), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var winUnfocused = new WindowSnapshot(2, "notepad.exe", "Notepad", new IntRect(500, 0, 1000, 500), IsFocused: false, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var winExcluded = new WindowSnapshot(3, "vlc.exe", "VLC", new IntRect(600, 100, 900, 400), IsFocused: false, IsTaskbar: false, IsDesktop: false, IsExcluded: true);
        var cfg = DefaultCfg with { DimDesktop = false };

        engine.UpdateScene([Mon1], [winFocused, winUnfocused, winExcluded], new IntPoint(100, 100), nowMs: 0, cfg);

        var rects = engine.ComputeDrawRectsForMonitor("DISPLAY1", 20_500);
        Assert.NotEmpty(rects);
        Assert.All(rects, r => Assert.True(Math.Abs(r.Alpha - 0.80f) < 0.01f));
        Assert.Equal(winUnfocused.Bounds.Width * winUnfocused.Bounds.Height, rects.Sum(r => r.Bounds.Width * r.Bounds.Height));
        Assert.Contains(rects, r => r.Bounds.Contains(new IntPoint(750, 250)));
    }

    [Fact]
    public void DuplicateMonitorDeviceId_DoesNotThrowInUpdateScene()
    {
        var engine = new DimStateEngine();
        var dupMon = Mon1 with { DeviceId = "display1" };
        var winA = new WindowSnapshot(1, "appA.exe", "ClassA", new IntRect(0, 0, 800, 600), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var cfg = DefaultCfg with { DimDesktop = false };

        engine.UpdateScene([Mon1, dupMon], [winA], new IntPoint(100, 100), nowMs: 0, cfg);

        Assert.Empty(engine.ComputeDrawRectsForMonitor("DISPLAY1", 0));
    }

    [Fact]
    public void AllMonitorsFullscreenPaused_SuppressesActiveAnimationsAndDeadlines()
    {
        var engine = new DimStateEngine();
        var fullMon = Mon1 with { HasFullscreenApp = true };
        var winFocused = new WindowSnapshot(1, "game.exe", "Game", new IntRect(0, 0, 1920, 1080), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var winUnfocused = new WindowSnapshot(2, "notepad.exe", "Notepad", new IntRect(100, 100, 500, 500), IsFocused: false, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        engine.UpdateScene([fullMon], [winFocused, winUnfocused], new IntPoint(500, 500), nowMs: 0, DefaultCfg);

        Assert.Null(engine.NextDeadlineMs(0));
        Assert.False(engine.HasActiveAnimations(20_100));
    }

    [Fact]
    public void FrameInsetPx_InsetsFloatingEdgesWithoutPullingMonitorFlushEdges()
    {
        var engine = new DimStateEngine();
        var cfg = DefaultCfg with { FrameInsetPx = 4 };
        // Left=0 is flush with Mon1.Left (0); Top/Right/Bottom are floating inside Mon1
        var snappedLeftWin = new WindowSnapshot(1, "app.exe", "Class", new IntRect(0, 100, 1000, 900), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        engine.UpdateScene([Mon1], [snappedLeftWin], new IntPoint(500, 500), nowMs: 0, cfg);
        var rects = engine.ComputeDrawRectsForMonitor("DISPLAY1", 0);

        Assert.DoesNotContain(rects, r => r.Bounds.Intersects(new IntRect(0, 104, 996, 896)));
        Assert.Contains(rects, r => r.Bounds.Intersects(new IntRect(0, 100, 1000, 104)));
        Assert.Contains(rects, r => r.Bounds.Intersects(new IntRect(996, 100, 1000, 900)));
    }

    [Fact]
    public void FloatingFocusedWindow_GeneratesRoundedCornerSpandrelsInsideMonitorBounds()
    {
        var engine = new DimStateEngine();
        var mon150 = Mon1 with { DpiScale = 1.5 };
        var cfg = DefaultCfg with { CornerRadiusDip = 8, FrameInsetPx = 0 };
        var floating = new WindowSnapshot(1, "app.exe", "Class", new IntRect(200, 200, 1000, 800), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        engine.UpdateScene([mon150], [floating], new IntPoint(500, 500), nowMs: 0, cfg);
        var plan = engine.ComputeDrawPlanForMonitor("DISPLAY1", 0);

        Assert.Equal(4, plan.Corners.Count);
        Assert.All(plan.Corners, c =>
        {
            Assert.Equal(12f, c.Radius);
            Assert.Equal(0.85f, c.Alpha, precision: 3);
        });
    }

    [Fact]
    public void FadingWindow_PreservesRoundedCornerSpandrelsWithoutPop()
    {
        var engine = new DimStateEngine();
        var cfg = DefaultCfg with { CornerRadiusDip = 8, FrameInsetPx = 0, UnfocusDelayMs = 0, FadeOutMs = 1000 };
        var winA = new WindowSnapshot(1, "a.exe", "A", new IntRect(0, 0, 100, 100), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var winB = new WindowSnapshot(2, "b.exe", "B", new IntRect(200, 200, 1000, 800), IsFocused: false, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(50, 50), nowMs: 0, cfg);
        // Mid-fade at t=500ms: winB's 4 corners should still be emitted with compensating alpha
        var plan = engine.ComputeDrawPlanForMonitor("DISPLAY1", 500);
        Assert.Equal(4, plan.Corners.Count(c => c.CornerBounds.Left >= 200));
        Assert.All(plan.Corners, c => Assert.True(c.Alpha > 0f));
    }

    [Fact]
    public void DesktopWindow_BrightensOnHover_AndDims10sAfterExit()
    {
        var engine = new DimStateEngine();
        var winA = new WindowSnapshot(1, "app.exe", "App", new IntRect(0, 0, 800, 600), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var desktop = new WindowSnapshot(2, "explorer.exe", "Progman", new IntRect(0, 0, 1920, 1080), IsFocused: false, IsTaskbar: false, IsDesktop: true, IsExcluded: false);

        // Cursor on empty desktop (outside winA) -> desktop stays bright
        engine.UpdateScene([Mon1], [winA, desktop], new IntPoint(1200, 500), nowMs: 0, DefaultCfg);
        Assert.Empty(engine.ComputeDrawRectsForMonitor("DISPLAY1", 15_000));

        // Cursor moves back inside winA at t=15_000 -> 10s hover-exit timer starts for desktop
        engine.UpdateScene([Mon1], [winA, desktop], new IntPoint(200, 200), nowMs: 15_000, DefaultCfg);
        Assert.Equal(25_000, engine.NextDeadlineMs(15_000));
        Assert.Empty(engine.ComputeDrawRectsForMonitor("DISPLAY1", 24_999));

        var dimmed = engine.ComputeDrawRectsForMonitor("DISPLAY1", 25_500);
        Assert.Contains(dimmed, r => Math.Abs(r.Alpha - 0.85f) < 0.01f);
    }

    [Fact]
    public void ProcessOpacityMultiplier_DimsMatchingProcessAtHalfOpacity()
    {
        var engine = new DimStateEngine();
        var winA = new WindowSnapshot(1, "app.exe", "App", new IntRect(0, 0, 500, 500), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var vvidget = new WindowSnapshot(2, "vvidget.exe", "Tauri Window", new IntRect(600, 100, 1200, 700), IsFocused: false, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        engine.UpdateScene([Mon1], [winA, vvidget], new IntPoint(100, 100), nowMs: 0, DefaultCfg);
        var rects = engine.ComputeDrawRectsForMonitor("DISPLAY1", 20_500);

        var vvidgetRects = rects.Where(r => r.Bounds.Intersects(vvidget.Bounds)).ToArray();
        Assert.NotEmpty(vvidgetRects);
        Assert.All(vvidgetRects, r => Assert.Equal(0.52f, r.Alpha, precision: 2));
    }

    [Fact]
    public void BriefHoverUnder750ms_DoesNotBrighten_SustainedHoverBrightensAt750ms()
    {
        var engine = new DimStateEngine();
        var winA = new WindowSnapshot(1, "appA.exe", "A", new IntRect(0, 0, 800, 600), IsFocused: true, IsTaskbar: false, IsDesktop: false, IsExcluded: false);
        var winB = new WindowSnapshot(2, "appB.exe", "B", new IntRect(900, 0, 1800, 600), IsFocused: false, IsTaskbar: false, IsDesktop: false, IsExcluded: false);

        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(100, 100), nowMs: 0, DefaultCfg);
        Assert.Contains(engine.ComputeDrawRectsForMonitor("DISPLAY1", 20_500), r => r.Bounds == winB.Bounds && r.Alpha > 0.7f);

        // Mouse enters winB at t=25_000 -> still dimmed at t=25_100 (< 150ms)
        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(1000, 100), nowMs: 25_000, DefaultCfg);
        Assert.Equal(25_150, engine.NextDeadlineMs(25_000));
        Assert.Contains(engine.ComputeDrawRectsForMonitor("DISPLAY1", 25_100), r => r.Bounds == winB.Bounds && r.Alpha > 0.7f);

        // Mouse leaves winB before 150ms (t=25_120) -> stays dimmed
        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(100, 100), nowMs: 25_120, DefaultCfg);
        Assert.Contains(engine.ComputeDrawRectsForMonitor("DISPLAY1", 26_000), r => r.Bounds == winB.Bounds && r.Alpha > 0.7f);

        // Sustained hover at t=30_000 -> brightens after 150ms + 100ms FadeIn
        engine.UpdateScene([Mon1], [winA, winB], new IntPoint(1000, 100), nowMs: 30_000, DefaultCfg);
        Assert.DoesNotContain(engine.ComputeDrawRectsForMonitor("DISPLAY1", 30_300), r => r.Bounds.Intersects(winB.Bounds));
    }
}
