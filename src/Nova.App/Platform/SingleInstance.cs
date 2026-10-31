namespace Nova.App.Platform;

/// <summary>One NOVA per user session; a second launch just activates the first.</summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\NovaWorkspace.SingleInstance";
    private const string EventName = @"Local\NovaWorkspace.Activate";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly CancellationTokenSource _cts = new();

    public bool IsFirst { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        IsFirst = created;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
    }

    public void SignalFirstInstance() => _activate.Set();

    public void ListenForActivation(Action onActivate)
    {
        var thread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _activate, _cts.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0) onActivate();
        }) { IsBackground = true, Name = "NOVA activation listener" };
        thread.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst) { try { _mutex.ReleaseMutex(); } catch (ApplicationException) { } }
        _mutex.Dispose();
        _activate.Dispose();
    }
}
