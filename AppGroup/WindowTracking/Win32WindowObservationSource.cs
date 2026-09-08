using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AppGroup.WindowTracking;

internal sealed class Win32WindowObservationSource : IWindowObservationSource {
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint GwOwner = 4;
    private const uint GaRoot = 2;
    private const int DwmwaCloaked = 14;
    private const uint WineventOutOfContext = 0x0000;
    private const int ObjidWindow = 0;

    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectNameChange = 0x800C;

    private const int ErrorInsufficientBuffer = 122;
    private const int AppmodelErrorNoApplication = 15703;

    public IReadOnlyCollection<WindowSnapshot> EnumerateWindows(
        IReadOnlyDictionary<IntPtr, WindowSnapshot> previousWindows) {
        if (!OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("Window tracking requires Windows.");
        }

        List<WindowSnapshot> windows = new();
        GCHandle stateHandle = GCHandle.Alloc(new EnumerationState(this, previousWindows, windows));
        try {
            if (!EnumWindows(EnumWindowCallback, GCHandle.ToIntPtr(stateHandle))) {
                int error = Marshal.GetLastWin32Error();
                if (error != 0) {
                    throw new Win32Exception(error);
                }
            }
        }
        finally {
            stateHandle.Free();
        }

        return windows;
    }

    public WindowSnapshot? TryGetWindow(
        IntPtr windowHandle,
        bool allowHidden,
        WindowSnapshot? previousWindow) {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero || !IsWindow(windowHandle)) {
            return null;
        }

        if (GetAncestor(windowHandle, GaRoot) != windowHandle) {
            return null;
        }

        if (windowHandle == GetDesktopWindow() || windowHandle == GetShellWindow()) {
            return null;
        }

        long extendedStyle = GetExtendedWindowStyle(windowHandle);
        bool forceAppWindow = (extendedStyle & WsExAppWindow) != 0;
        if (!forceAppWindow && ((extendedStyle & WsExToolWindow) != 0 || (extendedStyle & WsExNoActivate) != 0)) {
            return null;
        }

        if (!forceAppWindow && GetWindow(windowHandle, GwOwner) != IntPtr.Zero) {
            return null;
        }

        if (TryGetCloaked(windowHandle, out bool cloaked) && cloaked) {
            return null;
        }

        bool visible = IsWindowVisible(windowHandle);
        if (!visible && !allowHidden) {
            return null;
        }

        bool minimized = IsIconic(windowHandle);
        if (!minimized && GetWindowRect(windowHandle, out Rect rect) &&
            (rect.Right <= rect.Left || rect.Bottom <= rect.Top)) {
            return null;
        }

        uint? processId = TryGetProcessId(windowHandle);
        string? executablePath = null;
        string? appUserModelId = null;

        if (previousWindow is not null && previousWindow.ProcessId == processId) {
            executablePath = previousWindow.ExecutablePath;
            appUserModelId = previousWindow.AppUserModelId;
        }
        else if (processId.HasValue) {
            ResolveProcessIdentity(processId.Value, out executablePath, out appUserModelId);
        }

        return new WindowSnapshot(
            windowHandle,
            processId,
            executablePath,
            appUserModelId,
            TryGetWindowTitle(windowHandle),
            visible,
            minimized,
            GetForegroundWindow() == windowHandle);
    }

    public IDisposable Subscribe(Action<WindowObservationEvent> sink) {
        if (!OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("Window tracking requires Windows.");
        }

        return new WinEventSubscription(sink);
    }

    private static bool EnumWindowCallback(IntPtr windowHandle, IntPtr parameter) {
        GCHandle handle = GCHandle.FromIntPtr(parameter);
        EnumerationState state = (EnumerationState)handle.Target!;
        state.PreviousWindows.TryGetValue(windowHandle, out WindowSnapshot? previous);
        WindowSnapshot? snapshot = state.Source.TryGetWindow(windowHandle, allowHidden: false, previous);
        if (snapshot is not null) {
            state.Windows.Add(snapshot);
        }

        return true;
    }

    private static uint? TryGetProcessId(IntPtr windowHandle) {
        uint threadId = GetWindowThreadProcessId(windowHandle, out uint processId);
        return threadId == 0 || processId == 0 ? null : processId;
    }

    private static string? TryGetWindowTitle(IntPtr windowHandle) {
        Marshal.SetLastPInvokeError(0);
        int length = GetWindowTextLength(windowHandle);
        if (length == 0 && Marshal.GetLastPInvokeError() != 0) {
            return null;
        }

        StringBuilder title = new(Math.Max(length + 1, 2));
        Marshal.SetLastPInvokeError(0);
        int copied = GetWindowText(windowHandle, title, title.Capacity);
        if (copied == 0 && length > 0 && Marshal.GetLastPInvokeError() != 0) {
            return null;
        }

        return title.ToString();
    }

    private static void ResolveProcessIdentity(
        uint processId,
        out string? executablePath,
        out string? appUserModelId) {
        executablePath = null;
        appUserModelId = null;

        IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero) {
            return;
        }

        try {
            executablePath = TryGetExecutablePath(processHandle);
            appUserModelId = TryGetApplicationUserModelId(processHandle);
        }
        finally {
            CloseHandle(processHandle);
        }
    }

    private static string? TryGetExecutablePath(IntPtr processHandle) {
        uint capacity = 32768;
        StringBuilder path = new((int)capacity);
        return QueryFullProcessImageName(processHandle, 0, path, ref capacity)
            ? path.ToString()
            : null;
    }

    private static string? TryGetApplicationUserModelId(IntPtr processHandle) {
        uint length = 0;
        int result = GetApplicationUserModelId(processHandle, ref length, null);
        if (result == AppmodelErrorNoApplication) {
            return null;
        }

        if (result != ErrorInsufficientBuffer || length == 0) {
            return null;
        }

        StringBuilder appId = new((int)length);
        result = GetApplicationUserModelId(processHandle, ref length, appId);
        return result == 0 ? appId.ToString() : null;
    }

    private static long GetExtendedWindowStyle(IntPtr windowHandle) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, GwlExStyle).ToInt64()
            : GetWindowLong32(windowHandle, GwlExStyle);

    private static bool TryGetCloaked(IntPtr windowHandle, out bool cloaked) {
        int value;
        int result = DwmGetWindowAttribute(
            windowHandle,
            DwmwaCloaked,
            out value,
            Marshal.SizeOf<int>());
        cloaked = result == 0 && value != 0;
        return result == 0;
    }

    private sealed record EnumerationState(
        Win32WindowObservationSource Source,
        IReadOnlyDictionary<IntPtr, WindowSnapshot> PreviousWindows,
        List<WindowSnapshot> Windows);

    private sealed class WinEventSubscription : IDisposable {
        private readonly Action<WindowObservationEvent> _sink;
        private readonly WinEventDelegate _callback;
        private readonly List<IntPtr> _hooks = new();
        private int _disposed;

        public WinEventSubscription(Action<WindowObservationEvent> sink) {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _callback = WinEventCallback;

            try {
                AddHook(EventSystemForeground, EventSystemForeground);
                AddHook(EventSystemMinimizeStart, EventSystemMinimizeEnd);
                AddHook(EventObjectCreate, EventObjectHide);
                AddHook(EventObjectNameChange, EventObjectNameChange);
            }
            catch {
                Dispose();
                throw;
            }
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            foreach (IntPtr hook in _hooks) {
                if (hook != IntPtr.Zero) {
                    UnhookWinEvent(hook);
                }
            }
            _hooks.Clear();
        }

        private void AddHook(uint eventMin, uint eventMax) {
            IntPtr hook = SetWinEventHook(
                eventMin,
                eventMax,
                IntPtr.Zero,
                _callback,
                0,
                0,
                WineventOutOfContext);

            if (hook == IntPtr.Zero) {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Unable to register WinEvent hook 0x{eventMin:X}-0x{eventMax:X}.");
            }

            _hooks.Add(hook);
        }

        private void WinEventCallback(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime) {
            _ = hook;
            _ = eventThread;
            _ = eventTime;

            if (Volatile.Read(ref _disposed) != 0 || windowHandle == IntPtr.Zero) {
                return;
            }

            bool objectEvent = eventType >= EventObjectCreate;
            if (objectEvent && (objectId != ObjidWindow || childId != 0)) {
                return;
            }

            WindowObservationEventType? type = eventType switch {
                EventSystemForeground => WindowObservationEventType.ForegroundChanged,
                EventSystemMinimizeStart => WindowObservationEventType.MinimizeStarted,
                EventSystemMinimizeEnd => WindowObservationEventType.MinimizeEnded,
                EventObjectCreate => WindowObservationEventType.Created,
                EventObjectDestroy => WindowObservationEventType.Destroyed,
                EventObjectShow => WindowObservationEventType.Shown,
                EventObjectHide => WindowObservationEventType.Hidden,
                EventObjectNameChange => WindowObservationEventType.NameChanged,
                _ => null
            };

            if (type.HasValue) {
                _sink(new WindowObservationEvent(type.Value, windowHandle));
            }
        }
    }

    private delegate bool EnumWindowsDelegate(IntPtr windowHandle, IntPtr parameter);

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        out int value,
        int valueSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        uint flags,
        StringBuilder executableName,
        ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(
        IntPtr processHandle,
        ref uint applicationUserModelIdLength,
        StringBuilder? applicationUserModelId);
}
