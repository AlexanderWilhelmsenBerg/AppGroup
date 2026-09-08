using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AppGroup.WindowTracking;

internal enum WindowObservationEventType {
    Created,
    Destroyed,
    Shown,
    Hidden,
    MinimizeStarted,
    MinimizeEnded,
    ForegroundChanged,
    NameChanged,
    ReconcileRequested
}

internal readonly record struct WindowObservationEvent(
    WindowObservationEventType Type,
    IntPtr WindowHandle);

internal interface IWindowObservationSource {
    IReadOnlyCollection<WindowSnapshot> EnumerateWindows(
        IReadOnlyDictionary<IntPtr, WindowSnapshot> previousWindows);

    WindowSnapshot? TryGetWindow(
        IntPtr windowHandle,
        bool allowHidden,
        WindowSnapshot? previousWindow);

    IDisposable Subscribe(Action<WindowObservationEvent> sink);
}

internal sealed class WindowTrackerState {
    private readonly Dictionary<IntPtr, WindowSnapshot> _windows = new();

    public IReadOnlyDictionary<IntPtr, WindowSnapshot> CopyMap() =>
        new Dictionary<IntPtr, WindowSnapshot>(_windows);

    public WindowSnapshot? Get(IntPtr windowHandle) =>
        _windows.TryGetValue(windowHandle, out WindowSnapshot? snapshot) ? snapshot : null;

    public IReadOnlyCollection<WindowSnapshot> CopyWindows() =>
        _windows.Values.OrderBy(window => window.WindowHandle.ToInt64()).ToArray();

    public bool Reconcile(IEnumerable<WindowSnapshot> nextWindows) {
        Dictionary<IntPtr, WindowSnapshot> next = nextWindows
            .GroupBy(window => window.WindowHandle)
            .ToDictionary(group => group.Key, group => group.Last());

        bool changed = _windows.Count != next.Count ||
            _windows.Any(pair => !next.TryGetValue(pair.Key, out WindowSnapshot? nextValue) || pair.Value != nextValue);

        if (!changed) {
            return false;
        }

        _windows.Clear();
        foreach ((IntPtr handle, WindowSnapshot snapshot) in next) {
            _windows[handle] = snapshot;
        }

        return true;
    }

    public bool Upsert(WindowSnapshot snapshot) {
        if (_windows.TryGetValue(snapshot.WindowHandle, out WindowSnapshot? current) && current == snapshot) {
            return false;
        }

        _windows[snapshot.WindowHandle] = snapshot;
        return true;
    }

    public bool Remove(IntPtr windowHandle) => _windows.Remove(windowHandle);

    public bool SetForeground(IntPtr? foregroundWindow) {
        bool changed = false;
        foreach (IntPtr handle in _windows.Keys.ToArray()) {
            WindowSnapshot snapshot = _windows[handle];
            bool shouldBeForeground = foregroundWindow.HasValue && handle == foregroundWindow.Value;
            if (snapshot.IsForeground != shouldBeForeground) {
                _windows[handle] = snapshot with { IsForeground = shouldBeForeground };
                changed = true;
            }
        }

        return changed;
    }
}

internal sealed class WindowTrackerProcessor {
    private readonly IWindowObservationSource _source;
    private readonly WindowTrackerState _state;

    public WindowTrackerProcessor(IWindowObservationSource source, WindowTrackerState state) {
        _source = source;
        _state = state;
    }

    public bool Process(IReadOnlyCollection<WindowObservationEvent> events) {
        bool changed = false;
        bool reconcileRequested = events.Any(item => item.Type == WindowObservationEventType.ReconcileRequested);

        if (reconcileRequested) {
            IReadOnlyCollection<WindowSnapshot> current = _source.EnumerateWindows(_state.CopyMap());
            changed |= _state.Reconcile(current);
        }

        Dictionary<IntPtr, WindowObservationEvent> latestByWindow = new();
        WindowObservationEvent? latestForeground = null;

        foreach (WindowObservationEvent item in events) {
            if (item.Type == WindowObservationEventType.ReconcileRequested) {
                continue;
            }

            if (item.Type == WindowObservationEventType.ForegroundChanged) {
                latestForeground = item;
                continue;
            }

            if (item.WindowHandle != IntPtr.Zero) {
                latestByWindow[item.WindowHandle] = item;
            }
        }

        foreach ((IntPtr handle, WindowObservationEvent item) in latestByWindow) {
            if (item.Type == WindowObservationEventType.Destroyed) {
                changed |= _state.Remove(handle);
                continue;
            }

            WindowSnapshot? previous = _state.Get(handle);
            bool allowHidden = item.Type == WindowObservationEventType.Hidden && previous is not null;
            WindowSnapshot? refreshed = _source.TryGetWindow(handle, allowHidden, previous);

            if (refreshed is null) {
                if (previous is not null && item.Type != WindowObservationEventType.Created) {
                    changed |= _state.Remove(handle);
                }
                continue;
            }

            changed |= _state.Upsert(refreshed);
        }

        if (latestForeground.HasValue) {
            IntPtr handle = latestForeground.Value.WindowHandle;
            WindowSnapshot? previous = _state.Get(handle);
            WindowSnapshot? foreground = handle == IntPtr.Zero
                ? null
                : _source.TryGetWindow(handle, allowHidden: false, previous);

            if (foreground is not null) {
                changed |= _state.Upsert(foreground);
            }

            changed |= _state.SetForeground(foreground?.WindowHandle);
        }

        return changed;
    }
}

public sealed class WindowTracker : IDisposable, IAsyncDisposable {
    private static readonly TimeSpan DefaultReconciliationInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EventCoalesceDelay = TimeSpan.FromMilliseconds(15);

    private readonly IWindowObservationSource _source;
    private readonly TimeSpan _reconciliationInterval;
    private readonly WindowTrackerState _state = new();
    private readonly WindowTrackerProcessor _processor;
    private readonly Channel<WindowObservationEvent> _events;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifecycleGate = new();

    private IDisposable? _subscription;
    private Task? _workerTask;
    private Task? _reconciliationTask;
    private bool _started;
    private int _disposeStarted;

    public WindowTracker()
        : this(new Win32WindowObservationSource(), DefaultReconciliationInterval) {
    }

    internal WindowTracker(IWindowObservationSource source, TimeSpan reconciliationInterval) {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (reconciliationInterval <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(reconciliationInterval));
        }

        _reconciliationInterval = reconciliationInterval;
        _processor = new WindowTrackerProcessor(_source, _state);
        _events = Channel.CreateUnbounded<WindowObservationEvent>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public event EventHandler<WindowsChangedEventArgs>? WindowsChanged;

    public IReadOnlyCollection<WindowSnapshot> CurrentWindows {
        get {
            lock (_lifecycleGate) {
                return _state.CopyWindows();
            }
        }
    }

    public void Start() {
        ThrowIfDisposed();

        lock (_lifecycleGate) {
            if (_started) {
                return;
            }

            IDisposable? subscription = null;
            try {
                subscription = _source.Subscribe(EnqueueFromNativeCallback);
                IReadOnlyCollection<WindowSnapshot> initial = _source.EnumerateWindows(_state.CopyMap());
                bool changed = _state.Reconcile(initial);

                _subscription = subscription;
                _started = true;
                _workerTask = Task.Run(ProcessEventsAsync);
                _reconciliationTask = Task.Run(ReconciliationLoopAsync);

                if (changed) {
                    PublishChanged();
                }
            }
            catch {
                subscription?.Dispose();
                throw;
            }
        }
    }

    public void Dispose() {
        BeginDispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync() {
        BeginDispose();

        Task[] tasks = new[] { _workerTask, _reconciliationTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();

        try {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
        }

        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private void EnqueueFromNativeCallback(WindowObservationEvent item) {
        if (Volatile.Read(ref _disposeStarted) != 0) {
            return;
        }

        _events.Writer.TryWrite(item);
    }

    private async Task ProcessEventsAsync() {
        CancellationToken cancellationToken = _shutdown.Token;
        ChannelReader<WindowObservationEvent> reader = _events.Reader;

        try {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
                if (!reader.TryRead(out WindowObservationEvent first)) {
                    continue;
                }

                List<WindowObservationEvent> batch = new() { first };
                await Task.Delay(EventCoalesceDelay, cancellationToken).ConfigureAwait(false);
                while (reader.TryRead(out WindowObservationEvent next)) {
                    batch.Add(next);
                }

                bool changed;
                lock (_lifecycleGate) {
                    changed = _processor.Process(batch);
                }

                if (changed) {
                    PublishChanged();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        }
    }

    private async Task ReconciliationLoopAsync() {
        using PeriodicTimer timer = new(_reconciliationInterval);
        CancellationToken cancellationToken = _shutdown.Token;

        try {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
                _events.Writer.TryWrite(new WindowObservationEvent(
                    WindowObservationEventType.ReconcileRequested,
                    IntPtr.Zero));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        }
    }

    private void PublishChanged() {
        EventHandler<WindowsChangedEventArgs>? handlers = WindowsChanged;
        if (handlers is null) {
            return;
        }

        WindowsChangedEventArgs args = new(CurrentWindows);
        foreach (EventHandler<WindowsChangedEventArgs> handler in handlers.GetInvocationList().Cast<EventHandler<WindowsChangedEventArgs>>()) {
            try {
                handler(this, args);
            }
            catch (Exception ex) {
                Debug.WriteLine($"WindowTracker subscriber failed: {ex.Message}");
            }
        }
    }

    private void BeginDispose() {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) {
            return;
        }

        IDisposable? subscription;
        lock (_lifecycleGate) {
            subscription = _subscription;
            _subscription = null;
        }

        subscription?.Dispose();
        _shutdown.Cancel();
        _events.Writer.TryComplete();
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposeStarted) != 0) {
            throw new ObjectDisposedException(nameof(WindowTracker));
        }
    }
}
