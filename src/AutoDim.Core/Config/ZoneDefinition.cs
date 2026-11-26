namespace AutoDim.Core.Config;

/// <summary>
/// User-defined static UI region (e.g., sidebar or dock) dimmed via presence-based hover tracking.
/// </summary>
public sealed record ZoneDefinition(
    Guid Id,
    string Name,
    string MonitorDeviceId,
    NormalizedRect Bounds,
    string? ProcessNameFilter = null,
    double? OpacityOverride = null,
    bool Enabled = true)
{
    public ZoneDefinition Validated()
    {
        Guid id = Id == Guid.Empty ? Guid.NewGuid() : Id;
        string name = string.IsNullOrWhiteSpace(Name) ? "Zone" : Name.Trim();
        string monitorId = string.IsNullOrWhiteSpace(MonitorDeviceId) ? string.Empty : MonitorDeviceId.Trim();
        string? processFilter = string.IsNullOrWhiteSpace(ProcessNameFilter) ? null : ProcessNameFilter.Trim();
        double? opacity = OpacityOverride.HasValue
            ? NormalizedRect.ClampFinite(OpacityOverride.Value, 0.85, 0.0, DimConfig.MaxOpacity)
            : null;

        return this with
        {
            Id = id,
            Name = name,
            MonitorDeviceId = monitorId,
            Bounds = Bounds.Normalized(),
            ProcessNameFilter = processFilter,
            OpacityOverride = opacity
        };
    }
}
