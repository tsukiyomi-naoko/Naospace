using System.IO;
using System.Windows.Threading;
using Nova.Core.Detection;
using Nova.Windows.Interop;

namespace Nova.Windows.Windowing;

public sealed class ForegroundChangedEventArgs(IntPtr hwnd, ForegroundInfo info) : EventArgs
{
    public IntPtr Hwnd { get; } = hwnd;
    public ForegroundInfo Info { get; } = info;
}

/// <summary>
/// Event-driven foreground tracking (SetWinEventHook, no polling). Also follows title changes of the foreground window
/// so that e.g. switching folders inside one VS Code window is recognised. Events are debounced to avoid churn while the
/// user alt-tabs through windows. Window titles are never logged.
/// </summary>
public sealed class ForegroundMonitor : IDisposable
{
    private readonly Native.WinEventProc _proc;
    private readonly DispatcherTimer _debounce;
    private readonly Dictionary<uint, string> _exeCache = new();
    private IntPtr _foregroundHook, _nameHook, _current;

    public event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

    /// <summary>Most recent top-level File Explorer window that was in the foreground.</summary>
    public IntPtr LastExplorerWindow { get; private set; }

    public ForegroundMonitor(Dispatcher dispatcher)
    {
        _proc = OnEvent;
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Raise(), dispatcher);
        _debounce.Stop();
    }

    public void Start()
    {
        if (_foregroundHook != IntPtr.Zero) return;
        var flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, flags);
        _nameHook = Native.SetWinEventHook(Native.EVENT_OBJECT_NAMECHANGE, Native.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _proc, 0, 0, flags);
    }

    public void Stop()
    {
        if (_foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(_foregroundHook);
        if (_nameHook != IntPtr.Zero) Native.UnhookWinEvent(_nameHook);
        _foregroundHook = _nameHook = IntPtr.Zero;
        _debounce.Stop();
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
        if (evt == Native.EVENT_SYSTEM_FOREGROUND)
        {
            _current = hwnd;
        }
        else if (evt == Native.EVENT_OBJECT_NAMECHANGE)
        {
            if (hwnd != _current) return; // only title changes of the foreground window matter
        }
        _debounce.Stop();
        _debounce.Start();
    }

    private void Raise()
    {
        _debounce.Stop();
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return;
        if (!_exeCache.TryGetValue(pid, out var exe))
        {
            exe = Path.GetFileName(Native.GetProcessImagePath(pid));
            if (_exeCache.Count > 512) _exeCache.Clear();
            _exeCache[pid] = exe;
        }
        if (string.IsNullOrEmpty(exe)) return; // e.g. elevated process: cannot identify, so never act on it
        if (exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && Native.GetClassNameOf(hwnd) == "CabinetWClass")
            LastExplorerWindow = hwnd;
        ForegroundChanged?.Invoke(this, new ForegroundChangedEventArgs(hwnd, new ForegroundInfo(exe, Native.GetWindowTitle(hwnd))));
    }

    public void Dispose() => Stop();
}
