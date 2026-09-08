namespace AppGroup.Geometry;

public enum PopupOpenDirection
{
    Left,
    Up,
    Right,
    Down
}

public sealed record PopupPlacement(
    int X,
    int Y,
    int Width,
    int Height,
    PopupOpenDirection OpenDirection,
    string MonitorId,
    uint MonitorDpi,
    bool UsedTriggerRect)
{
    public GeometryRect Bounds => GeometryRect.FromXYWH(X, Y, Width, Height);
}
