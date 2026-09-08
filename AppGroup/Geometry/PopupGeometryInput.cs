namespace AppGroup.Geometry;

public sealed record PopupGeometryInput(
    GeometryRect? TriggerRect,
    GeometryPoint CursorPosition,
    MonitorGeometry Monitor,
    TaskbarGeometry Taskbar,
    GeometrySize PopupDesiredSizeDip);
