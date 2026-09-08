using System;

namespace AppGroup.Geometry;

public sealed class PopupGeometryService
{
    private const double StandardDpi = 96.0;
    private const double PopupGapDip = 8.0;

    public PopupPlacement CalculatePlacement(PopupGeometryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        GeometryRect monitorBounds = input.Monitor.Bounds;
        GeometryRect workArea = monitorBounds.Intersect(input.Monitor.WorkArea);
        if (workArea.IsEmpty)
        {
            workArea = monitorBounds;
        }

        GeometryRect placementArea = GetPlacementArea(workArea, monitorBounds, input.Taskbar);

        int desiredWidth = ScaleDipToPixels(input.PopupDesiredSizeDip.Width, input.Monitor.Dpi);
        int desiredHeight = ScaleDipToPixels(input.PopupDesiredSizeDip.Height, input.Monitor.Dpi);
        int width = Math.Min(desiredWidth, placementArea.Width);
        int height = Math.Min(desiredHeight, placementArea.Height);
        int gap = ScaleDipToPixels(PopupGapDip, input.Monitor.Dpi);

        bool useTriggerRect = input.TriggerRect is GeometryRect triggerRect && !triggerRect.IsEmpty;
        int anchorX = useTriggerRect ? input.TriggerRect!.Value.CenterX : input.CursorPosition.X;
        int anchorY = useTriggerRect ? input.TriggerRect!.Value.CenterY : input.CursorPosition.Y;

        int x;
        int y;
        PopupOpenDirection openDirection;

        switch (input.Taskbar.Edge)
        {
            case TaskbarEdge.Top:
                x = anchorX - (width / 2);
                y = placementArea.Top + gap;
                openDirection = PopupOpenDirection.Down;
                break;
            case TaskbarEdge.Bottom:
                x = anchorX - (width / 2);
                y = placementArea.Bottom - height - gap;
                openDirection = PopupOpenDirection.Up;
                break;
            case TaskbarEdge.Left:
                x = placementArea.Left + gap;
                y = anchorY - (height / 2);
                openDirection = PopupOpenDirection.Right;
                break;
            case TaskbarEdge.Right:
                x = placementArea.Right - width - gap;
                y = anchorY - (height / 2);
                openDirection = PopupOpenDirection.Left;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Taskbar.Edge, "Unsupported taskbar edge.");
        }

        x = Clamp(x, placementArea.Left, placementArea.Right - width);
        y = Clamp(y, placementArea.Top, placementArea.Bottom - height);

        return new PopupPlacement(
            x,
            y,
            width,
            height,
            openDirection,
            input.Monitor.Id,
            input.Monitor.Dpi,
            useTriggerRect);
    }

    private static GeometryRect GetPlacementArea(
        GeometryRect workArea,
        GeometryRect monitorBounds,
        TaskbarGeometry taskbar)
    {
        GeometryRect taskbarOnMonitor = monitorBounds.Intersect(taskbar.Bounds);
        if (taskbarOnMonitor.IsEmpty || !TouchesExpectedMonitorEdge(taskbarOnMonitor, monitorBounds, taskbar.Edge))
        {
            return workArea;
        }

        bool taskbarOverlapsWorkArea = !workArea.Intersect(taskbarOnMonitor).IsEmpty;
        if (!taskbar.Autohide && !taskbarOverlapsWorkArea)
        {
            return workArea;
        }

        GeometryRect adjusted = taskbar.Edge switch
        {
            TaskbarEdge.Top => new GeometryRect(
                workArea.Left,
                Math.Max(workArea.Top, taskbarOnMonitor.Bottom),
                workArea.Right,
                workArea.Bottom),
            TaskbarEdge.Bottom => new GeometryRect(
                workArea.Left,
                workArea.Top,
                workArea.Right,
                Math.Min(workArea.Bottom, taskbarOnMonitor.Top)),
            TaskbarEdge.Left => new GeometryRect(
                Math.Max(workArea.Left, taskbarOnMonitor.Right),
                workArea.Top,
                workArea.Right,
                workArea.Bottom),
            TaskbarEdge.Right => new GeometryRect(
                workArea.Left,
                workArea.Top,
                Math.Min(workArea.Right, taskbarOnMonitor.Left),
                workArea.Bottom),
            _ => workArea
        };

        return adjusted.IsEmpty ? workArea : adjusted;
    }

    private static bool TouchesExpectedMonitorEdge(
        GeometryRect taskbarBounds,
        GeometryRect monitorBounds,
        TaskbarEdge edge)
    {
        return edge switch
        {
            TaskbarEdge.Top => taskbarBounds.Top == monitorBounds.Top,
            TaskbarEdge.Bottom => taskbarBounds.Bottom == monitorBounds.Bottom,
            TaskbarEdge.Left => taskbarBounds.Left == monitorBounds.Left,
            TaskbarEdge.Right => taskbarBounds.Right == monitorBounds.Right,
            _ => false
        };
    }

    private static int ScaleDipToPixels(double dip, uint dpi)
    {
        double pixels = Math.Ceiling(dip * dpi / StandardDpi);
        if (pixels > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(dip), "Scaled geometry exceeds supported pixel coordinates.");
        }

        return Math.Max(1, (int)pixels);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static void Validate(PopupGeometryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Monitor.Id))
        {
            throw new ArgumentException("Monitor ID must be provided.", nameof(input));
        }

        if (input.Monitor.Bounds.IsEmpty)
        {
            throw new ArgumentException("Monitor bounds must have positive width and height.", nameof(input));
        }

        if (input.Monitor.Dpi == 0)
        {
            throw new ArgumentException("Monitor DPI must be greater than zero.", nameof(input));
        }

        if (input.PopupDesiredSizeDip.Width <= 0 || input.PopupDesiredSizeDip.Height <= 0)
        {
            throw new ArgumentException("Popup desired size must be positive.", nameof(input));
        }
    }
}
