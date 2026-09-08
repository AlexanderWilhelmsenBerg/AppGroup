using AppGroup.Geometry;

namespace AppGroup.Tests;

public sealed class PopupGeometryServiceTests
{
    private readonly PopupGeometryService _service = new();

    [Fact]
    public void BottomTaskbar_1920By1080At100Percent_PlacesPopupAboveTaskbar()
    {
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                new GeometryRect(0, 0, 1920, 1080),
                new GeometryRect(0, 0, 1920, 1040),
                96),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 1040, 1920, 1080),
                false),
            cursor: new GeometryPoint(960, 1060),
            desiredSizeDip: new GeometrySize(400, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(760, placement.X);
        Assert.Equal(732, placement.Y);
        Assert.Equal(400, placement.Width);
        Assert.Equal(300, placement.Height);
        Assert.Equal(PopupOpenDirection.Up, placement.OpenDirection);
        AssertWithin(new GeometryRect(0, 0, 1920, 1040), placement);
    }

    [Fact]
    public void BottomTaskbar_2560By1440At125Percent_UsesMonitorDpi()
    {
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                new GeometryRect(0, 0, 2560, 1440),
                new GeometryRect(0, 0, 2560, 1392),
                120),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 1392, 2560, 1440),
                false),
            cursor: new GeometryPoint(1280, 1410),
            desiredSizeDip: new GeometrySize(400, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(500, placement.Width);
        Assert.Equal(375, placement.Height);
        Assert.Equal(1030, placement.X);
        Assert.Equal(1007, placement.Y);
        Assert.Equal(120u, placement.MonitorDpi);
    }

    [Theory]
    [InlineData(144u, 600, 450)]
    [InlineData(192u, 800, 600)]
    public void HighDpi4KDisplay_ScalesDesiredSizeUsingSelectedMonitor(uint dpi, int expectedWidth, int expectedHeight)
    {
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY-4K",
                new GeometryRect(0, 0, 3840, 2160),
                new GeometryRect(0, 0, 3840, 2100),
                dpi),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 2100, 3840, 2160),
                false),
            cursor: new GeometryPoint(1920, 2130),
            desiredSizeDip: new GeometrySize(400, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(expectedWidth, placement.Width);
        Assert.Equal(expectedHeight, placement.Height);
        Assert.Equal(dpi, placement.MonitorDpi);
        AssertWithin(new GeometryRect(0, 0, 3840, 2100), placement);
    }

    [Fact]
    public void TriggerRect_IsPreferredOverCursorForTaskbarAnchor()
    {
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                new GeometryRect(0, 0, 1920, 1080),
                new GeometryRect(0, 0, 1920, 1040),
                96),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 1040, 1920, 1080),
                false),
            cursor: new GeometryPoint(1600, 1060),
            triggerRect: new GeometryRect(300, 1040, 380, 1080),
            desiredSizeDip: new GeometrySize(400, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.True(placement.UsedTriggerRect);
        Assert.Equal(140, placement.X);
        Assert.Equal(732, placement.Y);
    }

    [Fact]
    public void Cursor_IsSafeFallbackWhenTriggerRectIsUnavailable()
    {
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                new GeometryRect(0, 0, 1920, 1080),
                new GeometryRect(0, 0, 1920, 1040),
                96),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 1040, 1920, 1080),
                false),
            cursor: new GeometryPoint(1600, 1060),
            desiredSizeDip: new GeometrySize(400, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.False(placement.UsedTriggerRect);
        Assert.Equal(1400, placement.X);
        Assert.Equal(732, placement.Y);
    }

    [Fact]
    public void MonitorLeftOfPrimary_PreservesNegativeXCoordinates()
    {
        GeometryRect workArea = new(-1920, 0, 0, 1040);
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY-LEFT",
                new GeometryRect(-1920, 0, 0, 1080),
                workArea,
                96),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(-1920, 1040, 0, 1080),
                false),
            cursor: new GeometryPoint(-960, 1060),
            desiredSizeDip: new GeometrySize(400, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal("DISPLAY-LEFT", placement.MonitorId);
        Assert.Equal(-1160, placement.X);
        Assert.True(placement.Bounds.Right <= 0);
        AssertWithin(workArea, placement);
    }

    [Fact]
    public void MonitorAbovePrimary_PreservesNegativeYCoordinatesAt125Percent()
    {
        GeometryRect workArea = new(0, -1440, 2560, -48);
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY-ABOVE",
                new GeometryRect(0, -1440, 2560, 0),
                workArea,
                120),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, -48, 2560, 0),
                false),
            cursor: new GeometryPoint(1280, -20),
            desiredSizeDip: new GeometrySize(400, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(1030, placement.X);
        Assert.Equal(-433, placement.Y);
        Assert.True(placement.Y < 0);
        Assert.True(placement.Bounds.Bottom <= -48);
        AssertWithin(workArea, placement);
    }

    [Fact]
    public void MixedDpiMonitors_UseEachSelectedMonitorsOwnScale()
    {
        PopupGeometryInput primaryInput = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                new GeometryRect(0, 0, 1920, 1080),
                new GeometryRect(0, 0, 1920, 1040),
                96),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 1040, 1920, 1080),
                false),
            cursor: new GeometryPoint(960, 1060),
            desiredSizeDip: new GeometrySize(300, 200));

        PopupGeometryInput secondaryInput = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY2",
                new GeometryRect(1920, 0, 5760, 2160),
                new GeometryRect(1920, 0, 5760, 2100),
                144),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(1920, 2100, 5760, 2160),
                false),
            cursor: new GeometryPoint(3840, 2130),
            desiredSizeDip: new GeometrySize(300, 200));

        PopupPlacement primaryPlacement = _service.CalculatePlacement(primaryInput);
        PopupPlacement secondaryPlacement = _service.CalculatePlacement(secondaryInput);

        Assert.Equal(300, primaryPlacement.Width);
        Assert.Equal(200, primaryPlacement.Height);
        Assert.Equal(450, secondaryPlacement.Width);
        Assert.Equal(300, secondaryPlacement.Height);
        Assert.Equal("DISPLAY1", primaryPlacement.MonitorId);
        Assert.Equal("DISPLAY2", secondaryPlacement.MonitorId);
        Assert.True(secondaryPlacement.X >= 1920);
    }

    [Fact]
    public void TaskbarOnNonPrimaryDisplay_StaysOnThatDisplay()
    {
        GeometryRect workArea = new(1920, 0, 5760, 2100);
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY2",
                new GeometryRect(1920, 0, 5760, 2160),
                workArea,
                144),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(1920, 2100, 5760, 2160),
                false),
            cursor: new GeometryPoint(5200, 2130),
            triggerRect: new GeometryRect(3150, 2100, 3210, 2160),
            desiredSizeDip: new GeometrySize(300, 240));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal("DISPLAY2", placement.MonitorId);
        Assert.Equal(2955, placement.X);
        Assert.Equal(1728, placement.Y);
        AssertWithin(workArea, placement);
    }

    [Theory]
    [InlineData(TaskbarEdge.Top, PopupOpenDirection.Down, 48)]
    [InlineData(TaskbarEdge.Bottom, PopupOpenDirection.Up, 832)]
    [InlineData(TaskbarEdge.Left, PopupOpenDirection.Right, 48)]
    [InlineData(TaskbarEdge.Right, PopupOpenDirection.Left, 1572)]
    public void TaskbarEdges_OpenInwardAndRespectWorkArea(
        TaskbarEdge edge,
        PopupOpenDirection expectedDirection,
        int expectedPerpendicularCoordinate)
    {
        (GeometryRect workArea, GeometryRect taskbarBounds, GeometryPoint cursor) = edge switch
        {
            TaskbarEdge.Top => (
                new GeometryRect(0, 40, 1920, 1080),
                new GeometryRect(0, 0, 1920, 40),
                new GeometryPoint(960, 20)),
            TaskbarEdge.Bottom => (
                new GeometryRect(0, 0, 1920, 1040),
                new GeometryRect(0, 1040, 1920, 1080),
                new GeometryPoint(960, 1060)),
            TaskbarEdge.Left => (
                new GeometryRect(40, 0, 1920, 1080),
                new GeometryRect(0, 0, 40, 1080),
                new GeometryPoint(20, 540)),
            TaskbarEdge.Right => (
                new GeometryRect(0, 0, 1880, 1080),
                new GeometryRect(1880, 0, 1920, 1080),
                new GeometryPoint(1900, 540)),
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };

        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                new GeometryRect(0, 0, 1920, 1080),
                workArea,
                96),
            taskbar: new TaskbarGeometry(edge, taskbarBounds, false),
            cursor: cursor,
            desiredSizeDip: new GeometrySize(300, 200));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(expectedDirection, placement.OpenDirection);
        if (edge is TaskbarEdge.Top or TaskbarEdge.Bottom)
        {
            Assert.Equal(expectedPerpendicularCoordinate, placement.Y);
        }
        else
        {
            Assert.Equal(expectedPerpendicularCoordinate, placement.X);
        }

        AssertWithin(workArea, placement);
    }

    [Fact]
    public void Autohide_ReservesTaskbarBoundsWhenWorkAreaEqualsMonitorBounds()
    {
        GeometryRect monitorBounds = new(0, 0, 1920, 1080);
        GeometryRect taskbarBounds = new(0, 1040, 1920, 1080);
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                monitorBounds,
                monitorBounds,
                96),
            taskbar: new TaskbarGeometry(TaskbarEdge.Bottom, taskbarBounds, true),
            cursor: new GeometryPoint(960, 1078),
            desiredSizeDip: new GeometrySize(400, 200));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(832, placement.Y);
        Assert.True(placement.Bounds.Bottom < taskbarBounds.Top);
        Assert.True(placement.Bounds.Right <= monitorBounds.Right);
    }

    [Fact]
    public void NearEdgeTrigger_IsClampedInsideSelectedMonitorWorkArea()
    {
        GeometryRect workArea = new(0, 0, 1920, 1040);
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                new GeometryRect(0, 0, 1920, 1080),
                workArea,
                96),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 1040, 1920, 1080),
                false),
            cursor: new GeometryPoint(1500, 1060),
            triggerRect: new GeometryRect(0, 1040, 32, 1080),
            desiredSizeDip: new GeometrySize(400, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(0, placement.X);
        AssertWithin(workArea, placement);
    }

    [Fact]
    public void OversizedPopup_IsReducedInsteadOfCrossingMonitorBoundary()
    {
        GeometryRect workArea = new(0, 0, 800, 560);
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "SMALL-DISPLAY",
                new GeometryRect(0, 0, 800, 600),
                workArea,
                96),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 560, 800, 600),
                false),
            cursor: new GeometryPoint(400, 580),
            desiredSizeDip: new GeometrySize(1200, 900));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(800, placement.Width);
        Assert.Equal(560, placement.Height);
        Assert.Equal(0, placement.X);
        Assert.Equal(0, placement.Y);
        AssertWithin(workArea, placement);
    }

    [Fact]
    public void AutohideLeftTaskbar_OnNegativeCoordinateMonitor_DoesNotOverlapTaskbar()
    {
        GeometryRect monitorBounds = new(-1200, 0, 0, 1920);
        GeometryRect taskbarBounds = new(-1200, 0, -1150, 1920);
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY-LEFT-VERTICAL",
                monitorBounds,
                monitorBounds,
                96),
            taskbar: new TaskbarGeometry(TaskbarEdge.Left, taskbarBounds, true),
            cursor: new GeometryPoint(-1198, 960),
            desiredSizeDip: new GeometrySize(300, 300));

        PopupPlacement placement = _service.CalculatePlacement(input);

        Assert.Equal(PopupOpenDirection.Right, placement.OpenDirection);
        Assert.True(placement.X > taskbarBounds.Right);
        Assert.True(placement.Bounds.Right <= monitorBounds.Right);
        Assert.True(placement.X < 0);
    }

    [Fact]
    public void InvalidMonitorDpi_IsRejected()
    {
        PopupGeometryInput input = CreateInput(
            monitor: new MonitorGeometry(
                "DISPLAY1",
                new GeometryRect(0, 0, 1920, 1080),
                new GeometryRect(0, 0, 1920, 1040),
                0),
            taskbar: new TaskbarGeometry(
                TaskbarEdge.Bottom,
                new GeometryRect(0, 1040, 1920, 1080),
                false),
            cursor: new GeometryPoint(960, 1060),
            desiredSizeDip: new GeometrySize(400, 300));

        Assert.Throws<ArgumentException>(() => _service.CalculatePlacement(input));
    }

    private static PopupGeometryInput CreateInput(
        MonitorGeometry monitor,
        TaskbarGeometry taskbar,
        GeometryPoint cursor,
        GeometrySize desiredSizeDip,
        GeometryRect? triggerRect = null)
    {
        return new PopupGeometryInput(
            triggerRect,
            cursor,
            monitor,
            taskbar,
            desiredSizeDip);
    }

    private static void AssertWithin(GeometryRect boundary, PopupPlacement placement)
    {
        Assert.True(placement.X >= boundary.Left, $"Expected X >= {boundary.Left}, got {placement.X}.");
        Assert.True(placement.Y >= boundary.Top, $"Expected Y >= {boundary.Top}, got {placement.Y}.");
        Assert.True(placement.Bounds.Right <= boundary.Right, $"Expected right <= {boundary.Right}, got {placement.Bounds.Right}.");
        Assert.True(placement.Bounds.Bottom <= boundary.Bottom, $"Expected bottom <= {boundary.Bottom}, got {placement.Bounds.Bottom}.");
    }
}
