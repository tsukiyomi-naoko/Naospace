using System.Diagnostics;
using System.Text;
using System.Windows.Threading;
using Nova.Core.Services;

namespace Nova.App.Platform;

/// <summary>
/// Reports the foreground application (exe name + title) using WinEvent hooks — no polling.
/// Title changes of the foreground window are included (e.g. VS Code switching repos),
/// debounced so noisy titles don't cause churn. NOVA's own windows are skipped.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly Native.WinEventDelegate _proc; // keep alive: the hook holds a raw pointer
    private readonly IntPtr _hookForeground;
    private IntPtr _hookName;
    private uint _hookNamePid;
    private readonly DispatcherTimer _debounce;
    private readonly Dictionary<uint, string> _processNames = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private IntPtr _pending;

    public event Action<ForegroundInfo, IntPtr>? Changed;
    public ForegroundInfo? Current { get; private set; }
    public IntPtr CurrentHwnd { get; private set; }

    public ForegroundWatcher()
    {
        _proc = OnWinEvent;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Publish(_pending); };
        const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        _hookForeground = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, flags);
    }

    /// <summary>
    /// Title changes are only interesting for the foreground app, so the name-change hook is
    /// scoped to that one process (system-wide name-change events are extremely chatty).
    /// </summary>
    private void WatchTitlesOf(uint pid)
    {
        if (pid == _hookNamePid && _hookName != IntPtr.Zero) return;
        if (_hookName != IntPtr.Zero) Native.UnhookWinEvent(_hookName);
        _hookNamePid = pid;
        _hookName = pid == 0 ? IntPtr.Zero : Native.SetWinEventHook(Native.EVENT_OBJECT_NAMECHANGE, Native.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _proc, pid, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != Native.OBJID_WINDOW || idChild != 0) return;
        if (evt == Native.EVENT_OBJECT_NAMECHANGE && hwnd != Native.GetForegroundWindow()) return;
        _pending = hwnd;
        _debounce.Stop();
        _debounce.Interval = TimeSpan.FromMilliseconds(evt == Native.EVENT_SYSTEM_FOREGROUND ? 120 : 600);
        _debounce.Start();
    }

    private void Publish(IntPtr hwnd)
    {
        var info = Describe(hwnd, out var pid);
        if (info is null || pid == _ownPid) return;
        WatchTitlesOf(pid);
        if (Current is not null && hwnd == CurrentHwnd && info == Current) return;
        Current = info;
        CurrentHwnd = hwnd;
        Changed?.Invoke(info, hwnd);
    }

    public ForegroundInfo? Describe(IntPtr hwnd, out uint pid)
    {
        pid = 0;
        if (hwnd == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        if (pid == 0) return null;
        var len = Native.GetWindowTextLength(hwnd);
        var sb = new StringBuilder(Math.Max(len + 1, 2));
        Native.GetWindowText(hwnd, sb, sb.Capacity);
        return new ForegroundInfo(GetProcessName(pid), sb.ToString());
    }

    private string GetProcessName(uint pid)
    {
        if (_processNames.TryGetValue(pid, out var cached)) return cached;
        string name = "";
        var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (Native.QueryFullProcessImageName(h, 0, sb, ref size))
                    name = System.IO.Path.GetFileName(sb.ToString());
            }
            finally { Native.CloseHandle(h); }
        }
        if (name.Length == 0)
        {
            try { name = Process.GetProcessById((int)pid).ProcessName + ".exe"; } catch (ArgumentException) { } catch (InvalidOperationException) { }
        }
        if (_processNames.Count > 512) _processNames.Clear();
        _processNames[pid] = name;
        return name;
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_hookForeground != IntPtr.Zero) Native.UnhookWinEvent(_hookForeground);
        if (_hookName != IntPtr.Zero) Native.UnhookWinEvent(_hookName);
    }
}
