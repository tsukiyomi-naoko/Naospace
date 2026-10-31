using Nova.Core.Diagnostics;

namespace Nova.Core.Persistence;

/// <summary>
/// Debounced, ordered autosave. Serialization runs on the owning (UI) thread via the captured
/// <see cref="SynchronizationContext"/> so the model is never read concurrently with UI mutations; the disk write then
/// runs on a background thread. Writes for the same key are strictly ordered.
/// </summary>
public sealed class SaveScheduler : IDisposable
{
    private sealed class Pending
    {
        public required Func<string> Serialize { get; init; }
        public required Action<string> Write { get; init; }
        public Timer? Timer { get; set; }
    }

    private readonly TimeSpan _delay;
    private readonly SynchronizationContext? _context;
    private readonly Dictionary<string, Pending> _pending = new();
    private readonly Dictionary<string, object> _writeLocks = new();
    private readonly object _gate = new();
    private int _inFlight;
    private bool _disposed;

    public event Action<string, Exception>? SaveFailed;

    public SaveScheduler(TimeSpan delay, SynchronizationContext? context)
    {
        _delay = delay;
        _context = context;
    }

    public int PendingCount { get { lock (_gate) return _pending.Count; } }

    public void Request(string key, Func<string> serialize, Action<string> write)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_pending.TryGetValue(key, out var old)) old.Timer?.Dispose();
            var p = new Pending { Serialize = serialize, Write = write };
            p.Timer = new Timer(_ => OnElapsed(key, p), null, _delay, Timeout.InfiniteTimeSpan);
            _pending[key] = p;
        }
    }

    private void OnElapsed(string key, Pending p)
    {
        void SerializeAndQueue()
        {
            lock (_gate)
            {
                if (!_pending.TryGetValue(key, out var current) || !ReferenceEquals(current, p)) return;
                _pending.Remove(key);
                p.Timer?.Dispose();
                Interlocked.Increment(ref _inFlight);
            }
            string json;
            try
            {
                json = p.Serialize();
            }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref _inFlight);
                Report(key, ex);
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { WriteOrdered(key, p.Write, json); }
                finally { Interlocked.Decrement(ref _inFlight); }
            });
        }

        if (_context != null) _context.Post(_ => SerializeAndQueue(), null);
        else SerializeAndQueue();
    }

    private void WriteOrdered(string key, Action<string> write, string json)
    {
        object l;
        lock (_gate)
        {
            if (!_writeLocks.TryGetValue(key, out l!)) _writeLocks[key] = l = new object();
        }
        lock (l)
        {
            try { write(json); }
            catch (Exception ex) { Report(key, ex); }
        }
    }

    /// <summary>Synchronously writes everything pending. Must be called on the owning thread (e.g. on exit).</summary>
    public void FlushAll()
    {
        List<KeyValuePair<string, Pending>> items;
        lock (_gate)
        {
            items = _pending.ToList();
            _pending.Clear();
        }
        foreach (var (key, p) in items)
        {
            p.Timer?.Dispose();
            try { WriteOrdered(key, p.Write, p.Serialize()); }
            catch (Exception ex) { Report(key, ex); }
        }
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref _inFlight) > 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(5);
    }

    private void Report(string key, Exception ex)
    {
        Log.Error($"Saving '{key}' failed", ex);
        SaveFailed?.Invoke(key, ex);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var p in _pending.Values) p.Timer?.Dispose();
        }
    }
}
