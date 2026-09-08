namespace AppGroup.Geometry;

public sealed record TaskbarGeometry(
    TaskbarEdge Edge,
    GeometryRect Bounds,
    bool Autohide);
