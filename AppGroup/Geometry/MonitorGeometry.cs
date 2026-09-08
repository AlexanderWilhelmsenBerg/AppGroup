namespace AppGroup.Geometry;

public sealed record MonitorGeometry(
    string Id,
    GeometryRect Bounds,
    GeometryRect WorkArea,
    uint Dpi);
