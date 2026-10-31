using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Nova.Core.Diagnostics;
using Nova.Windows.Interop;

namespace Nova.Windows.Shell;

public sealed class ShellNavigatedEventArgs(string path, string displayName) : EventArgs
{
    public string Path { get; } = path;
    public string DisplayName { get; } = displayName;
}

public sealed class ShellNavigationFailedEventArgs(string path, string reason) : EventArgs
{
    public string Path { get; } = path;
    public string Reason { get; } = reason;
}

public readonly record struct SelectedShellItem(string Path, string Name, bool IsFolder, bool IsFileSystem);

/// <summary>
/// Hosts one native Windows Shell folder view (<c>IExplorerBrowser</c>) inside WPF.
///
/// Design notes (Milestone 0 feasibility findings):
/// <list type="bullet">
/// <item>WPF's UI thread is STA, which the Shell view requires. All calls happen on that thread.</item>
/// <item>The browser is parented to a private child "container" HWND that this HwndHost owns, so WPF can position,
/// hide and destroy it independently of the Shell's own internal windows.</item>
/// <item>One instance per tab. Inactive tabs are hidden (Visibility.Collapsed), not destroyed, so selection and scroll
/// position survive tab switches. The owner disposes instances to bound resource use.</item>
/// <item>Keyboard accelerators (F2, Del, Ctrl+C/V/X, Ctrl+Shift+N...) must be forwarded explicitly through
/// <see cref="IShellView.TranslateAccelerator"/>; WPF delivers them via <see cref="TranslateAcceleratorCore"/>.</item>
/// <item>NOVA keeps its own navigation history (<c>EBO_NOTRAVELLOG</c>) so history persists across restarts.</item>
/// <item>Airspace: WPF content cannot overlay this area; all popups are separate windows.</item>
/// </list>
/// The native view itself provides selection, multi-select, sorting, details/icon/thumbnail views, in-place rename,
/// drag-and-drop (OLE, also between two NOVA panes), the full native context menu and native copy-engine dialogs.
/// </summary>
public sealed class ShellBrowserHost : HwndHost
{
    private IExplorerBrowser? _browser;
    private EventSink? _sink;
    private uint _cookie;
    private IntPtr _container;
    private string? _pendingPath;
    private bool _disposed;

    public ShellBrowserHost()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
    }

    /// <summary>Raised after the native view has completed navigating (including navigation initiated inside the view).</summary>
    public event EventHandler<ShellNavigatedEventArgs>? Navigated;
    public event EventHandler<ShellNavigationFailedEventArgs>? NavigationFailed;

    /// <summary>Raised when keyboard focus enters the native view (used to track the active pane).</summary>
    public event EventHandler? FocusEntered;

    /// <summary>
    /// Application shortcuts get the first chance at key presses that occur inside the native view
    /// (e.g. Alt+Left, Ctrl+T, Ctrl+W, F5). Return true when handled.
    /// </summary>
    public Func<Key, ModifierKeys, bool>? ApplicationAccelerator { get; set; }

    public string? CurrentPath { get; private set; }
    public bool HasBrowser => _browser != null;
    public IntPtr ContainerHandle => _container;

    public void Navigate(string path)
    {
        if (_browser == null)
        {
            _pendingPath = path;
            return;
        }
        NavigateCore(path);
    }

    private void NavigateCore(string path)
    {
        var hr = ShellNative.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
        if (hr < 0 || pidl == IntPtr.Zero)
        {
            NavigationFailed?.Invoke(this, new ShellNavigationFailedEventArgs(path, Marshal.GetExceptionForHR(hr)?.Message ?? "The location could not be resolved."));
            return;
        }
        try
        {
            _browser!.BrowseToIDList(pidl, ShellNative.SBSP_ABSOLUTE);
        }
        catch (COMException ex)
        {
            NavigationFailed?.Invoke(this, new ShellNavigationFailedEventArgs(path, ex.Message));
        }
        finally
        {
            ShellNative.ILFree(pidl);
        }
    }

    public void Refresh() => WithView(v => v.Refresh());

    /// <summary>Moves Win32 keyboard focus into the item view.</summary>
    public void FocusView()
    {
        WithView(v =>
        {
            v.UIActivate(ShellNative.SVUIA_ACTIVATE_FOCUS);
            if (v.GetWindow(out var hwnd) >= 0 && hwnd != IntPtr.Zero) Native.SetFocus(hwnd);
        });
    }

    public IReadOnlyList<SelectedShellItem> GetSelection()
    {
        var result = new List<SelectedShellItem>();
        var view = GetView();
        if (view == null) return result;
        IntPtr pdo = IntPtr.Zero;
        try
        {
            var iid = ShellNative.IID_IDataObject;
            if (view.GetItemObject(ShellNative.SVGIO_SELECTION, ref iid, out pdo) < 0 || pdo == IntPtr.Zero)
                return result;
            var iidArray = ShellNative.IID_IShellItemArray;
            if (ShellNative.SHCreateShellItemArrayFromDataObject(pdo, ref iidArray, out var array) < 0 || array == null)
                return result;
            try
            {
                array.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    array.GetItemAt(i, out var item);
                    try
                    {
                        var path = ShellNative.GetName(item, SIGDN.DESKTOPABSOLUTEPARSING) ?? string.Empty;
                        var name = ShellNative.GetName(item, SIGDN.NORMALDISPLAY) ?? path;
                        item.GetAttributes(ShellNative.SFGAO_FOLDER | ShellNative.SFGAO_FILESYSTEM | ShellNative.SFGAO_STREAM, out var attrs);
                        // Zip archives report FOLDER|STREAM; treat them as files for "open folder" purposes.
                        var isFolder = (attrs & ShellNative.SFGAO_FOLDER) != 0 && (attrs & ShellNative.SFGAO_STREAM) == 0;
                        result.Add(new SelectedShellItem(path, name, isFolder, (attrs & ShellNative.SFGAO_FILESYSTEM) != 0));
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally { Marshal.ReleaseComObject(array); }
        }
        catch (COMException ex)
        {
            Log.Warn("Reading the shell selection failed", ex);
        }
        finally
        {
            if (pdo != IntPtr.Zero) Marshal.Release(pdo);
            Marshal.ReleaseComObject(view);
        }
        return result;
    }

    /// <summary>Invokes a canonical verb ("copy", "cut", "paste", "delete", "rename", "properties", "NewFolder"...)
    /// on the current selection or, when <paramref name="background"/> is true, on the folder background.</summary>
    public bool InvokeVerb(string verb, bool background)
    {
        var view = GetView();
        if (view == null) return false;
        IntPtr pcm = IntPtr.Zero;
        IntPtr menu = IntPtr.Zero;
        try
        {
            var iid = ShellNative.IID_IContextMenu;
            var what = background ? ShellNative.SVGIO_BACKGROUND : ShellNative.SVGIO_SELECTION;
            if (view.GetItemObject(what, ref iid, out pcm) < 0 || pcm == IntPtr.Zero) return false;
            var cm = (IContextMenu)Marshal.GetObjectForIUnknown(pcm);
            try
            {
                // Several Shell context-menu implementations only accept verbs after QueryContextMenu has run.
                menu = Native.CreatePopupMenu();
                cm.QueryContextMenu(menu, 0, 1, 0x7FFF, ShellNative.CMF_NORMAL | (background ? 0 : ShellNative.CMF_EXPLORE));
                var ansi = Marshal.StringToHGlobalAnsi(verb);
                var uni = Marshal.StringToHGlobalUni(verb);
                try
                {
                    var info = new CMINVOKECOMMANDINFOEX
                    {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                        fMask = ShellNative.CMIC_MASK_UNICODE,
                        hwnd = _container,
                        lpVerb = ansi,
                        lpVerbW = uni,
                        nShow = 1,
                    };
                    var hr = cm.InvokeCommand(ref info);
                    if (hr < 0 && hr != ShellNative.HRESULT_CANCELLED)
                        Log.Debug($"Verb '{verb}' failed with 0x{hr:X8}");
                    return hr >= 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(ansi);
                    Marshal.FreeHGlobal(uni);
                }
            }
            finally { Marshal.ReleaseComObject(cm); }
        }
        catch (COMException ex)
        {
            Log.Warn($"Invoking verb '{verb}' failed", ex);
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero) Native.DestroyMenu(menu);
            if (pcm != IntPtr.Zero) Marshal.Release(pcm);
            Marshal.ReleaseComObject(view);
        }
    }

    private IShellView? GetView()
    {
        if (_browser == null) return null;
        var iid = ShellNative.IID_IShellView;
        if (_browser.GetCurrentView(ref iid, out var p) < 0 || p == IntPtr.Zero) return null;
        try { return (IShellView)Marshal.GetObjectForIUnknown(p); }
        finally { Marshal.Release(p); }
    }

    private void WithView(Action<IShellView> action)
    {
        var v = GetView();
        if (v == null) return;
        try { action(v); }
        catch (COMException ex) { Log.Warn("Shell view call failed", ex); }
        finally { Marshal.ReleaseComObject(v); }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _container = Native.CreateWindowEx(0, "static", string.Empty,
            Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN | Native.WS_CLIPSIBLINGS,
            0, 0, 10, 10, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_container == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the file view container window.");

        try
        {
            var type = Type.GetTypeFromCLSID(ShellNative.CLSID_ExplorerBrowser, throwOnError: true)!;
            _browser = (IExplorerBrowser)Activator.CreateInstance(type)!;
            Native.GetClientRect(_container, out var rc);
            var fs = new FOLDERSETTINGS { ViewMode = ShellNative.FVM_DETAILS, fFlags = 0 };
            _browser.Initialize(_container, ref rc, ref fs);
            _browser.SetOptions(ExplorerBrowserOptions.NoTravelLog | ExplorerBrowserOptions.NoBorder);
            _sink = new EventSink(this);
            _browser.Advise(_sink, out _cookie);
            FocusTracker.Register(this);
        }
        catch (Exception ex)
        {
            Log.Error("Creating the embedded Shell browser failed", ex);
            ReleaseBrowser();
            throw;
        }

        if (_pendingPath != null)
        {
            var p = _pendingPath;
            _pendingPath = null;
            // Navigate after WPF finishes parenting so the first view gets the correct size.
            Dispatcher.BeginInvoke(() => { if (_browser != null) NavigateCore(p); });
        }
        return new HandleRef(this, _container);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        FocusTracker.Unregister(this);
        ReleaseBrowser();
        Native.DestroyWindow(hwnd.Handle);
        _container = IntPtr.Zero;
    }

    private void ReleaseBrowser()
    {
        if (_browser == null) return;
        try
        {
            if (_cookie != 0) _browser.Unadvise(_cookie);
            _browser.Destroy();
        }
        catch (COMException ex)
        {
            Log.Warn("Error while destroying Shell browser", ex);
        }
        finally
        {
            Marshal.FinalReleaseComObject(_browser);
            _browser = null;
            _cookie = 0;
            _sink = null;
        }
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)Native.WM_SIZE) ResizeBrowser();
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeBrowser();
    }

    private void ResizeBrowser()
    {
        if (_browser == null || _container == IntPtr.Zero) return;
        Native.GetClientRect(_container, out var rc);
        try { _browser.SetRect(IntPtr.Zero, rc); }
        catch (COMException) { }
    }

    protected override bool TranslateAcceleratorCore(ref System.Windows.Interop.MSG msg, ModifierKeys modifiers)
    {
        if (msg.message == Native.WM_KEYDOWN || msg.message == Native.WM_SYSKEYDOWN)
        {
            // While an in-place rename edit box is active, plain keys (Backspace, F5, ...) belong to the edit box.
            var editing = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 &&
                          Native.GetClassNameOf(Native.GetFocus()).Equals("Edit", StringComparison.OrdinalIgnoreCase);
            var key = KeyInterop.KeyFromVirtualKey((int)msg.wParam);
            if (!editing && ApplicationAccelerator?.Invoke(key, modifiers) == true)
                return true;
        }

        var view = GetView();
        if (view != null)
        {
            try
            {
                var native = new Interop.MSG
                {
                    hwnd = msg.hwnd,
                    message = (uint)msg.message,
                    wParam = msg.wParam,
                    lParam = msg.lParam,
                    time = (uint)msg.time,
                    pt = new POINT { X = msg.pt_x, Y = msg.pt_y },
                };
                if (view.TranslateAccelerator(ref native) == 0) return true;
            }
            finally { Marshal.ReleaseComObject(view); }
        }
        return base.TranslateAcceleratorCore(ref msg, modifiers);
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        FocusView();
        return true;
    }

    internal void OnNativeFocus() => FocusEntered?.Invoke(this, EventArgs.Empty);

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            FocusTracker.Unregister(this);
        }
        base.Dispose(disposing);
    }

    [ComVisible(true)]
    private sealed class EventSink(ShellBrowserHost owner) : IExplorerBrowserEvents
    {
        public int OnNavigationPending(IntPtr pidlFolder) => 0;
        public int OnViewCreated(object psv) => 0;

        public int OnNavigationComplete(IntPtr pidlFolder)
        {
            try
            {
                var path = ShellNative.GetName(pidlFolder, SIGDN.DESKTOPABSOLUTEPARSING) ?? string.Empty;
                var name = ShellNative.GetName(pidlFolder, SIGDN.NORMALDISPLAY) ?? path;
                owner.CurrentPath = path;
                owner.Navigated?.Invoke(owner, new ShellNavigatedEventArgs(path, name));
            }
            catch (Exception ex)
            {
                Log.Error("Navigation-complete handler failed", ex);
            }
            return 0;
        }

        public int OnNavigationFailed(IntPtr pidlFolder)
        {
            try
            {
                var path = ShellNative.GetName(pidlFolder, SIGDN.DESKTOPABSOLUTEPARSING) ?? string.Empty;
                owner.NavigationFailed?.Invoke(owner, new ShellNavigationFailedEventArgs(path, "The folder could not be opened."));
            }
            catch (Exception ex)
            {
                Log.Error("Navigation-failed handler failed", ex);
            }
            return 0;
        }
    }
}

/// <summary>
/// Detects when Win32 focus moves into one of the hosted Shell views (child windows that WPF does not see directly),
/// using an out-of-context WinEvent hook limited to NOVA's own process.
/// </summary>
internal static class FocusTracker
{
    private static readonly List<WeakReference<ShellBrowserHost>> Hosts = new();
    private static IntPtr _hook;
    private static Native.WinEventProc? _proc;

    public static void Register(ShellBrowserHost host)
    {
        Hosts.Add(new WeakReference<ShellBrowserHost>(host));
        if (_hook != IntPtr.Zero) return;
        _proc = OnEvent;
        _hook = Native.SetWinEventHook(Native.EVENT_OBJECT_FOCUS, Native.EVENT_OBJECT_FOCUS, IntPtr.Zero, _proc,
            (uint)Environment.ProcessId, 0, Native.WINEVENT_OUTOFCONTEXT);
    }

    public static void Unregister(ShellBrowserHost host)
    {
        Hosts.RemoveAll(w => !w.TryGetTarget(out var h) || ReferenceEquals(h, host));
        if (Hosts.Count == 0 && _hook != IntPtr.Zero)
        {
            Native.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
            _proc = null;
        }
    }

    private static void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        foreach (var w in Hosts.ToArray())
        {
            if (w.TryGetTarget(out var host) && host.ContainerHandle != IntPtr.Zero &&
                (hwnd == host.ContainerHandle || Native.IsChild(host.ContainerHandle, hwnd)))
            {
                host.OnNativeFocus();
                return;
            }
        }
    }
}
