using System;
using System.Collections.Generic;

namespace AppGroup.WindowTracking;

public sealed record WindowSnapshot(
    IntPtr WindowHandle,
    uint? ProcessId,
    string? ExecutablePath,
    string? AppUserModelId,
    string? Title,
    bool IsVisible,
    bool IsMinimized,
    bool IsForeground);

public sealed class WindowsChangedEventArgs : EventArgs {
    public WindowsChangedEventArgs(IReadOnlyCollection<WindowSnapshot> windows) {
        Windows = windows;
    }

    public IReadOnlyCollection<WindowSnapshot> Windows { get; }
}
