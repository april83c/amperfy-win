namespace Amperfy.Core.Common;

/// The "main actor": all storage and player state is owned by one thread (the UI thread in the
/// app). Background work (network, file I/O, timers) posts back here.
public static class MainThread
{
    private static SynchronizationContext? _staticContext;
    private static int _staticThreadId = -1;

    // Per async-flow override (used by tests running in parallel, each with its own "main thread").
    private static readonly AsyncLocal<(SynchronizationContext Context, int ThreadId)?> FlowOverride = new();

    private static SynchronizationContext? _context => FlowOverride.Value?.Context ?? _staticContext;
    private static int _threadId => FlowOverride.Value?.ThreadId ?? _staticThreadId;

    /// Sets the process wide main thread context (app startup on the UI thread).
    public static void Initialize(SynchronizationContext context)
    {
        _staticContext = context;
        _staticThreadId = Environment.CurrentManagedThreadId;
    }

    /// Sets the main thread context for the current async flow only (tests).
    public static void InitializeForCurrentFlow(SynchronizationContext context) =>
        FlowOverride.Value = (context, Environment.CurrentManagedThreadId);

    public static bool IsInitialized => _context is not null;

    private static volatile bool _isShuttingDown;

    /// App shutdown: from now on posted work and timer ticks are dropped (they could otherwise run
    /// after the storage was disposed).
    public static void BeginShutdown() => _isShuttingDown = true;

    public static bool IsShuttingDown => _isShuttingDown;

    public static bool IsCurrent => _threadId == -1 || Environment.CurrentManagedThreadId == _threadId;

    /// Runs the action on the main thread (asynchronously; inline if no context is set up).
    public static void Post(Action action)
    {
        if (_context is null) action();
        else _context.Post(_ =>
        {
            if (!_isShuttingDown) action();
        }, null);
    }

    /// Runs the action on the main thread and completes when it ran.
    public static Task InvokeAsync(Action action)
    {
        if (_context is null || IsCurrent)
        {
            action();
            return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(_ =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, null);
        return tcs.Task;
    }

    public static Task<T> InvokeAsync<T>(Func<T> func)
    {
        if (_context is null || IsCurrent) return Task.FromResult(func());
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(_ =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, null);
        return tcs.Task;
    }

    /// Repeating timer whose ticks run on the main thread.
    public static IDisposable CreateTimer(TimeSpan interval, Action tick, bool repeats = true)
    {
        var timer = new MainThreadTimer(interval, tick, repeats);
        return timer;
    }

    private sealed class MainThreadTimer : IDisposable
    {
        private readonly Timer _timer;
        private volatile bool _disposed;

        public MainThreadTimer(TimeSpan interval, Action tick, bool repeats)
        {
            _timer = new Timer(_ =>
            {
                if (_disposed) return;
                Post(() =>
                {
                    if (!_disposed && !_isShuttingDown) tick();
                });
            }, null, interval, repeats ? interval : Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}

/// Single threaded synchronization context with its own message loop (tests, console tools).
public sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("Synchronous send is not supported.");

    public override SynchronizationContext CreateCopy() => this;

    private void RunUntilComplete(Task task)
    {
        task.ContinueWith(_ => _queue.CompleteAdding(), TaskScheduler.Default);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
    }

    /// Runs the async function on a fresh single-threaded context (like a UI thread) and waits for it.
    public static void Run(Func<Task> func)
    {
        var previous = Current;
        using var context = new SingleThreadSynchronizationContext();
        SetSynchronizationContext(context);
        try
        {
            MainThread.InitializeForCurrentFlow(context);
            var task = func();
            context.RunUntilComplete(task);
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    public void Dispose() => _queue.Dispose();
}
