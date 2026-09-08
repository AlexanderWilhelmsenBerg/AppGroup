using Xunit;
using AppGroup.WindowTracking;
using System;
using System.Collections.Generic;

namespace AppGroup.Tests;

public sealed class WindowTrackerStateTests {
    [Fact]
    public void Reconcile_removes_retains_updates_and_adds() {
        WindowTrackerState state = new();
        WindowSnapshot a = Snapshot(1, title: "A");
        WindowSnapshot b = Snapshot(2, title: "B");
        state.Reconcile(new[] { a, b });

        WindowSnapshot updatedB = b with { Title = "B updated" };
        WindowSnapshot c = Snapshot(3, title: "C");

        bool changed = state.Reconcile(new[] { updatedB, c });

        Assert.True(changed);
        Assert.Null(state.Get(new IntPtr(1)));
        Assert.Equal(updatedB, state.Get(new IntPtr(2)));
        Assert.Equal(c, state.Get(new IntPtr(3)));
    }

    [Fact]
    public void SetForeground_clears_previous_foreground() {
        WindowTrackerState state = new();
        state.Reconcile(new[] {
            Snapshot(1, foreground: true),
            Snapshot(2)
        });

        Assert.True(state.SetForeground(new IntPtr(2)));
        Assert.False(state.Get(new IntPtr(1))!.IsForeground);
        Assert.True(state.Get(new IntPtr(2))!.IsForeground);
    }

    private static WindowSnapshot Snapshot(long handle, string? title = null, bool foreground = false) =>
        new(new IntPtr(handle), (uint)handle, $"C:\\App{handle}.exe", null, title, true, false, foreground);
}

public sealed class WindowTrackerProcessorTests {
    [Fact]
    public void Duplicate_create_events_are_coalesced_to_one_metadata_refresh() {
        FakeWindowObservationSource source = new();
        WindowTrackerState state = new();
        WindowTrackerProcessor processor = new(source, state);
        source.SetWindow(Snapshot(1));

        bool changed = processor.Process(new[] {
            Event(WindowObservationEventType.Created, 1),
            Event(WindowObservationEventType.Created, 1)
        });

        Assert.True(changed);
        Assert.Equal(1, source.TryGetWindowCalls);
        Assert.NotNull(state.Get(new IntPtr(1)));
    }

    [Fact]
    public void Destroy_unknown_window_is_a_no_op() {
        FakeWindowObservationSource source = new();
        WindowTrackerState state = new();
        WindowTrackerProcessor processor = new(source, state);

        Assert.False(processor.Process(new[] { Event(WindowObservationEventType.Destroyed, 99) }));
    }

    [Fact]
    public void Name_change_updates_title_without_changing_process_identity() {
        FakeWindowObservationSource source = new();
        WindowTrackerState state = new();
        WindowTrackerProcessor processor = new(source, state);
        WindowSnapshot original = Snapshot(1, "Old");
        state.Reconcile(new[] { original });
        source.SetWindow(original with { Title = "New" });

        Assert.True(processor.Process(new[] { Event(WindowObservationEventType.NameChanged, 1) }));
        Assert.Equal("New", state.Get(new IntPtr(1))!.Title);
        Assert.Same(original, source.LastPreviousWindow);
    }

    [Fact]
    public void Hide_retains_known_window_as_not_visible_until_reconciliation() {
        FakeWindowObservationSource source = new();
        WindowTrackerState state = new();
        WindowTrackerProcessor processor = new(source, state);
        WindowSnapshot original = Snapshot(1);
        state.Reconcile(new[] { original });
        source.SetWindow(original with { IsVisible = false });

        Assert.True(processor.Process(new[] { Event(WindowObservationEventType.Hidden, 1) }));
        Assert.False(state.Get(new IntPtr(1))!.IsVisible);
        Assert.True(source.LastAllowHidden);
    }

    [Fact]
    public void Show_readds_window_removed_by_reconciliation() {
        FakeWindowObservationSource source = new();
        WindowTrackerState state = new();
        WindowTrackerProcessor processor = new(source, state);
        source.SetWindow(Snapshot(1));

        Assert.True(processor.Process(new[] { Event(WindowObservationEventType.Shown, 1) }));
        Assert.NotNull(state.Get(new IntPtr(1)));
    }

    [Fact]
    public void Event_after_reconciliation_removed_window_does_not_resurrect_stale_handle() {
        FakeWindowObservationSource source = new();
        WindowTrackerState state = new();
        WindowTrackerProcessor processor = new(source, state);
        state.Reconcile(new[] { Snapshot(1) });
        source.SetEnumeration();
        source.RemoveWindow(1);

        processor.Process(new[] { new WindowObservationEvent(WindowObservationEventType.ReconcileRequested, IntPtr.Zero) });
        bool changed = processor.Process(new[] { Event(WindowObservationEventType.NameChanged, 1) });

        Assert.False(changed);
        Assert.Null(state.Get(new IntPtr(1)));
    }

    [Fact]
    public void Foreground_transition_updates_only_trackable_target() {
        FakeWindowObservationSource source = new();
        WindowTrackerState state = new();
        WindowTrackerProcessor processor = new(source, state);
        WindowSnapshot a = Snapshot(1, foreground: true);
        WindowSnapshot b = Snapshot(2);
        state.Reconcile(new[] { a, b });
        source.SetWindow(b with { IsForeground = true });

        Assert.True(processor.Process(new[] { Event(WindowObservationEventType.ForegroundChanged, 2) }));
        Assert.False(state.Get(new IntPtr(1))!.IsForeground);
        Assert.True(state.Get(new IntPtr(2))!.IsForeground);
    }

    [Fact]
    public void Metadata_failure_degrades_to_nullable_values() {
        FakeWindowObservationSource source = new();
        WindowTrackerState state = new();
        WindowTrackerProcessor processor = new(source, state);
        source.SetWindow(new WindowSnapshot(new IntPtr(1), null, null, null, null, true, false, false));

        Assert.True(processor.Process(new[] { Event(WindowObservationEventType.Created, 1) }));
        WindowSnapshot snapshot = state.Get(new IntPtr(1))!;
        Assert.Null(snapshot.ProcessId);
        Assert.Null(snapshot.ExecutablePath);
        Assert.Null(snapshot.AppUserModelId);
        Assert.Null(snapshot.Title);
    }

    private static WindowObservationEvent Event(WindowObservationEventType type, long handle) =>
        new(type, new IntPtr(handle));

    private static WindowSnapshot Snapshot(long handle, string? title = null, bool foreground = false) =>
        new(new IntPtr(handle), (uint)handle, $"C:\\App{handle}.exe", $"App.{handle}", title, true, false, foreground);
}

public sealed class WindowTrackerLifecycleTests {
    [Fact]
    public void Dispose_unregisters_subscription_exactly_once_and_ignores_late_events() {
        FakeWindowObservationSource source = new();
        source.SetEnumeration(Snapshot(1));
        WindowTracker tracker = new(source, TimeSpan.FromHours(1));
        tracker.Start();

        tracker.Dispose();
        tracker.Dispose();
        source.Emit(Event(WindowObservationEventType.Destroyed, 1));

        Assert.Equal(1, source.SubscriptionDisposeCount);
        Assert.Single(tracker.CurrentWindows);
    }

    private static WindowObservationEvent Event(WindowObservationEventType type, long handle) =>
        new(type, new IntPtr(handle));

    private static WindowSnapshot Snapshot(long handle) =>
        new(new IntPtr(handle), (uint)handle, null, null, string.Empty, true, false, false);
}

internal sealed class FakeWindowObservationSource : IWindowObservationSource {
    private readonly Dictionary<IntPtr, WindowSnapshot> _windows = new();
    private IReadOnlyCollection<WindowSnapshot>? _enumerationOverride;
    private Action<WindowObservationEvent>? _sink;

    public int TryGetWindowCalls { get; private set; }
    public int SubscriptionDisposeCount { get; private set; }
    public bool LastAllowHidden { get; private set; }
    public WindowSnapshot? LastPreviousWindow { get; private set; }

    public IReadOnlyCollection<WindowSnapshot> EnumerateWindows(
        IReadOnlyDictionary<IntPtr, WindowSnapshot> previousWindows) {
        _ = previousWindows;
        return _enumerationOverride ?? _windows.Values.ToArray();
    }

    public WindowSnapshot? TryGetWindow(
        IntPtr windowHandle,
        bool allowHidden,
        WindowSnapshot? previousWindow) {
        TryGetWindowCalls++;
        LastAllowHidden = allowHidden;
        LastPreviousWindow = previousWindow;
        return _windows.TryGetValue(windowHandle, out WindowSnapshot? snapshot) ? snapshot : null;
    }

    public IDisposable Subscribe(Action<WindowObservationEvent> sink) {
        _sink = sink;
        return new DelegateDisposable(() => {
            SubscriptionDisposeCount++;
        });
    }

    public void SetWindow(WindowSnapshot snapshot) => _windows[snapshot.WindowHandle] = snapshot;

    public void RemoveWindow(long handle) => _windows.Remove(new IntPtr(handle));

    public void SetEnumeration(params WindowSnapshot[] windows) => _enumerationOverride = windows;

    public void Emit(WindowObservationEvent item) => _sink?.Invoke(item);

    private sealed class DelegateDisposable : IDisposable {
        private readonly Action _dispose;
        private int _disposed;

        public DelegateDisposable(Action dispose) {
            _dispose = dispose;
        }

        public void Dispose() {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 0) {
                _dispose();
            }
        }
    }
}
