using AutoDim.Core.Config;
using Xunit;

namespace AutoDim.Core.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public ConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AutoDim_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenMissing_ReturnsDefaults()
    {
        var store = new JsonConfigStore(_configPath);
        DimConfig cfg = store.Load();

        Assert.Equal(0.80, cfg.InactiveWindowOpacity);
        Assert.Equal(0.85, cfg.TaskbarOpacity);
        Assert.Equal(10_000, cfg.HoverExitDelayMs);
        Assert.Equal(20_000, cfg.UnfocusDelayMs);
        Assert.Equal("Ctrl+Alt+D", cfg.ToggleHotkey);
        Assert.Equal(0.65, cfg.ProcessOpacityMultipliers["vvidget.exe"]);
        Assert.True(cfg.PauseOnFullscreen);
        Assert.True(cfg.ExcludeFromCapture);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAllFieldsAndZones()
    {
        var store = new JsonConfigStore(_configPath);
        var zoneId = Guid.NewGuid();
        var original = new DimConfig
        {
            InactiveWindowOpacity = 0.75,
            TaskbarOpacity = 0.90,
            HoverExitDelayMs = 8_000,
            UnfocusDelayMs = 15_000,
            FadeOutMs = 600,
            FadeInMs = 120,
            OverlayColorRgb = 0x050505,
            StartWithWindows = true,
            ToggleHotkey = "Ctrl+Shift+D",
            ExcludedProcesses = ["vlc.exe", "mpv.exe", "VLC.EXE"],
            Zones =
            [
                new ZoneDefinition(
                    zoneId,
                    "  Sidebar  ",
                    "DISPLAY1",
                    new NormalizedRect(0.0, 0.0, 0.15, 1.0),
                    ProcessNameFilter: "code.exe",
                    OpacityOverride: 0.92,
                    Enabled: true)
            ]
        };

        store.Save(original);
        DimConfig loaded = store.Load();

        Assert.Equal(0.75, loaded.InactiveWindowOpacity);
        Assert.Equal(0.90, loaded.TaskbarOpacity);
        Assert.Equal(8_000, loaded.HoverExitDelayMs);
        Assert.Equal(15_000, loaded.UnfocusDelayMs);
        Assert.Equal((uint)0x050505, loaded.OverlayColorRgb);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal("Ctrl+Shift+D", loaded.ToggleHotkey);
        Assert.Equal(2, loaded.ExcludedProcesses.Count);
        Assert.Single(loaded.Zones);
        Assert.Equal(zoneId, loaded.Zones[0].Id);
        Assert.Equal("Sidebar", loaded.Zones[0].Name);
        Assert.Equal(0.92, loaded.Zones[0].OpacityOverride);
    }

    [Fact]
    public void Validated_ClampsOutOfRangeAndNaNValues()
    {
        var invalid = new DimConfig
        {
            InactiveWindowOpacity = double.NaN,
            TaskbarOpacity = 1.5,
            ZoneOpacity = -0.3,
            DesktopOpacity = double.PositiveInfinity,
            HoverExitDelayMs = -500,
            UnfocusDelayMs = 99_999_999,
            FadeOutMs = -10,
            FadeInMs = 50_000,
            OverlayColorRgb = 0xFF112233,
            ToggleHotkey = "   ",
            Zones =
            [
                new ZoneDefinition(Guid.Empty, "  ", "MON1", new NormalizedRect(-0.5, 2.0, 5.0, -1.0), OpacityOverride: 1.0)
            ]
        };

        DimConfig clamped = invalid.Validated();

        Assert.Equal(0.80, clamped.InactiveWindowOpacity);
        Assert.Equal(DimConfig.MaxOpacity, clamped.TaskbarOpacity);
        Assert.Equal(0.0, clamped.ZoneOpacity);
        Assert.Equal(0.85, clamped.DesktopOpacity);
        Assert.Equal(0, clamped.HoverExitDelayMs);
        Assert.Equal(DimConfig.MaxDelayMs, clamped.UnfocusDelayMs);
        Assert.Equal(0, clamped.FadeOutMs);
        Assert.Equal(DimConfig.MaxFadeMs, clamped.FadeInMs);
        Assert.Equal((uint)0x112233, clamped.OverlayColorRgb);
        Assert.Equal("Ctrl+Alt+D", clamped.ToggleHotkey);

        var zone = Assert.Single(clamped.Zones);
        Assert.NotEqual(Guid.Empty, zone.Id);
        Assert.Equal("Zone", zone.Name);
        Assert.InRange(zone.Bounds.Left, 0.0, 1.0);
        Assert.InRange(zone.Bounds.Top, 0.0, 1.0);
        Assert.True(zone.Bounds.Right <= 1.000001);
        Assert.True(zone.Bounds.Bottom <= 1.000001);
        Assert.Equal(DimConfig.MaxOpacity, zone.OpacityOverride);
    }

    [Fact]
    public void Load_WhenJsonCorrupt_BacksUpFileAndReturnsDefaults()
    {
        File.WriteAllText(_configPath, "{ invalid json !!!");
        var store = new JsonConfigStore(_configPath);

        DimConfig cfg = store.Load();

        Assert.Equal(DimConfig.Default, cfg);
        Assert.NotEmpty(Directory.GetFiles(_tempDir, "config.json.corrupt.*.bak"));
    }

    [Fact]
    public void NormalizedRect_NaNWidthNearRightEdge_ClampsToRemainingSpace()
    {
        var rect = new NormalizedRect(0.95, 0.95, double.NaN, double.NaN).Normalized();

        Assert.Equal(0.05, rect.Width, precision: 6);
        Assert.Equal(0.05, rect.Height, precision: 6);
        Assert.True(rect.Right <= 1.000001);
        Assert.True(rect.Bottom <= 1.000001);
    }

    [Fact]
    public void DimConfig_ValueEquality_ComparesCollectionsBySequence()
    {
        var validated = DimConfig.Default.Validated();
        Assert.Equal(DimConfig.Default, validated);

        var zoneId = Guid.NewGuid();
        var a = new DimConfig
        {
            ExcludedProcesses = ["vlc.exe"],
            Zones = [new ZoneDefinition(zoneId, "Side", "D1", new NormalizedRect(0, 0, 0.2, 1))]
        };
        var b = new DimConfig
        {
            ExcludedProcesses = ["VLC.EXE"],
            Zones = [new ZoneDefinition(zoneId, "Side", "D1", new NormalizedRect(0, 0, 0.2, 1))]
        };

        Assert.Equal(a, b);
    }
}
