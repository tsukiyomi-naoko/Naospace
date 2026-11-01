using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Nova.App.Platform;
using Nova.App.ViewModels;
using Nova.Core.Models;
using Nova.Core.Persistence;
using Nova.Core.Services;

namespace Nova.App.Views;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand QuickJumpCommand = new();
    private const string PinNodeFormat = "NovaWorkspace.PinNode";

    private readonly MainViewModel _vm;
    private readonly QuickJumpViewModel _quick;
    private HotkeyManager? _hotkeys;
    private ForegroundWatcher? _foreground;
    private AppBar? _appBar;
    private TrayIcon? _tray;
    private bool _exiting;
    private bool _trayHintShown;
    private bool _applyingBounds;
    private Point _dragStart;
    private PinNode? _dragNode;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _quick = new QuickJumpViewModel(vm);
        DataContext = vm;
        QuickJumpOverlay.DataContext = _quick;
        vm.OwnerHandle = () => new WindowInteropHelper(this).Handle;

        CommandBindings.Add(new CommandBinding(QuickJumpCommand, (_, _) => ToggleQuickJump()));
        vm.PropertyChanged += OnViewModelChanged;
        vm.Settings.PropertyChanged += OnSettingsChanged;
        _quick.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuickJumpViewModel.Selected) && _quick.Selected is not null)
                QuickJumpList.ScrollIntoView(_quick.Selected);
        };

        PreviewKeyDown += OnGlobalKeyDown;
        Deactivated += (_, _) =>
        {
            if (_vm.Settings.HideOnFocusLoss && _appBar?.IsDocked != true && !_exiting && OwnedWindows.Count == 0) HideToTray();
        };
        PinTree.PreviewMouseRightButtonDown += SelectTreeItemUnderMouse;

        ApplyBounds();
        ApplyLayout();
        Topmost = vm.Settings.AlwaysOnTop;
        _lastDock = vm.Settings.Dock;
    }

    // ═══════════════════════ Window lifecycle & desktop integration ═══════════════════════

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this);
        _hotkeys = new HotkeyManager(source);
        RegisterHotkeys();

        _foreground = new ForegroundWatcher();
        _foreground.Changed += (info, _) => _vm.OnForeground(info);

        _tray = new TrayIcon(ShowAndActivate, () => ((App)Application.Current).ExitApplication());
        _appBar = new AppBar(this);
        ApplyDock();
    }

    public void RegisterHotkeys()
    {
        if (_hotkeys is null) return;
        _hotkeys.UnregisterAll();
        var errors = new List<string>();
        if (_hotkeys.Register(_vm.Settings.ToggleHotkey, ToggleFromHotkey) is { } e1) errors.Add(e1);
        if (_hotkeys.Register(_vm.Settings.CaptureHotkey, CaptureFromHotkey) is { } e2) errors.Add(e2);
        if (errors.Count > 0) _vm.ShowStatus("Hotkey: " + string.Join(" ", errors));
    }

    private void ToggleFromHotkey()
    {
        if (IsVisible && IsActive && WindowState != WindowState.Minimized) { HideToTray(); return; }
        ShowAndActivate();
        OpenQuickJump();
    }

    private void CaptureFromHotkey()
    {
        var fg = Native.GetForegroundWindow();
        var folder = _vm.CaptureExplorerFolder(fg);
        var msg = folder is null
            ? "The active window isn't a File Explorer folder."
            : $"Captured “{Path.GetFileName(folder.TrimEnd('\\'))}” into {_vm.ActiveWorkspace.Name} › working set";
        _vm.ShowStatus(msg);
        if (!IsVisible || WindowState == WindowState.Minimized) _tray?.Notify("NOVA Workspace", msg);
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
            if (_vm.Settings.Dock != DockMode.Floating) ApplyDock();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetForegroundWindow(hwnd);
    }

    private void HideToTray()
    {
        SaveBounds();
        // A hidden appbar would leave an empty reserved strip; release the edge while hidden.
        if (_appBar?.IsDocked == true) _appBar.Undock();
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray?.Notify("NOVA is still running", $"Press {_vm.Settings.ToggleHotkey} or click the tray icon to bring it back.");
        }
    }

    public void PrepareForExit() => _exiting = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true; // closing the window keeps NOVA available via hotkey/tray
            HideToTray();
            return;
        }
        SaveBounds();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkeys?.Dispose();
        _foreground?.Dispose();
        _appBar?.Dispose();
        _tray?.Dispose();
        base.OnClosed(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsCompact):
                OnCompactToggled();
                break;
            case nameof(MainViewModel.IsDualPane):
                ApplyLayout();
                break;
            case nameof(MainViewModel.IsTopmost):
                Topmost = _vm.IsTopmost;
                break;
        }
    }

    private DockMode _lastDock = DockMode.Floating;

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Dock))
        {
            if (_lastDock == DockMode.Floating) SaveBounds(force: true); // remember the floating geometry
            _lastDock = _vm.Settings.Dock;
            ApplyDock();
        }
        if (e.PropertyName == nameof(AppSettings.Theme)) App.ApplyTheme(_vm.Settings.Theme);
        if (e.PropertyName == nameof(AppSettings.ShowHiddenFiles))
            foreach (var t in _vm.LeftPane.Tabs.Concat(_vm.RightPane.Tabs)) _ = t.ReloadAsync();
    }

    private void OnToggleBrowser(object sender, RoutedEventArgs e) => _vm.IsCompact = !_vm.IsCompact;

    /// <summary>
    /// Retracts/reveals the browser while keeping the sidebar where it is on screen: only the
    /// width changes (each mode remembers its own width); position and height carry over.
    /// </summary>
    private void OnCompactToggled()
    {
        var wasCompact = !_vm.IsCompact;
        var wasMaximized = WindowState == WindowState.Maximized;
        var current = wasMaximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        if (wasMaximized && !wasCompact)
        {
            // Retracting from a maximized window: anchor to the screen's left work edge.
            var wa = SystemParameters.WorkArea;
            current = new Rect(Left < wa.Left ? wa.Left : current.Left, wa.Top, current.Width, wa.Height);
        }
        SaveBounds(wasCompact);
        ApplyLayout();
        if (_appBar?.IsDocked == true) { ApplyDock(); return; }

        _applyingBounds = true;
        try
        {
            var target = CurrentModeBounds();
            WindowState = WindowState.Normal;
            var width = Math.Max(MinWidth, target.Width > 0 ? target.Width : (_vm.IsCompact ? 300 : 1100));
            var left = current.Left;
            // If the sidebar hugs the right side of the screen, grow/shrink towards the left instead.
            var screenRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            var screenLeft = SystemParameters.VirtualScreenLeft;
            if (left + width > screenRight) left = Math.Max(screenLeft, screenRight - width);
            Left = left;
            Top = current.Top;
            Height = current.Height;
            Width = width;
            if (!_vm.IsCompact && target.Maximized) WindowState = WindowState.Maximized;
        }
        finally { _applyingBounds = false; }
    }

    private void ApplyLayout()
    {
        var compact = _vm.IsCompact;
        SidebarColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(_vm.Settings.SidebarWidth);
        SplitterColumn.Width = compact ? new GridLength(0) : new GridLength(4);
        BrowserColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        BrowserHost.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarSplitter.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

        // Top bar: one row when expanded; two rows when compact so every control stays reachable.
        void Place(UIElement e, int row, int col, int span) { Grid.SetRow(e, row); Grid.SetColumn(e, col); Grid.SetColumnSpan(e, span); }
        if (compact)
        {
            Place(WorkspaceGroup, 0, 0, 4); WorkspaceGroup.Width = double.NaN; WorkspaceGroup.Margin = new Thickness(0, 0, 0, 4);
            Place(SessionGroup, 1, 0, 3); SessionGroup.Width = double.NaN;
            Place(ToolButtons, 1, 3, 1);
            Place(DetectChip, 2, 0, 4); DetectChip.Margin = new Thickness(0, 4, 0, 0);
        }
        else
        {
            Place(WorkspaceGroup, 0, 0, 1); WorkspaceGroup.Width = 230; WorkspaceGroup.Margin = new Thickness(0, 0, 8, 0);
            Place(SessionGroup, 0, 1, 1); SessionGroup.Width = 190;
            Place(ToolButtons, 0, 3, 1);
            Place(DetectChip, 0, 2, 1); DetectChip.Margin = new Thickness(0);
        }
        JumpLabel.Visibility = JumpHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

        var dual = _vm.IsDualPane && !compact;
        PaneSplitterColumn.Width = new GridLength(dual ? 5 : 0);
        RightPaneColumn.Width = dual ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RightPaneView.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
        LeftPaneView.UpdateActiveFrame();
        RightPaneView.UpdateActiveFrame();
    }

    private void ApplyDock()
    {
        if (_appBar is null) return;
        var dock = _vm.Settings.Dock;
        if (dock == DockMode.Floating)
        {
            if (_appBar.IsDocked)
            {
                _appBar.Undock();
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                ApplyBounds();
            }
            return;
        }
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        var width = _vm.IsCompact ? _vm.Settings.Compact.Width : _vm.Settings.Expanded.Width;
        _appBar.Dock(dock == DockMode.Right, Math.Max(220, width));
    }

    private WindowBounds CurrentModeBounds(bool? compact = null) =>
        (compact ?? _vm.IsCompact) ? _vm.Settings.Compact : _vm.Settings.Expanded;

    private void SaveBounds(bool? compactMode = null, bool force = false)
    {
        // Only floating geometry is remembered; docked geometry is owned by the shell.
        if (_applyingBounds || !IsLoaded) return;
        if (!force && (_vm.Settings.Dock != DockMode.Floating || _appBar?.IsDocked == true)) return;
        var b = CurrentModeBounds(compactMode);
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (r.Width < 100 || double.IsInfinity(r.Width)) return;
        b.Left = r.Left; b.Top = r.Top; b.Width = r.Width; b.Height = r.Height;
        b.Maximized = WindowState == WindowState.Maximized;
    }

    private void ApplyBounds()
    {
        _applyingBounds = true;
        try
        {
            var b = CurrentModeBounds();
            WindowState = WindowState.Normal;
            Width = Math.Max(MinWidth, b.Width > 0 ? b.Width : 1000);
            Height = Math.Max(MinHeight, b.Height > 0 ? b.Height : 700);
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var wanted = new Rect(double.IsNaN(b.Left) ? double.NaN : b.Left, double.IsNaN(b.Top) ? double.NaN : b.Top, Width, Height);
            if (double.IsNaN(b.Left) || double.IsNaN(b.Top) || !screen.IntersectsWith(new Rect(wanted.Left + 40, wanted.Top, 120, 40)))
            {
                // Unknown or now-offscreen position (monitor unplugged): centre on the primary work area.
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
                Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
            }
            else
            {
                Left = b.Left;
                Top = b.Top;
            }
            if (b.Maximized && !_vm.IsCompact) WindowState = WindowState.Maximized;
        }
        finally { _applyingBounds = false; }
    }

    private void OnSidebarResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (!_vm.IsCompact) _vm.Settings.SidebarWidth = Math.Round(SidebarColumn.ActualWidth);
    }

    // ═══════════════════════ Keyboard ═══════════════════════

    private void OnGlobalKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var tab = _vm.ActivePane.SelectedTab;

        if (QuickJumpOverlay.Visibility == Visibility.Visible) return; // palette handles its own keys

        bool handled = true;
        if (ctrl && !shift && key is >= Key.D1 and <= Key.D9)
        {
            var i = key - Key.D1;
            if (i < _vm.Workspaces.Count) _vm.SwitchWorkspace(_vm.Workspaces[i], null, manual: true);
        }
        else if (ctrl && key == Key.T) _ = _vm.ActivePane.NewTabAsync(tab?.Path ?? _vm.DefaultLocation());
        else if (ctrl && key == Key.W) _vm.ActivePane.CloseTabCommand.Execute(null);
        else if (ctrl && key == Key.Tab) _vm.ActivePane.SelectRelative(shift ? -1 : 1);
        else if (ctrl && key == Key.B) _vm.IsCompact = !_vm.IsCompact;
        else if (ctrl && key == Key.OemPipe) _vm.IsDualPane = !_vm.IsDualPane;
        else if (ctrl && !shift && key == Key.D) _vm.PinCurrentFolderCommand.Execute(null);
        else if (ctrl && shift && key == Key.D) _vm.AddCurrentToWorkingSetCommand.Execute(null);
        else if (key == Key.F6 && _vm.IsDualPane) { _vm.ActivatePane(_vm.OtherPane); PaneView(_vm.ActivePane).FocusList(); }
        else if ((ctrl && key == Key.L) || (alt && key == Key.D)) PaneView(_vm.ActivePane).FocusAddress();
        else if (ctrl && key == Key.F) PaneView(_vm.ActivePane).FocusFilter();
        else if (alt && key == Key.Left && tab is not null) _ = tab.GoBackAsync();
        else if (alt && key == Key.Right && tab is not null) _ = tab.GoForwardAsync();
        else if (alt && key == Key.Up && tab is not null) _ = tab.GoUpAsync();
        else if (key == Key.BrowserBack && tab is not null) _ = tab.GoBackAsync();
        else if (key == Key.BrowserForward && tab is not null) _ = tab.GoForwardAsync();
        else if (key == Key.F5 && tab is not null) _ = tab.ReloadAsync(preserveSelection: true, currentSelection: PaneView(_vm.ActivePane).SelectedNames());
        else handled = false;
        e.Handled = handled;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        // Mouse back/forward buttons.
        var tab = _vm.ActivePane.SelectedTab;
        if (tab is not null && e.ChangedButton == MouseButton.XButton1) { _ = tab.GoBackAsync(); e.Handled = true; }
        else if (tab is not null && e.ChangedButton == MouseButton.XButton2) { _ = tab.GoForwardAsync(); e.Handled = true; }
        base.OnMouseUp(e);
    }

    private BrowserPaneView PaneView(BrowserPaneViewModel pane) => pane == _vm.RightPane ? RightPaneView : LeftPaneView;

    // ═══════════════════════ Quick jump ═══════════════════════

    private void OnQuickJump(object sender, RoutedEventArgs e) => OpenQuickJump();

    private void ToggleQuickJump()
    {
        if (QuickJumpOverlay.Visibility == Visibility.Visible) CloseQuickJump();
        else OpenQuickJump();
    }

    private void OpenQuickJump()
    {
        _quick.Open();
        QuickJumpOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => { QuickJumpBox.Focus(); Keyboard.Focus(QuickJumpBox); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseQuickJump()
    {
        _quick.Close();
        QuickJumpOverlay.Visibility = Visibility.Collapsed;
        PaneView(_vm.ActivePane).FocusList();
    }

    private async void OnQuickJumpKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: CloseQuickJump(); e.Handled = true; break;
            case Key.Down: _quick.Move(1); e.Handled = true; break;
            case Key.Up: _quick.Move(-1); e.Handled = true; break;
            case Key.PageDown: _quick.Move(8); e.Handled = true; break;
            case Key.PageUp: _quick.Move(-8); e.Handled = true; break;
            case Key.Enter:
                e.Handled = true;
                QuickJumpOverlay.Visibility = Visibility.Collapsed;
                await _quick.ExecuteAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                PaneView(_vm.ActivePane).FocusList();
                break;
        }
    }

    private async void OnQuickJumpClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder<ListBoxItem>(e.OriginalSource) is null) return;
        QuickJumpOverlay.Visibility = Visibility.Collapsed;
        await _quick.ExecuteAsync(alternate: e.ChangedButton == MouseButton.Middle);
    }

    private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e) => CloseQuickJump();
    private void OnOverlayPanelMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // ═══════════════════════ Workspace & session menus ═══════════════════════

    private void OnWorkspaceMenu(object sender, RoutedEventArgs e)
    {
        var ws = _vm.ActiveWorkspace;
        ShowMenu((FrameworkElement)sender,
            Item("New workspace…", () =>
            {
                if (InputDialog.Ask(this, "New workspace", "Name:", "") is { Length: > 0 } name) _vm.CreateWorkspace(name);
            }),
            Item("Workspace settings…", () => OpenWorkspaceSettings(ws)),
            Item($"Detect “{ws.Name}” from the last active app", () =>
            {
                var rule = _vm.AddRuleFromLastApp(ws);
                _vm.ShowStatus(rule is null ? "No other application has been active yet." :
                    $"{ws.Name} will activate for {rule.ProcessName}{(rule.TitlePattern is null ? "" : $" · “{rule.TitlePattern}”")}");
            }, enabled: _vm.LastExternal is not null),
            null,
            Item("Export workspace…", () => ExportWorkspace(ws)),
            Item("Import workspace…", ImportWorkspace),
            null,
            Item("Delete workspace…", () =>
            {
                if (MessageBox.Show(this, $"Delete the workspace “{ws.Name}”?\n\nThis removes its pins, sessions and rules from NOVA. No files are deleted.",
                        "Delete workspace", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                    _vm.DeleteWorkspace(ws);
            }, enabled: _vm.Workspaces.Count > 1));
    }

    private void OnSessionMenu(object sender, RoutedEventArgs e)
    {
        var s = _vm.ActiveSession;
        ShowMenu((FrameworkElement)sender,
            Item("New session…", () =>
            {
                if (InputDialog.Ask(this, "New session", "Name:", "") is { Length: > 0 } n) _vm.CreateSession(n, fromCurrent: false);
            }),
            Item("Duplicate session", () => _vm.CreateSession(s.Name + " (copy)", fromCurrent: true)),
            Item("Rename session…", () =>
            {
                if (InputDialog.Ask(this, "Rename session", "Name:", s.Name) is { Length: > 0 } n) s.Name = n;
            }),
            null,
            Item("Delete session", () =>
            {
                if (MessageBox.Show(this, $"Delete the session “{s.Name}”? Its tabs and working set will be forgotten.",
                        "Delete session", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                    _vm.DeleteSession(s);
            }, enabled: _vm.ActiveWorkspace.Sessions.Count > 1));
    }

    private void OpenWorkspaceSettings(Workspace ws)
    {
        new WorkspaceSettingsDialog(ws, _vm) { Owner = this }.ShowDialog();
        // Re-evaluate detection in case rules changed.
        if (_vm.LastExternal is { } fg) _vm.OnForeground(fg);
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        new SettingsDialog(_vm.Settings) { Owner = this }.ShowDialog();
        RegisterHotkeys();
    }

    private void ExportWorkspace(Workspace ws)
    {
        var dlg = new SaveFileDialog
        {
            FileName = ws.Name + WorkspacePorter.FileExtension,
            Filter = $"NOVA workspace (*{WorkspacePorter.FileExtension})|*{WorkspacePorter.FileExtension}",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            StateStore.WriteAtomic(dlg.FileName, WorkspacePorter.Export(ws, includeHistory: false));
            _vm.ShowStatus($"Exported {ws.Name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _vm.ShowStatus("Export failed: " + ex.Message); }
    }

    private void ImportWorkspace()
    {
        var dlg = new OpenFileDialog { Filter = $"NOVA workspace (*{WorkspacePorter.FileExtension})|*{WorkspacePorter.FileExtension}|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var ws = WorkspacePorter.Import(File.ReadAllText(dlg.FileName), _vm.Workspaces.Select(w => w.Name));
            _vm.ImportWorkspace(ws);
            _vm.ShowStatus($"Imported {ws.Name}. Check its variables if paths differ on this PC.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, "Could not import: " + ex.Message, "Import workspace", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════ Pins ═══════════════════════

    private PinNode? SelectedPin => PinTree.SelectedItem as PinNode;

    private void OnAddPinMenu(object sender, RoutedEventArgs e) => ShowMenu((FrameworkElement)sender, AddPinItems(null).ToArray());

    private IEnumerable<MenuItem?> AddPinItems(PinGroup? group)
    {
        var current = _vm.ActivePane.SelectedTab?.Path;
        yield return Item("Pin current folder" + (current is null ? "" : $"  ({Path.GetFileName(current.TrimEnd('\\'))})"),
            () => { if (current is not null) _vm.PinPaths(new[] { current }, group); }, enabled: current is not null);
        yield return Item("Add folder…", () =>
        {
            var dlg = new OpenFolderDialog { Multiselect = true };
            if (dlg.ShowDialog(this) == true) _vm.PinPaths(dlg.FolderNames, group);
        });
        yield return Item("Add file, app or script…", () =>
        {
            var dlg = new OpenFileDialog { Multiselect = true, DereferenceLinks = false };
            if (dlg.ShowDialog(this) == true) _vm.PinPaths(dlg.FileNames, group);
        });
        yield return Item("Add web link or command…", () =>
        {
            var pin = new PinItem { Kind = PinKind.Url, Name = "New link", Target = "https://" };
            if (new PinEditorDialog(pin) { Owner = this }.ShowDialog() == true)
                (group?.Children ?? _vm.ActiveWorkspace.Pins).Add(pin);
        });
        yield return null;
        yield return Item("New group…", () =>
        {
            if (InputDialog.Ask(this, "New group", "Group name:", "") is { Length: > 0 } n)
                (group?.Children ?? _vm.ActiveWorkspace.Pins).Add(new PinGroup { Name = n });
        });
    }

    private void OnPinContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = PinTree.ContextMenu!;
        menu.Items.Clear();
        var node = ItemUnder<TreeViewItem>(e.OriginalSource) is not null ? SelectedPin : null;
        IEnumerable<MenuItem?> items = node switch
        {
            PinItem p => PinItemMenu(p),
            PinGroup g => GroupMenu(g),
            _ => AddPinItems(null),
        };
        foreach (var i in items) menu.Items.Add(i is null ? new Separator() : i);
    }

    private IEnumerable<MenuItem?> PinItemMenu(PinItem pin)
    {
        var resolved = _vm.Resolve(pin);
        if (pin.Kind == PinKind.Folder)
        {
            yield return Item("Open", () => _ = _vm.OpenPinAsync(pin), bold: true);
            yield return Item("Open in new tab", () => _ = _vm.OpenPinAsync(pin, OpenTarget.NewTab));
            yield return Item("Open in other pane", () => _ = _vm.OpenPinAsync(pin, OpenTarget.OtherPane));
            yield return Item("Open in File Explorer", () => Report(ShellLauncher.OpenInExplorer(resolved)));
            yield return Item("Open terminal here", () => Report(ShellLauncher.OpenTerminal(resolved)));
            yield return Item("Add to working set", () => _vm.AddToWorkingSet(resolved));
        }
        else
        {
            yield return Item(pin.Kind is PinKind.Application or PinKind.Script or PinKind.Command ? "Run" : "Open", () => _ = _vm.OpenPinAsync(pin), bold: true);
            if (pin.Kind is PinKind.Application or PinKind.Script)
                yield return Item("Run as administrator", () => Report(ShellLauncher.Launch(pin.Kind, resolved, pin.Arguments, pin.WorkingDirectory, elevated: true)));
            if (pin.Kind != PinKind.Url && pin.Kind != PinKind.Command)
                yield return Item("Show in folder", () =>
                {
                    var dir = Path.GetDirectoryName(resolved);
                    if (dir is not null) _ = _vm.NavigateAsync(dir);
                });
        }
        yield return Item("Copy path", () => TrySetClipboard(resolved));
        yield return null;
        yield return Item("Edit…", () => new PinEditorDialog(pin) { Owner = this }.ShowDialog());
        foreach (var i in CommonNodeItems(pin)) yield return i;
    }

    private IEnumerable<MenuItem?> GroupMenu(PinGroup g)
    {
        foreach (var i in AddPinItems(g)) yield return i;
        yield return null;
        yield return Item("Rename group…", () =>
        {
            if (InputDialog.Ask(this, "Rename group", "Group name:", g.Name) is { Length: > 0 } n) g.Name = n;
        });
        foreach (var i in CommonNodeItems(g)) yield return i;
    }

    private IEnumerable<MenuItem?> CommonNodeItems(PinNode node)
    {
        var ws = _vm.ActiveWorkspace;
        yield return Item("Move up", () => WorkspaceOps.MoveNode(ws, node, -1));
        yield return Item("Move down", () => WorkspaceOps.MoveNode(ws, node, +1));
        var move = new MenuItem { Header = "Move to group" };
        move.Items.Add(Item("(top level)", () => MoveNodeTo(node, null)));
        foreach (var g in PinNode.Flatten(ws.Pins).OfType<PinGroup>().Where(g => g != node && !IsDescendant(g, node)))
            move.Items.Add(Item(g.Name, () => MoveNodeTo(node, g)));
        yield return move;
        yield return null;
        yield return Item(node is PinGroup ? "Remove group" : "Remove pin", () => RemovePin(node));
    }

    private static bool IsDescendant(PinGroup candidate, PinNode ancestor) =>
        ancestor is PinGroup a && PinNode.Flatten(a.Children).Contains(candidate);

    private void MoveNodeTo(PinNode node, PinGroup? group, int index = -1)
    {
        var ws = _vm.ActiveWorkspace;
        if (group is not null && (group == node || IsDescendant(group, node))) return;
        WorkspaceOps.RemoveNode(ws, node);
        var target = group?.Children ?? ws.Pins;
        if (index < 0 || index > target.Count) target.Add(node); else target.Insert(index, node);
        if (group is not null) group.IsExpanded = true;
    }

    private void RemovePin(PinNode node)
    {
        if (node is PinGroup { Children.Count: > 0 } g &&
            MessageBox.Show(this, $"Remove the group “{g.Name}” and the {PinNode.Flatten(g.Children).Count()} pins inside it?\n\nNo files are deleted.",
                "Remove group", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        WorkspaceOps.RemoveNode(_vm.ActiveWorkspace, node);
    }

    private void OnPinDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder<TreeViewItem>(e.OriginalSource) is null) return;
        // Folders already open on single click; double-click runs apps, scripts and files.
        if (SelectedPin is PinItem { Kind: not PinKind.Folder } p) { _ = _vm.OpenPinAsync(p); e.Handled = true; }
    }

    private void OnPinMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && ItemUnder<TreeViewItem>(e.OriginalSource)?.DataContext is PinItem p)
        {
            _ = _vm.OpenPinAsync(p, OpenTarget.NewTab);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.None &&
                 ItemUnder<TreeViewItem>(e.OriginalSource)?.DataContext is PinItem { Kind: PinKind.Folder } folder &&
                 _dragNode is null)
        {
            // Single click navigates folders — the sidebar is a navigator, not a list to manage.
            _ = _vm.OpenPinAsync(folder);
        }
    }

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (SelectedPin is not { } node) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Enter when node is PinItem p: _ = _vm.OpenPinAsync(p, ctrl ? OpenTarget.NewTab : OpenTarget.Current); e.Handled = true; break;
            case Key.Delete: RemovePin(node); e.Handled = true; break;
            case Key.F2:
                if (InputDialog.Ask(this, "Rename", "Name:", node.Name) is { Length: > 0 } n) node.Name = n;
                e.Handled = true; break;
            case Key.Up when ctrl: WorkspaceOps.MoveNode(_vm.ActiveWorkspace, node, -1); e.Handled = true; break;
            case Key.Down when ctrl: WorkspaceOps.MoveNode(_vm.ActiveWorkspace, node, +1); e.Handled = true; break;
        }
    }

    private void SelectTreeItemUnderMouse(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder<TreeViewItem>(e.OriginalSource) is { } item) { item.IsSelected = true; item.Focus(); }
    }

    // Pin drag & drop: reorder pins; drop folders to pin them; drop files onto a folder pin to move/copy them there.
    private void OnPinMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragNode = null;
    }

    private void OnPinMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragNode is not null) return;
        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (ItemUnder<TreeViewItem>(e.OriginalSource)?.DataContext is not PinNode node) return;
        _dragNode = node;
        var data = new DataObject(PinNodeFormat, node.Id);
        if (node is PinItem { Kind: not (PinKind.Url or PinKind.Command) } p && _vm.Resolve(p) is var path && Path.Exists(path))
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
        // Never offer Move: dragging a pin out must not relocate the real folder by accident.
        try { DragDrop.DoDragDrop(PinTree, data, DragDropEffects.Copy | DragDropEffects.Link); }
        finally { _dragNode = null; }
    }

    private void OnPinDragOver(object sender, DragEventArgs e)
    {
        var target = ItemUnder<TreeViewItem>(e.OriginalSource)?.DataContext as PinNode;
        if (e.Data.GetDataPresent(PinNodeFormat)) e.Effects = DragDropEffects.Link; // internal reorder
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = target is PinItem { Kind: PinKind.Folder } ? DragDropHelper.ChooseEffect(e, (string[])e.Data.GetData(DataFormats.FileDrop), _vm.Resolve((PinItem)target)) : DragDropEffects.Link;
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnPinDrop(object sender, DragEventArgs e)
    {
        var target = ItemUnder<TreeViewItem>(e.OriginalSource)?.DataContext as PinNode;
        e.Handled = true;
        if (e.Data.GetData(PinNodeFormat) is string id)
        {
            var node = PinNode.Flatten(_vm.ActiveWorkspace.Pins).FirstOrDefault(n => n.Id == id);
            if (node is null || node == target) return;
            if (target is PinGroup g) MoveNodeTo(node, g);
            else if (target is not null)
            {
                var parent = PinNode.FindParentCollection(_vm.ActiveWorkspace.Pins, target)!;
                var group = PinNode.Flatten(_vm.ActiveWorkspace.Pins).OfType<PinGroup>().FirstOrDefault(x => x.Children == parent);
                WorkspaceOps.RemoveNode(_vm.ActiveWorkspace, node);
                parent.Insert(Math.Max(0, parent.IndexOf(target)), node);
                if (group is not null) group.IsExpanded = true;
            }
            else MoveNodeTo(node, null);
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        if (target is PinItem { Kind: PinKind.Folder } folderPin)
        {
            var dest = _vm.Resolve(folderPin);
            var effect = DragDropHelper.ChooseEffect(e, paths, dest);
            await _vm.TransferAsync(paths, dest, move: effect == DragDropEffects.Move);
        }
        else
        {
            _vm.PinPaths(paths, target as PinGroup);
        }
    }

    // ═══════════════════════ Working set & recents ═══════════════════════

    private static string? LocationPath(object? item) => item switch
    {
        WorkingLocation w => w.Path,
        RecentEntry r => r.Path,
        _ => null,
    };

    private string? MenuLocation(object sender) =>
        LocationPath(((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is ListBox lb ? lb.SelectedItem : null);

    private void OnLocationDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder<ListBoxItem>(e.OriginalSource) is null) return;
        if (LocationPath(((ListBox)sender).SelectedItem) is { } p) _ = _vm.NavigateAsync(p);
    }

    private void OnLocationMouseUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemUnder<ListBoxItem>(e.OriginalSource);
        if (item is null || LocationPath(item.DataContext) is not { } p) return;
        if (e.ChangedButton == MouseButton.Middle) { _ = _vm.NavigateAsync(p, OpenTarget.NewTab); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.None) _ = _vm.NavigateAsync(p);
    }

    private void OnLocationKeyDown(object sender, KeyEventArgs e)
    {
        var lb = (ListBox)sender;
        if (e.Key == Key.Enter && LocationPath(lb.SelectedItem) is { } p)
        {
            _ = _vm.NavigateAsync(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? OpenTarget.NewTab : OpenTarget.Current);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && lb.SelectedItem is WorkingLocation wl)
        {
            _vm.RemoveWorkingLocationCommand.Execute(wl);
            e.Handled = true;
        }
    }

    private void OnLocationOpen(object sender, RoutedEventArgs e) { if (MenuLocation(sender) is { } p) _ = _vm.NavigateAsync(p); }
    private void OnLocationOpenNewTab(object sender, RoutedEventArgs e) { if (MenuLocation(sender) is { } p) _ = _vm.NavigateAsync(p, OpenTarget.NewTab); }
    private void OnLocationOpenExplorer(object sender, RoutedEventArgs e) { if (MenuLocation(sender) is { } p) Report(ShellLauncher.OpenInExplorer(p)); }
    private void OnRecentPin(object sender, RoutedEventArgs e) { if (MenuLocation(sender) is { } p) _vm.PinPaths(new[] { p }); }
    private void OnRecentToWorkingSet(object sender, RoutedEventArgs e) { if (MenuLocation(sender) is { } p) _vm.AddToWorkingSet(p); }
    private void OnWorkingPromote(object sender, RoutedEventArgs e) => _vm.PromoteWorkingLocationCommand.Execute(WorkingList.SelectedItem as WorkingLocation);
    private void OnWorkingRemove(object sender, RoutedEventArgs e) => _vm.RemoveWorkingLocationCommand.Execute(WorkingList.SelectedItem as WorkingLocation);

    private void OnWorkingRename(object sender, RoutedEventArgs e)
    {
        if (WorkingList.SelectedItem is not WorkingLocation wl) return;
        var label = InputDialog.Ask(this, "Working location label", "Label (leave empty to show the path):", wl.Label ?? Path.GetFileName(wl.Path.TrimEnd('\\')));
        if (label is not null) wl.Label = label.Length == 0 ? null : label;
    }

    private void OnWorkingSetDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWorkingSetDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var p in paths)
            _vm.AddToWorkingSet(Directory.Exists(p) ? p : Path.GetDirectoryName(p) ?? p);
        e.Handled = true;
    }

    // ═══════════════════════ Helpers ═══════════════════════

    /// <summary>Lets nested lists scroll the sidebar instead of swallowing the wheel.</summary>
    private void OnNestedMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        SidebarScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
    }

    internal static T? ItemUnder<T>(object? source) where T : DependencyObject
    {
        var d = source as DependencyObject;
        while (d is not null and not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    internal static MenuItem Item(string header, Action action, bool enabled = true, bool bold = false)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        if (bold) mi.FontWeight = FontWeights.SemiBold;
        mi.Click += (_, _) => action();
        return mi;
    }

    internal static void ShowMenu(FrameworkElement anchor, params MenuItem?[] items)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            FontFamily = SystemFonts.MessageFontFamily,
        };
        foreach (var i in items) menu.Items.Add(i is null ? new Separator() : i);
        menu.IsOpen = true;
    }

    private void Report(string? error)
    {
        if (error is not null) _vm.ShowStatus(error);
    }

    internal void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); _vm.ShowStatus("Copied path"); }
        catch (System.Runtime.InteropServices.COMException) { _vm.ShowStatus("Clipboard is busy — try again."); }
    }
}
