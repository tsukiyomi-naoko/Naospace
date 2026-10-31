using System.Windows;
using System.Windows.Interop;
using Nova.App.Services;
using Nova.Core.Layout;
using Nova.Core.Model;
using Nova.Windows.Windowing;

namespace Nova.App.Views;

/// <summary>
/// Owns the main window's presentation: floating / docked (AppBar) / desktop modes, compact (sidebar-only) vs.
/// expanded, always-on-top, and multi-monitor placement persistence. Collapsing the browser never closes tabs.
/// </summary>
public sealed class WindowModeController : IDisposable
{
    private const double CompactChrome = 16; // window border allowance in DIPs
    private readonly MainWindow _window;
    private IntPtr _hwnd;
    private AppBar? _appBar;
    private bool _applying;

    public WindowModeController(MainWindow window) => _window = window;

    private static WindowPlacement P => AppServices.Settings.Window;
    public bool Compact => P.Compact;

    /// <summary>Restores the saved placement. Called from OnSourceInitialized (handle exists, window not yet shown).</summary>
    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        _window.SidebarColumn.Width = new GridLength(P.SidebarWidth);
        RestoreFloatingBounds();
        ApplyAll();
        if (P.Maximized && P.Mode == DisplayMode.Floating && !P.Compact) _window.WindowState = WindowState.Maximized;
    }

    public void SetMode(DisplayMode mode, DockEdge? edge = null)
    {
        if (P.Mode == DisplayMode.Floating || P.Mode == DisplayMode.Desktop) CapturePlacement();
        P.Mode = mode;
        if (edge.HasValue) P.DockEdge = edge.Value;
        if (mode != DisplayMode.Docked) _appBar?.Undock();
        if (mode != DisplayMode.Docked) RestoreFloatingBounds();
        ApplyAll();
        AppServices.SaveSettings();
    }

    public void SetCompact(bool compact)
    {
        if (P.Compact == compact) return;
        if (compact && P.Mode != DisplayMode.Docked && _window.WindowState == WindowState.Normal) CapturePlacement();
        P.Compact = compact;
        if (P.Mode != DisplayMode.Docked && !compact) RestoreFloatingBounds(keepPosition: true);
        ApplyAll();
        AppServices.SaveSettings();
    }

    public void SetTopmost(bool topmost)
    {
        P.Topmost = topmost;
        ApplyAll();
        AppServices.SaveSettings();
    }

    public void OnSidebarResized(double widthDip)
    {
        P.SidebarWidth = Math.Clamp(widthDip, 160, 1000);
        if (P.Mode == DisplayMode.Docked) ApplyDocking();
        AppServices.SaveSettings();
    }

    private void ApplyAll()
    {
        _applying = true;
        try
        {
            var docked = P.Mode == DisplayMode.Docked;
            _window.WindowStyle = docked ? WindowStyle.None : WindowStyle.SingleBorderWindow;
            _window.ResizeMode = docked ? ResizeMode.NoResize : ResizeMode.CanResize;
            if (docked && _window.WindowState == WindowState.Maximized) _window.WindowState = WindowState.Normal;
            _window.Topmost = P.Mode != DisplayMode.Desktop && P.Topmost;

            // Columns: compact = sidebar fills the window; expanded = fixed sidebar + browser.
            if (P.Compact)
            {
                _window.SidebarColumn.Width = new GridLength(1, GridUnitType.Star);
                _window.BrowserColumn.Width = new GridLength(0);
            }
            else
            {
                _window.SidebarColumn.Width = new GridLength(P.SidebarWidth);
                _window.BrowserColumn.Width = new GridLength(1, GridUnitType.Star);
            }

            if (docked) ApplyDocking();
            else if (P.Compact && _window.WindowState == WindowState.Normal) _window.Width = P.SidebarWidth + CompactChrome;

            if (P.Mode == DisplayMode.Desktop && _hwnd != IntPtr.Zero && !_window.IsActive) WindowTools.SendToBottom(_hwnd);
            _window.ReflectModeState(P.Compact, P.Topmost, P.Mode != DisplayMode.Desktop);
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplyDocking()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!_window.IsVisible) return; // re-applied in OnShown: a hidden window must not reserve desktop space
        _appBar ??= new AppBar(_window);
        var windowWidth = P.Compact ? P.SidebarWidth : Math.Max(P.DockedWidth, P.SidebarWidth + 400);
        _appBar.Dock(P.DockEdge, P.SidebarWidth, windowWidth);
    }

    public void OnShown()
    {
        if (P.Mode == DisplayMode.Docked) ApplyDocking();
    }

    public void OnHiding()
    {
        // Release the reserved strip while hidden so the desktop does not show an empty bar.
        if (P.Mode == DisplayMode.Docked) _appBar?.Undock();
    }

    public void OnDeactivated()
    {
        if (P.Mode == DisplayMode.Desktop && _hwnd != IntPtr.Zero)
            WindowTools.SendToBottom(_hwnd);
    }

    /// <summary>Saves floating bounds (physical pixels) and the monitor they belong to.</summary>
    public void CapturePlacement()
    {
        if (_applying || _hwnd == IntPtr.Zero || !_window.IsLoaded) return;
        if (P.Mode == DisplayMode.Docked) return;
        P.Maximized = _window.WindowState == WindowState.Maximized;
        if (_window.WindowState != WindowState.Normal) return;
        var b = WindowTools.GetBounds(_hwnd);
        P.Left = b.X;
        P.Top = b.Y;
        P.Height = b.Height;
        if (!P.Compact) P.Width = b.Width; // the compact width is derived, the expanded width is remembered
        P.MonitorDeviceName = WindowTools.GetMonitorDeviceName(_hwnd);
        AppServices.SaveSettings();
    }

    private void RestoreFloatingBounds(bool keepPosition = false)
    {
        if (_hwnd == IntPtr.Zero) return;
        var monitors = WindowTools.GetMonitors();
        if (monitors.Count == 0) return;
        var saved = new RectD(P.Left, P.Top, P.Width, P.Height);
        if (double.IsNaN(P.Left) || double.IsNaN(P.Top))
        {
            // First run: the defaults are in DIPs; convert to physical pixels for the current monitor's scale.
            var dpi = Nova.Windows.Interop.Native.GetDpiForWindow(_hwnd);
            var scale = dpi == 0 ? 1.0 : dpi / 96.0;
            saved = new RectD(double.NaN, double.NaN, P.Width * scale, P.Height * scale);
            P.Width = saved.Width;
            P.Height = saved.Height;
        }
        if (keepPosition)
        {
            var cur = WindowTools.GetBounds(_hwnd);
            saved = new RectD(cur.X, cur.Y, P.Width, cur.Height);
        }
        var (bounds, monitor) = ScreenGeometry.EnsureVisible(saved, P.MonitorDeviceName, monitors);
        WindowTools.SetBounds(_hwnd, bounds);
        P.MonitorDeviceName = monitor.DeviceName;
    }

    public void Dispose()
    {
        _appBar?.Dispose();
        _appBar = null;
    }
}
