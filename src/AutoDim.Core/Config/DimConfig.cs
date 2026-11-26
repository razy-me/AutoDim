namespace AutoDim.Core.Config;

public sealed record DimConfig : IEquatable<DimConfig>
{
    public const int CurrentSchemaVersion = 1;
    public const double MaxOpacity = 0.98;
    public const int MaxDelayMs = 3_600_000;
    public const int MaxFadeMs = 10_000;
    public const int MaxFrameInsetPx = 20;
    public const int MaxCornerRadiusDip = 24;

    public static DimConfig Default { get; } = new();

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public double InactiveWindowOpacity { get; init; } = 0.80;
    public double TaskbarOpacity { get; init; } = 0.85;
    public double ZoneOpacity { get; init; } = 0.85;
    public double DesktopOpacity { get; init; } = 0.85;
    public int HoverEnterDelayMs { get; init; } = 150;
    public int HoverExitDelayMs { get; init; } = 10_000;
    public int UnfocusDelayMs { get; init; } = 20_000;
    public int FadeOutMs { get; init; } = 450;
    public int FadeInMs { get; init; } = 140;
    public int FrameInsetPx { get; init; } = 3;
    public int CornerRadiusDip { get; init; } = 8;
    public uint OverlayColorRgb { get; init; } = 0x000000;
    public bool DimDesktop { get; init; } = true;
    public bool DimTaskbar { get; init; } = true;
    public bool PauseOnFullscreen { get; init; } = true;
    public bool ExcludeFromCapture { get; init; } = true;
    public bool StartWithWindows { get; init; } = false;
    public string ToggleHotkey { get; init; } = "Ctrl+Alt+D";
    public IReadOnlyList<string> ExcludedProcesses { get; init; } = [];
    public IReadOnlyDictionary<string, double> ProcessOpacityMultipliers { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["vvidget.exe"] = 0.65 };
    public IReadOnlyList<ZoneDefinition> Zones { get; init; } = [];

    public DimConfig Validated()
    {
        var excluded = (ExcludedProcesses ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var seenIds = new HashSet<Guid>();
        var zones = new List<ZoneDefinition>();
        if (Zones is not null)
        {
            foreach (var raw in Zones)
            {
                if (raw is null) continue;
                var validated = raw.Validated();
                if (seenIds.Add(validated.Id))
                {
                    zones.Add(validated);
                }
            }
        }

        var multipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (ProcessOpacityMultipliers is not null)
        {
            foreach (var kvp in ProcessOpacityMultipliers)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                multipliers[kvp.Key.Trim()] = NormalizedRect.ClampFinite(kvp.Value, 1.0, 0.0, 1.0);
            }
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            InactiveWindowOpacity = NormalizedRect.ClampFinite(InactiveWindowOpacity, 0.80, 0.0, MaxOpacity),
            TaskbarOpacity = NormalizedRect.ClampFinite(TaskbarOpacity, 0.85, 0.0, MaxOpacity),
            ZoneOpacity = NormalizedRect.ClampFinite(ZoneOpacity, 0.85, 0.0, MaxOpacity),
            DesktopOpacity = NormalizedRect.ClampFinite(DesktopOpacity, 0.85, 0.0, MaxOpacity),
            HoverEnterDelayMs = Math.Clamp(HoverEnterDelayMs, 0, MaxFadeMs),
            HoverExitDelayMs = Math.Clamp(HoverExitDelayMs, 0, MaxDelayMs),
            UnfocusDelayMs = Math.Clamp(UnfocusDelayMs, 0, MaxDelayMs),
            FadeOutMs = Math.Clamp(FadeOutMs, 0, MaxFadeMs),
            FadeInMs = Math.Clamp(FadeInMs, 0, MaxFadeMs),
            FrameInsetPx = Math.Clamp(FrameInsetPx, 0, MaxFrameInsetPx),
            CornerRadiusDip = Math.Clamp(CornerRadiusDip, 0, MaxCornerRadiusDip),
            OverlayColorRgb = OverlayColorRgb & 0x00FFFFFFu,
            ToggleHotkey = string.IsNullOrWhiteSpace(ToggleHotkey) ? "Ctrl+Alt+D" : ToggleHotkey.Trim(),
            ExcludedProcesses = excluded,
            ProcessOpacityMultipliers = multipliers,
            Zones = zones
        };
    }

    public bool Equals(DimConfig? other) =>
        other is not null &&
        SchemaVersion == other.SchemaVersion &&
        InactiveWindowOpacity == other.InactiveWindowOpacity &&
        TaskbarOpacity == other.TaskbarOpacity &&
        ZoneOpacity == other.ZoneOpacity &&
        DesktopOpacity == other.DesktopOpacity &&
        HoverEnterDelayMs == other.HoverEnterDelayMs &&
        HoverExitDelayMs == other.HoverExitDelayMs &&
        UnfocusDelayMs == other.UnfocusDelayMs &&
        FadeOutMs == other.FadeOutMs &&
        FadeInMs == other.FadeInMs &&
        FrameInsetPx == other.FrameInsetPx &&
        CornerRadiusDip == other.CornerRadiusDip &&
        OverlayColorRgb == other.OverlayColorRgb &&
        DimDesktop == other.DimDesktop &&
        DimTaskbar == other.DimTaskbar &&
        PauseOnFullscreen == other.PauseOnFullscreen &&
        ExcludeFromCapture == other.ExcludeFromCapture &&
        StartWithWindows == other.StartWithWindows &&
        ToggleHotkey == other.ToggleHotkey &&
        (ExcludedProcesses ?? []).SequenceEqual(other.ExcludedProcesses ?? [], StringComparer.OrdinalIgnoreCase) &&
        MultipliersEqual(ProcessOpacityMultipliers, other.ProcessOpacityMultipliers) &&
        (Zones ?? []).SequenceEqual(other.Zones ?? []);

    private static bool MultipliersEqual(IReadOnlyDictionary<string, double>? a, IReadOnlyDictionary<string, double>? b)
    {
        int countA = a?.Count ?? 0;
        int countB = b?.Count ?? 0;
        if (countA != countB) return false;
        if (countA == 0) return true;
        foreach (var kvp in a!)
        {
            if (!b!.TryGetValue(kvp.Key, out double otherVal) || !kvp.Value.Equals(otherVal)) return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(SchemaVersion);
        hc.Add(InactiveWindowOpacity);
        hc.Add(TaskbarOpacity);
        hc.Add(ZoneOpacity);
        hc.Add(DesktopOpacity);
        hc.Add(HoverEnterDelayMs);
        hc.Add(HoverExitDelayMs);
        hc.Add(UnfocusDelayMs);
        hc.Add(FadeOutMs);
        hc.Add(FadeInMs);
        hc.Add(FrameInsetPx);
        hc.Add(CornerRadiusDip);
        hc.Add(OverlayColorRgb);
        hc.Add(ToggleHotkey);
        hc.Add(ExcludedProcesses?.Count ?? 0);
        hc.Add(Zones?.Count ?? 0);
        return hc.ToHashCode();
    }
}
