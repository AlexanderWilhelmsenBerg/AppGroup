# Window tracker core

Roadmap PR 7A introduces a read-only `WindowTracker` service. It observes raw top-level application windows only; it does not match windows to AppGroup items, activate windows, or drive popup UI.

## Trackable window policy

Initial enumeration starts from `EnumWindows`. A window is included when it is a real top-level/root window and looks like a user activation target using window semantics rather than a process-name blacklist.

The native source excludes:

- invalid/destroyed handles;
- child/non-root windows;
- the desktop and shell root windows returned by Windows;
- owned windows unless they explicitly opt into `WS_EX_APPWINDOW`;
- `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE` windows unless `WS_EX_APPWINDOW` explicitly marks them as application windows;
- DWM-cloaked windows;
- newly discovered hidden windows;
- non-minimized zero-sized windows.

An already tracked window that receives a hide event is retained temporarily with `IsVisible=false`. The next 30-second reconciliation enumerates only currently relevant visible windows, so permanently hidden or stale windows do not remain forever. A later show event can add the window again.

Empty titles are not used as a filter. A legitimate user-facing window can therefore be tracked with an empty title. Title lookup, PID lookup, executable-path lookup, and AUMID lookup all degrade to nullable metadata when Windows denies access or the process/window disappears during inspection.

## Metadata

`WindowSnapshot` exposes:

- HWND (`WindowHandle`);
- nullable process ID;
- nullable executable path;
- nullable process AppUserModelID/AUMID;
- nullable title;
- visibility;
- minimized state;
- foreground state.

Executable path uses `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` plus `QueryFullProcessImageName`. AUMID uses the documented `GetApplicationUserModelId` process API. When a tracked HWND still belongs to the same PID, title/minimize/show/hide refreshes reuse the previous executable/AUMID values instead of repeating process identity work.

## Event hooks and threading

The native observation source owns a dedicated lightweight background thread with a Win32 message loop. That thread installs four `SetWinEventHook` registrations with `WINEVENT_OUTOFCONTEXT`:

1. `EVENT_SYSTEM_FOREGROUND`;
2. `EVENT_SYSTEM_MINIMIZESTART` through `EVENT_SYSTEM_MINIMIZEEND`;
3. `EVENT_OBJECT_CREATE` through `EVENT_OBJECT_HIDE`;
4. `EVENT_OBJECT_NAMECHANGE`.

Owning the message loop removes any dependency on the caller being a WinUI thread or otherwise pumping Windows messages. The tracker therefore does not require `PopupWindow` or a UI dispatcher to receive native events.

Object events are accepted only for `OBJID_WINDOW` with `idChild == 0`. The native callback maps the event to a small `(event type, HWND)` value, enqueues it, and returns. It does not inspect processes, titles, AUMIDs, or WinUI state.

A separate single-reader background worker batches queued events for 15 ms and coalesces repeated events by HWND. Metadata refresh and state mutation therefore occur away from the native callback/message-loop thread. `WindowsChanged` is raised from this tracker background context and has no WinUI thread affinity; UI consumers must marshal through their own dispatcher.

## Reconciliation

A `PeriodicTimer` requests one full `EnumWindows` reconciliation every 30 seconds. Enumeration is not performed on every event, and there is no high-frequency global polling. Reconciliation replaces drifted state, removes windows missed by destroy/hide events, and reuses prior process identity metadata when the HWND/PID pair is unchanged.

The interval is deliberately slow because WinEvent notifications are the primary update path. Thirty seconds is a safety net, not the responsiveness mechanism.

## Lifetime

`WindowTracker.Start()` registers hooks before the initial enumeration so events that race startup can queue while initial state is built. Hook delegates are rooted by the subscription object. Disposal is idempotent: the hook thread is asked to quit, it unregisters every WinEvent hook before exiting, callbacks ignore disposed state, the channel is completed, cancellation stops the worker and periodic timer, and `DisposeAsync()` can await the managed worker tasks to finish.

The service is intentionally not wired into `App.xaml.cs`, `Program.cs`, or `PopupWindow.xaml.cs` in this slice. Resident-host ownership and UI consumption belong to later roadmap work.
