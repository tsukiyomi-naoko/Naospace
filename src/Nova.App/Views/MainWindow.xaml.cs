using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Nova.App.Dialogs;
using Nova.App.Services;
using Nova.App.ViewModels;
using Nova.Core.Detection;
using Nova.Core.Diagnostics;
using Nova.Core.Input;
using Nova.Core.IO;
using Nova.Core.Model;
using Nova.Core.Persistence;
using Nova.Windows.Launching;
using Nova.Windows.Shell;
using Nova.Windows.Windowing;

namespace Nova.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Workspaces whose native file views stay alive after switching away (most recently used first).</summary>
    private const int LiveWorkspaceViews = 4;

    private readonly MainViewModel _vm;
    private readonly Dictionary<WorkspaceViewModel, WorkspaceBrowserView> _views = new();
    private readonly LinkedList<WorkspaceViewModel> _viewLru = new();
    private GlobalHotkey? _hotkey;
    private ForegroundMonitor? _foreground;
    private Point _dragStart;
    private PinNodeViewModel? _dragCandidate;
    private bool _dragging;
    private bool _exiting;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Modes = new WindowModeController(this);
        vm.ActiveWorkspaceChanged += (_, _) => ShowActiveWorkspaceView();
        vm.ActiveTabChanged += (_, _) => UpdateBrowserChrome();
        vm.PropertyChanged += OnVmPropertyChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Deactivated += (_, _) => Modes.OnDeactivated();
        LocationChanged += (_, _) => Modes.CapturePlacement();
        SizeChanged += (_, _) => Modes.CapturePlacement();
        StateChanged += (_, _) => Modes.CapturePlacement();
        Loaded += (_, _) => { ShowActiveWorkspaceView(); UpdateBrowserChrome(); };
        PinTree.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnPinMouseUp), handledEventsToo: true);
    }

    public WindowModeController Modes { get; }

    /// <summary>Set by the application before closing for real (instead of hiding to the tray).</summary>
    public void PrepareExit() => _exiting = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.ApplyTo(this);
        Modes.Initialize();

        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
        _hotkey = new GlobalHotkey(source);
        _hotkey.Pressed += (_, _) => ToggleVisibility();
        RegisterHotkey();

        _foreground = new ForegroundMonitor(Dispatcher);
        _foreground.ForegroundChanged += (_, ev) =>
        {
            if (AppServices.Settings.AutoDetectionEnabled) _vm.OnForegroundChanged(ev.Info);
        };
        if (AppServices.Settings.AutoDetectionEnabled) _foreground.Start();
    }

    public void RegisterHotkey()
    {
        if (_hotkey == null) return;
        if (!HotkeyGesture.TryParse(AppServices.Settings.SummonHotkey, out var g) || g == null)
        {
            AppServices.Status($"Invalid hotkey '{AppServices.Settings.SummonHotkey}'.", StatusKind.Warning);
            return;
        }
        if (!_hotkey.Register(g))
            AppServices.Status($"The hotkey {g} is already used by another application. Choose another one in Settings.", StatusKind.Warning);
    }

    public void ApplyDetectionSetting()
    {
        AppServices.AutoSwitch.Enabled = AppServices.Settings.AutoDetectionEnabled;
        if (AppServices.Settings.AutoDetectionEnabled) _foreground?.Start();
        else _foreground?.Stop();
    }

    // ================= Visibility =================

    public void ToggleVisibility()
    {
        if (IsVisible && IsActive && WindowState != WindowState.Minimized) HideToBackground();
        else ShowAndActivate();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        Modes.OnShown();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (!Modes.Compact && _vm.ActiveTab?.Host is { } host) host.FocusView();
        else PinTree.Focus();
    }

    public void HideToBackground()
    {
        Modes.CapturePlacement();
        Modes.OnHiding();
        Hide();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && AppServices.Settings.CloseToTray)
        {
            e.Cancel = true;
            HideToBackground();
            return;
        }
        Modes.CapturePlacement();
        Modes.Dispose();
        _hotkey?.Dispose();
        _foreground?.Dispose();
        foreach (var v in _views.Values) v.DisposeAll();
        _views.Clear();
    }

    // ================= Workspace views =================

    private void ShowActiveWorkspaceView()
    {
        var ws = _vm.ActiveWorkspace;
        if (ws == null) return;
        if (!_views.TryGetValue(ws, out var view))
        {
            view = new WorkspaceBrowserView(ws, _vm, HandleShortcutFromView);
            _views[ws] = view;
            BrowserHostArea.Children.Add(view);
        }
        foreach (var (w, v) in _views) v.Visibility = ReferenceEquals(w, ws) ? Visibility.Visible : Visibility.Collapsed;

        _viewLru.Remove(ws);
        _viewLru.AddFirst(ws);
        while (_viewLru.Count > LiveWorkspaceViews)
        {
            var old = _viewLru.Last!.Value;
            _viewLru.RemoveLast();
            if (_views.Remove(old, out var oldView))
            {
                oldView.DisposeAll();
                BrowserHostArea.Children.Remove(oldView);
            }
        }
        // Drop views of deleted workspaces.
        foreach (var gone in _views.Keys.Where(k => !_vm.Workspaces.Contains(k)).ToList())
        {
            _viewLru.Remove(gone);
            _views[gone].DisposeAll();
            BrowserHostArea.Children.Remove(_views[gone]);
            _views.Remove(gone);
        }
        UpdateBrowserChrome();
    }

    /// <summary>Destroys every native view (used after restoring a backup).</summary>
    public void ResetAllViews()
    {
        foreach (var v in _views.Values) v.DisposeAll();
        _views.Clear();
        _viewLru.Clear();
        BrowserHostArea.Children.Clear();
        ShowActiveWorkspaceView();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusKind))
        {
            StatusTextBlock.SetResourceReference(TextBlock.ForegroundProperty, _vm.StatusKind switch
            {
                StatusKind.Error => "Nova.Error",
                StatusKind.Warning => "Nova.Warning",
                _ => "Nova.Foreground",
            });
        }
    }

    private void UpdateBrowserChrome()
    {
        var tab = _vm.ActiveTab;
        BackButton.IsEnabled = tab?.CanGoBack == true;
        ForwardButton.IsEnabled = tab?.CanGoForward == true;
        UpButton.IsEnabled = tab?.CanGoUp == true;
        DualPaneToggle.IsChecked = _vm.ActiveWorkspace?.DualPane == true;
        if (AddressBox.Visibility != Visibility.Visible)
        {
            Breadcrumbs.ItemsSource = PathSegments.Split(tab?.Path, tab?.Title);
            BreadcrumbScroll.ScrollToRightEnd();
        }
        Title = tab?.Path is { } p ? $"{TabViewModel.DeriveTitle(p)} – {_vm.ActiveWorkspace?.Name} – NOVA Workspace" : "NOVA Workspace";
    }

    // ================= Keyboard =================

    private bool HandleShortcutFromView(Key key, ModifierKeys mods) => HandleShortcut(key, mods, fromView: true);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (Keyboard.FocusedElement is TextBox && (mods & (ModifierKeys.Control | ModifierKeys.Alt)) == 0) return;
        if (Keyboard.FocusedElement is TextBox && key is Key.Left or Key.Right && mods == ModifierKeys.Control) return;
        if (HandleShortcut(key, mods, fromView: false)) e.Handled = true;
    }

    /// <summary>Application shortcuts, consistent whether focus is in NOVA's own controls or inside the native file view.</summary>
    private bool HandleShortcut(Key key, ModifierKeys mods, bool fromView)
    {
        var ws = _vm.ActiveWorkspace;
        var tab = _vm.ActiveTab;
        const ModifierKeys Ctrl = ModifierKeys.Control, Alt = ModifierKeys.Alt, Shift = ModifierKeys.Shift, None = ModifierKeys.None;

        switch (key)
        {
            case Key.Left when mods == Alt: tab?.GoBack(); return true;
            case Key.Right when mods == Alt: tab?.GoForward(); return true;
            case Key.Up when mods == Alt: tab?.GoUp(); return true;
            case Key.Back when mods == None && fromView: tab?.GoBack(); return true;
            case Key.BrowserBack: tab?.GoBack(); return true;
            case Key.BrowserForward: tab?.GoForward(); return true;
            case Key.F5 when mods == None: tab?.Refresh(); return tab != null;
            case Key.F5 when mods == Shift: _ = TransferToOtherPane(FileOperationKind.Copy); return true;
            case Key.F6 when mods == Shift: _ = TransferToOtherPane(FileOperationKind.Move); return true;
            case Key.F6 when mods == None && ws is { DualPane: true }:
                ws.ActivePaneIndex = ws.ActivePaneIndex == 0 ? 1 : 0;
                ws.ActiveTab?.Host?.FocusView();
                return true;
            case Key.T when mods == Ctrl: EnsureBrowserVisible(); _vm.NewTab(); return true;
            case Key.W when mods == Ctrl: if (tab != null) CloseTab(tab); return tab != null;
            case Key.Tab when mods == Ctrl: ws?.ActivePane.SelectRelative(1); return ws != null;
            case Key.Tab when mods == (Ctrl | Shift): ws?.ActivePane.SelectRelative(-1); return ws != null;
            case Key.L when mods == Ctrl:
            case Key.D when mods == Alt:
                EnsureBrowserVisible(); BeginAddressEdit(); return true;
            case Key.D when mods == (Ctrl | Shift): if (ws != null) ws.DualPane = !ws.DualPane; UpdateBrowserChrome(); return true;
            case Key.B when mods == Ctrl: SetCompact(!Modes.Compact); return true;
            case Key.P when mods == (Ctrl | Shift): PinCurrentFolder(); return true;
            case Key.N when mods == (Ctrl | Shift) && !Modes.Compact: Commands.TryRun(NewFolder); return true;
            case >= Key.D1 and <= Key.D9 when mods == Ctrl:
                var i = key - Key.D1;
                if (i < _vm.Workspaces.Count) _vm.ActiveWorkspace = _vm.Workspaces[i];
                return true;
            case Key.Escape when !fromView && AddressBox.Visibility == Visibility.Visible:
                EndAddressEdit(); return true;
        }
        return false;
    }

    private void CloseTab(TabViewModel tab)
    {
        var view = _vm.ActiveWorkspace != null && _views.TryGetValue(_vm.ActiveWorkspace, out var v) ? v.ViewFor(tab.Pane) : null;
        if (view != null) view.CloseTab(tab);
        else tab.Pane.Remove(tab);
    }

    // ================= Address bar =================

    private void BeginAddressEdit()
    {
        AddressBox.Text = _vm.ActiveTab?.Path ?? string.Empty;
        AddressBox.Visibility = Visibility.Visible;
        BreadcrumbBorder.Visibility = Visibility.Hidden;
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void EndAddressEdit()
    {
        AddressBox.Visibility = Visibility.Collapsed;
        BreadcrumbBorder.Visibility = Visibility.Visible;
        UpdateBrowserChrome();
    }

    private void OnBreadcrumbAreaClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) != null) return;
        BeginAddressEdit();
    }

    private void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path }) NavigateActive(path);
        e.Handled = true;
    }

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = AddressBox.Text;
            EndAddressEdit();
            NavigateActive(text);
            _vm.ActiveTab?.Host?.FocusView();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndAddressEdit();
            e.Handled = true;
        }
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (AddressBox.Visibility == Visibility.Visible) EndAddressEdit();
    }

    private async void NavigateActive(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        // A typed file path opens the file (like Explorer's address bar); the check runs off the UI thread.
        if (!PathProbe.IsVirtual(p) && await Task.Run(() => File.Exists(p)))
        {
            Commands.TryRun(() => Launcher.ShellOpen(p, null, Path.GetDirectoryName(p)));
            return;
        }
        _vm.OpenFolder(p, OpenTarget.CurrentTab);
    }

    // ================= Browser toolbar =================

    private void OnBackClick(object sender, RoutedEventArgs e) => _vm.ActiveTab?.GoBack();
    private void OnForwardClick(object sender, RoutedEventArgs e) => _vm.ActiveTab?.GoForward();
    private void OnUpClick(object sender, RoutedEventArgs e) => _vm.ActiveTab?.GoUp();
    private void OnRefreshClick(object sender, RoutedEventArgs e) => _vm.ActiveTab?.Refresh();
    private void OnNewFolderClick(object sender, RoutedEventArgs e) => NewFolder();

    /// <summary>Creates a folder through the view's own "New folder" command (which starts in-place rename);
    /// falls back to creating a uniquely named folder if that command is unavailable.</summary>
    private void NewFolder()
    {
        var tab = _vm.ActiveTab;
        var host = tab?.Host;
        if (host == null || tab?.Path == null) { AppServices.Status("Open a folder first."); return; }
        if (host.InvokeVerb("NewFolder", background: true)) { host.FocusView(); return; }
        if (PathProbe.IsVirtual(tab.Path)) { AppServices.Status("A folder cannot be created here.", StatusKind.Info); return; }
        var name = "New folder";
        var candidate = Path.Combine(tab.Path, name);
        for (var i = 2; Directory.Exists(candidate) || File.Exists(candidate); i++) candidate = Path.Combine(tab.Path, $"{name} ({i})");
        Directory.CreateDirectory(candidate);
        host.Refresh();
        host.FocusView();
        AppServices.Status($"Created \"{Path.GetFileName(candidate)}\". Select it and press F2 to rename.");
    }
    private void OnCutClick(object sender, RoutedEventArgs e) => InvokeVerb("cut", background: false);
    private void OnCopyClick(object sender, RoutedEventArgs e) => InvokeVerb("copy", background: false);
    private void OnPasteClick(object sender, RoutedEventArgs e) => InvokeVerb("paste", background: true);
    private void OnRenameClick(object sender, RoutedEventArgs e) => InvokeVerb("rename", background: false);
    private void OnDeleteClick(object sender, RoutedEventArgs e) => InvokeVerb("delete", background: false);
    private void OnCopyToOtherClick(object sender, RoutedEventArgs e) => _ = TransferToOtherPane(FileOperationKind.Copy);
    private void OnMoveToOtherClick(object sender, RoutedEventArgs e) => _ = TransferToOtherPane(FileOperationKind.Move);

    private void OnDualPaneClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveWorkspace is { } ws) ws.DualPane = DualPaneToggle.IsChecked == true;
        UpdateBrowserChrome();
    }

    private void InvokeVerb(string verb, bool background)
    {
        var host = _vm.ActiveTab?.Host;
        if (host == null) { AppServices.Status("Open a folder first.", StatusKind.Info); return; }
        if (!background && host.GetSelection().Count == 0) { AppServices.Status("Select one or more items first.", StatusKind.Info); return; }
        if (!host.InvokeVerb(verb, background))
            AppServices.Status($"'{verb}' is not available here.", StatusKind.Info);
        host.FocusView();
    }

    /// <summary>Copies or moves the active pane's selection into the other pane's folder via the Windows copy engine.</summary>
    private async Task TransferToOtherPane(FileOperationKind kind)
    {
        var ws = _vm.ActiveWorkspace;
        var source = ws?.ActiveTab?.Host;
        if (ws == null || source == null) return;
        if (!ws.DualPane)
        {
            ws.DualPane = true;
            UpdateBrowserChrome();
            AppServices.Status("Dual pane enabled: open the destination folder in the second pane, then repeat.", StatusKind.Info);
            return;
        }
        var destTab = ws.OtherPane.ActiveTab;
        var dest = destTab?.Path;
        if (dest == null || PathProbe.IsVirtual(dest) || !Directory.Exists(dest))
        {
            AppServices.Status("The other pane must show a file-system folder to copy or move into.", StatusKind.Warning);
            return;
        }
        var items = source.GetSelection().Where(s => s.IsFileSystem && !string.IsNullOrEmpty(s.Path)).ToList();
        if (items.Count == 0)
        {
            AppServices.Status("Select the files or folders to " + (kind == FileOperationKind.Copy ? "copy" : "move") + " first.", StatusKind.Info);
            return;
        }
        if (items.Any(i => PathComparer.Equal(Path.GetDirectoryName(i.Path), dest)))
        {
            AppServices.Status("Source and destination are the same folder.", StatusKind.Warning);
            return;
        }

        var verb = kind == FileOperationKind.Copy ? "Copying" : "Moving";
        AppServices.Status($"{verb} {items.Count} item(s) to {dest}…");
        var hwnd = new WindowInteropHelper(this).Handle;
        var result = await FileOperations.RunAsync(kind, items.Select(i => i.Path).ToList(), dest, hwnd);
        if (result.Cancelled) AppServices.Status("Operation cancelled.", StatusKind.Info);
        else if (result.Error != null) AppServices.Status($"{verb} failed: {result.Error}", StatusKind.Error);
        else if (result.AnyAborted) AppServices.Status("Some items were skipped or the operation was stopped.", StatusKind.Warning);
        else AppServices.Status($"{(kind == FileOperationKind.Copy ? "Copied" : "Moved")} {items.Count} item(s) to {dest}.");
        destTab?.Refresh();
        source.Refresh();
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        var tab = _vm.ActiveTab;
        var path = tab?.Path;
        var fs = path != null && !PathProbe.IsVirtual(path);
        Menus.Show(MoreButton,
            Menus.Item("Open selected folder(s) in new tab", OpenSelectionInNewTabs, enabled: tab?.Host != null),
            Menus.Item("Pin selected item(s) to workspace", PinSelection, enabled: tab?.Host != null),
            Menus.Item("Pin current folder", PinCurrentFolder, "Ctrl+Shift+P", enabled: path != null),
            Menus.Item("Add current folder to working locations", AddCurrentToWorking, enabled: path != null),
            new Separator(),
            Menus.Item("Open current folder in Windows Explorer", () => Launcher.OpenInExplorer(path!), enabled: path != null),
            Menus.Item("Open terminal here", () => Launcher.OpenTerminal(path!), enabled: fs),
            Menus.Item("Copy current path", () => Clipboard.SetText(path!), enabled: path != null),
            Menus.Item("Properties of selection", () => InvokeVerb("properties", false), enabled: tab?.Host != null),
            new Separator(),
            Menus.Item("Open Downloads here", () => _vm.OpenFolder(ShellNative.DownloadsFolder, OpenTarget.CurrentTab)),
            Menus.Item("Open Downloads in other pane", () => _vm.OpenFolder(ShellNative.DownloadsFolder, OpenTarget.OtherPane)));
    }

    private void OpenSelectionInNewTabs()
    {
        var host = _vm.ActiveTab?.Host;
        if (host == null) return;
        var folders = host.GetSelection().Where(s => s.IsFolder).ToList();
        if (folders.Count == 0) { AppServices.Status("Select one or more folders first."); return; }
        foreach (var f in folders) _vm.OpenFolder(f.Path, OpenTarget.NewTab);
    }

    private void PinSelection()
    {
        var host = _vm.ActiveTab?.Host;
        var ws = _vm.ActiveWorkspace;
        if (host == null || ws == null) return;
        var sel = host.GetSelection().Where(s => !string.IsNullOrEmpty(s.Path)).ToList();
        if (sel.Count == 0) { AppServices.Status("Select items to pin first."); return; }
        foreach (var s in sel) ws.AddPin(PinFactory.FromPath(s.Path, s.IsFolder));
        AppServices.Status($"Pinned {sel.Count} item(s).");
    }

    private void PinCurrentFolder()
    {
        if (_vm.ActiveTab?.Path is { } p && _vm.ActiveWorkspace is { } ws)
        {
            ws.AddPin(PinNode.Folder(p));
            AppServices.Status($"Pinned {TabViewModel.DeriveTitle(p)}.");
        }
    }

    private void AddCurrentToWorking()
    {
        if (_vm.ActiveTab?.Path is { } p && _vm.ActiveWorkspace is { } ws)
            AppServices.Status(ws.AddWorkingLocation(p) ? "Added to working locations." : "Already in working locations.");
    }

    // ================= Sidebar: pins =================

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private static PinNodeViewModel? PinFromSource(object source) =>
        FindAncestor<TreeViewItem>(source as DependencyObject)?.DataContext as PinNodeViewModel;

    private void OnPinPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(PinTree);
        _dragCandidate = FindAncestor<System.Windows.Controls.Primitives.ToggleButton>(e.OriginalSource as DependencyObject) == null
            ? PinFromSource(e.OriginalSource)
            : null;
        _dragging = false;
        _clickCount = e.ClickCount;
    }

    private int _clickCount;

    private void OnPinMouseUp(object sender, MouseButtonEventArgs e)
    {
        var candidate = _dragCandidate;
        _dragCandidate = null;
        if (_dragging || candidate == null || _clickCount > 1) return;
        if (!ReferenceEquals(PinFromSource(e.OriginalSource), candidate)) return;
        // Single click on a folder navigates inside NOVA (like Explorer's navigation pane).
        if (candidate.IsFolder) OpenPin(candidate, OpenTarget.CurrentTab);
    }

    private void OnPinPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate == null || _dragging) return;
        var p = e.GetPosition(PinTree);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        // Internal format only. Deliberately NO FileDrop payload: dropping a pin into a file view must never be able to
        // move or copy the real folder it points to.
        var data = new DataObject(PinDragFormat, _dragCandidate.Id);
        DragDrop.DoDragDrop(PinTree, data, DragDropEffects.Move);
        _dragCandidate = null;
    }

    private const string PinDragFormat = "NovaWorkspace.PinId";

    private void OnPinPreviewMouseAnyDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && PinFromSource(e.OriginalSource) is { IsFolder: true } pin)
        {
            OpenPin(pin, OpenTarget.NewTab);
            e.Handled = true;
        }
    }

    private void OnPinDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PinFromSource(e.OriginalSource) is { } pin && !pin.IsGroup && !pin.IsFolder)
        {
            OpenPin(pin, OpenTarget.CurrentTab);
            e.Handled = true;
        }
    }

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (PinTree.SelectedItem is not PinNodeViewModel pin) return;
        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control:
                if (pin.IsFolder) OpenPin(pin, OpenTarget.NewTab);
                e.Handled = true;
                break;
            case Key.Enter:
                if (!pin.IsGroup) OpenPin(pin, OpenTarget.CurrentTab);
                e.Handled = true;
                break;
            case Key.F2:
                EditPin(pin);
                e.Handled = true;
                break;
            case Key.Delete:
                RemovePin(pin);
                e.Handled = true;
                break;
        }
    }

    private void OnPinRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void OnPinContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var ws = _vm.ActiveWorkspace;
        var menu = PinTree.ContextMenu!;
        menu.Items.Clear();
        if (ws == null) { e.Handled = true; return; }
        var pin = PinFromSource(e.OriginalSource);
        if (pin == null)
        {
            foreach (var i in BuildAddMenuItems(null)) menu.Items.Add(i);
            return;
        }

        if (pin.IsFolder)
        {
            menu.Items.Add(Menus.Item("Open", () => OpenPin(pin, OpenTarget.CurrentTab), "Click"));
            menu.Items.Add(Menus.Item("Open in new tab", () => OpenPin(pin, OpenTarget.NewTab), "Middle-click"));
            menu.Items.Add(Menus.Item("Open in other pane", () => OpenPin(pin, OpenTarget.OtherPane)));
            menu.Items.Add(Menus.Item("Open in Windows Explorer", () => Launcher.OpenInExplorer(pin.Target)));
            menu.Items.Add(Menus.Item("Open terminal here", () => Launcher.OpenTerminal(pin.Target)));
            menu.Items.Add(Menus.Item("Add to working locations", () => ws.AddWorkingLocation(pin.Target)));
        }
        else if (!pin.IsGroup)
        {
            menu.Items.Add(Menus.Item(pin.Kind is PinKind.Application or PinKind.Script ? "Launch" : "Open", () => OpenPin(pin, OpenTarget.CurrentTab), "Enter"));
            if (pin.Kind != PinKind.Url)
            {
                menu.Items.Add(Menus.Item("Show containing folder in NOVA", () =>
                {
                    var dir = Path.GetDirectoryName(pin.Target);
                    if (dir != null) _vm.OpenFolder(dir, OpenTarget.CurrentTab);
                }));
                menu.Items.Add(Menus.Item("Show in Windows Explorer", () => Launcher.OpenInExplorer(pin.Target)));
            }
        }
        if (pin.IsGroup)
        {
            foreach (var i in BuildAddMenuItems(pin)) menu.Items.Add(i);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Menus.Item(pin.IsGroup ? "Rename group…" : "Edit…", () => EditPin(pin), "F2"));
        menu.Items.Add(Menus.Item("Move up", () => MovePinBy(pin, -1)));
        menu.Items.Add(Menus.Item("Move down", () => MovePinBy(pin, +1)));
        menu.Items.Add(Menus.Item("Remove from workspace", () => RemovePin(pin), "Del"));
    }

    private void MovePinBy(PinNodeViewModel pin, int delta)
    {
        var ws = _vm.ActiveWorkspace;
        if (ws == null) return;
        var list = PinTree_FindList(ws, pin);
        if (list == null) return;
        var i = list.FindIndex(n => n.Id == pin.Id);
        var j = i + delta;
        if (j < 0 || j >= list.Count) return;
        ws.MovePin(pin.Id, list[j].Id, intoGroup: false, after: delta > 0);
    }

    private static List<PinNode>? PinTree_FindList(WorkspaceViewModel ws, PinNodeViewModel pin) =>
        Nova.Core.Model.PinTree.FindParentList(ws.Definition.Pins, pin.Id);

    private IEnumerable<object> BuildAddMenuItems(PinNodeViewModel? group)
    {
        var where = group == null ? string.Empty : $" to \"{group.Label}\"";
        yield return Menus.Item($"Add folder{where}…", () => AddFolders(group));
        yield return Menus.Item($"Add file{where}…", () => AddFile(group, PinKind.File));
        yield return Menus.Item($"Add application / tool{where}…", () => AddFile(group, PinKind.Application));
        yield return Menus.Item($"Add script{where}…", () => AddFile(group, PinKind.Script));
        yield return Menus.Item($"Add URL{where}…", () => AddUrl(group));
        yield return Menus.Item($"Add group{where}…", () => AddGroup(group));
        yield return new Separator();
        yield return Menus.Item("Add current browser folder", () =>
        {
            if (_vm.ActiveTab?.Path is { } p) _vm.ActiveWorkspace?.AddPin(PinNode.Folder(p), group);
        }, enabled: _vm.ActiveTab?.Path != null);
        yield return Menus.Item("Add Downloads", () => _vm.ActiveWorkspace?.AddPin(PinNode.Folder(ShellNative.DownloadsFolder, "Downloads"), group));
        yield return Menus.Item("Capture open Explorer location…", () => CaptureExplorerLocation(path => _vm.ActiveWorkspace?.AddPin(PinNode.Folder(path), group)));
    }

    private void OnAddPinClick(object sender, RoutedEventArgs e) => Menus.Show(AddPinButton, BuildAddMenuItems(null).ToArray());

    private void AddFolders(PinNodeViewModel? group)
    {
        var dlg = new OpenFolderDialog { Multiselect = true, Title = "Pin folders to this workspace" };
        if (_vm.ActiveTab?.Path is { } p && Directory.Exists(p)) dlg.InitialDirectory = p;
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FolderNames) _vm.ActiveWorkspace?.AddPin(PinNode.Folder(f), group);
    }

    private void AddFile(PinNodeViewModel? group, PinKind kind)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = kind == PinKind.File,
            Title = kind switch { PinKind.Application => "Choose an application or tool", PinKind.Script => "Choose a script", _ => "Choose files to pin" },
            Filter = kind switch
            {
                PinKind.Application => "Programs|*.exe;*.lnk;*.com|All files|*.*",
                PinKind.Script => "Scripts|*.ps1;*.bat;*.cmd;*.py;*.sh|All files|*.*",
                _ => "All files|*.*",
            },
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames)
        {
            var node = PinFactory.FromPath(f, isFolder: false);
            node.Kind = kind;
            _vm.ActiveWorkspace?.AddPin(node, group);
        }
    }

    private void AddUrl(PinNodeViewModel? group)
    {
        var dlg = new PinEditorWindow(new PinNode { Kind = PinKind.Url, Target = "https://" }) { Owner = this };
        if (dlg.ShowDialog() == true) _vm.ActiveWorkspace?.AddPin(dlg.Result, group);
    }

    private void AddGroup(PinNodeViewModel? parent)
    {
        var name = TextPromptWindow.Ask(this, "New group", "Group name:", "New group");
        if (!string.IsNullOrWhiteSpace(name)) _vm.ActiveWorkspace?.AddPin(PinNode.Group(name.Trim()), parent);
    }

    private void EditPin(PinNodeViewModel pin)
    {
        if (pin.IsGroup)
        {
            var name = TextPromptWindow.Ask(this, "Rename group", "Group name:", pin.Label);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var copy = pin.Model.DeepClone();
                copy.Label = name.Trim();
                _vm.ActiveWorkspace?.ReplacePin(pin, copy);
            }
            return;
        }
        var dlg = new PinEditorWindow(pin.Model.DeepClone()) { Owner = this };
        if (dlg.ShowDialog() == true) _vm.ActiveWorkspace?.ReplacePin(pin, dlg.Result);
    }

    private void RemovePin(PinNodeViewModel pin)
    {
        var what = pin.IsGroup && pin.Children.Count > 0 ? $"the group \"{pin.Label}\" and its {pin.Model.Children.Count} item(s)" : $"\"{pin.Label}\"";
        if (MessageBox.Show(this, $"Remove {what} from this workspace?\n\nOnly the shortcut is removed; no files are deleted.",
                "Remove pin", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            _vm.ActiveWorkspace?.RemovePin(pin);
    }

    private void OpenPin(PinNodeViewModel pin, OpenTarget target)
    {
        if (pin.IsGroup) return;
        if (pin.IsFolder)
        {
            if (AppServices.Settings.ExpandBrowserWhenOpeningFolder) EnsureBrowserVisible();
            _vm.OpenFolder(pin.Target, target);
            return;
        }
        Commands.TryRun(() =>
        {
            Launcher.Launch(pin.Model);
            if (pin.Kind == PinKind.File) { _vm.ActiveWorkspace?.State.RecentFiles.Touch(pin.Target); _vm.ActiveWorkspace?.SaveState(); }
            AppServices.Status($"Opened {pin.Label}.");
        });
    }

    private void OnPinDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PinDragFormat)) e.Effects = DragDropEffects.Move;
        // External files/folders become shortcuts. Never report Move: the drag source must not delete anything.
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy;
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPinDrop(object sender, DragEventArgs e)
    {
        var ws = _vm.ActiveWorkspace;
        if (ws == null) return;
        var target = PinFromSource(e.OriginalSource);
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        var lowerHalf = item != null && e.GetPosition(item).Y > Math.Min(item.ActualHeight, 24) / 2;

        if (e.Data.GetData(PinDragFormat) is string id)
        {
            ws.MovePin(id, target?.Id, intoGroup: target?.IsGroup == true, after: lowerHalf);
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            var group = target?.IsGroup == true ? target : target?.Parent;
            foreach (var p in paths) ws.AddPin(PinFactory.FromPath(p, Directory.Exists(p)), group);
            AppServices.Status($"Pinned {paths.Length} item(s).");
            e.Effects = (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    // ================= Sidebar: working locations & recents =================

    private void OnLocationListMouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var path = item?.DataContext switch
        {
            WorkingLocation w => w.Path,
            RecentItem r => r.Path,
            _ => null,
        };
        if (path == null) return;
        if (e.ChangedButton == MouseButton.Left)
        {
            EnsureBrowserVisible();
            _vm.OpenFolder(path, Keyboard.Modifiers == ModifierKeys.Control ? OpenTarget.NewTab : OpenTarget.CurrentTab);
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            EnsureBrowserVisible();
            _vm.OpenFolder(path, OpenTarget.NewTab);
            e.Handled = true;
        }
    }

    private void OnLocationListKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBox list) return;
        var path = list.SelectedItem switch { WorkingLocation w => w.Path, RecentItem r => r.Path, _ => null };
        if (path == null) return;
        if (e.Key == Key.Enter)
        {
            EnsureBrowserVisible();
            _vm.OpenFolder(path, Keyboard.Modifiers == ModifierKeys.Control ? OpenTarget.NewTab : OpenTarget.CurrentTab);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && list.SelectedItem is WorkingLocation w)
        {
            _vm.ActiveWorkspace?.RemoveWorkingLocation(w);
            e.Handled = true;
        }
    }

    private void OnWorkingContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var ws = _vm.ActiveWorkspace;
        var menu = WorkingList.ContextMenu!;
        menu.Items.Clear();
        if (ws == null) return;
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is WorkingLocation w)
        {
            menu.Items.Add(Menus.Item("Open in new tab", () => _vm.OpenFolder(w.Path, OpenTarget.NewTab)));
            menu.Items.Add(Menus.Item("Open in other pane", () => _vm.OpenFolder(w.Path, OpenTarget.OtherPane)));
            menu.Items.Add(Menus.Item("Pin permanently", () => { ws.AddPin(PinNode.Folder(w.Path, w.Label)); ws.RemoveWorkingLocation(w); }));
            menu.Items.Add(Menus.Item("Open in Windows Explorer", () => Launcher.OpenInExplorer(w.Path)));
            menu.Items.Add(Menus.Item("Remove", () => ws.RemoveWorkingLocation(w), "Del"));
            menu.Items.Add(new Separator());
        }
        foreach (var i in BuildWorkingMenu()) menu.Items.Add(i);
    }

    private IEnumerable<object> BuildWorkingMenu()
    {
        var ws = _vm.ActiveWorkspace;
        yield return Menus.Item("Add current folder", AddCurrentToWorking, enabled: _vm.ActiveTab?.Path != null);
        yield return Menus.Item("Add all open tabs", () =>
        {
            if (ws == null) return;
            var n = ws.Panes.SelectMany(p => p.Tabs).Select(t => t.Path).Where(p => p != null).Count(p => ws.AddWorkingLocation(p!));
            AppServices.Status($"Added {n} location(s).");
        });
        yield return Menus.Item("Add folder…", () =>
        {
            var dlg = new OpenFolderDialog { Multiselect = true };
            if (dlg.ShowDialog(this) == true) foreach (var f in dlg.FolderNames) ws?.AddWorkingLocation(f);
        });
        yield return Menus.Item("Capture open Explorer location…", () => CaptureExplorerLocation(p => ws?.AddWorkingLocation(p)));
        yield return new Separator();
        yield return Menus.Item("Clear working locations", () =>
        {
            if (MessageBox.Show(this, "Clear all working locations of this workspace? Pins are not affected.", "NOVA Workspace",
                    MessageBoxButton.OKCancel) == MessageBoxResult.OK) ws?.ClearWorkingLocations();
        }, enabled: ws?.WorkingLocations.Count > 0);
        yield return Menus.Item("Clear recent folders", () => ws?.ClearRecents(), enabled: ws?.RecentFolders.Count > 0);
    }

    private void OnWorkingMenuClick(object sender, RoutedEventArgs e) => Menus.Show(WorkingMenuButton, BuildWorkingMenu().ToArray());
    private void OnAddWorkingClick(object sender, RoutedEventArgs e) => AddCurrentToWorking();

    private void OnWorkingDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? ((e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy)
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWorkingDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && _vm.ActiveWorkspace is { } ws)
        {
            var n = paths.Where(Directory.Exists).Count(ws.AddWorkingLocation);
            AppServices.Status($"Added {n} working location(s).");
        }
        e.Handled = true;
    }

    private void CaptureExplorerLocation(Action<string> use)
    {
        var all = ExplorerWindows.GetOpenLocations();
        if (all.Count == 0) { AppServices.Status("No File Explorer windows with folders are open.", StatusKind.Info); return; }
        var last = _foreground?.LastExplorerWindow ?? IntPtr.Zero;
        var ordered = all.OrderByDescending(l => l.Hwnd == last).ToList();
        if (ordered.Count == 1) { use(ordered[0].Path); AppServices.Status($"Captured {ordered[0].Path}"); return; }
        Menus.Show(this, ordered.Select(l => (object)Menus.Item((l.Hwnd == last ? "● " : "") + l.Path, () => use(l.Path))).ToArray());
    }

    // ================= Sidebar header, workspaces, sessions =================

    private void OnHideClick(object sender, RoutedEventArgs e) => HideToBackground();
    private void OnTopmostClick(object sender, RoutedEventArgs e) => Modes.SetTopmost(TopmostToggle.IsChecked == true);
    private void OnBrowserToggleClick(object sender, RoutedEventArgs e) => SetCompact(BrowserToggle.IsChecked != true);

    public void SetCompact(bool compact)
    {
        Modes.SetCompact(compact);
        if (!compact) Dispatcher.BeginInvoke(() => _vm.ActiveTab?.Host?.FocusView());
    }

    private void EnsureBrowserVisible()
    {
        if (Modes.Compact) SetCompact(false);
    }

    /// <summary>Called by the mode controller to reflect state in the header toggles.</summary>
    public void ReflectModeState(bool compact, bool topmost, bool topmostAllowed)
    {
        BrowserToggle.IsChecked = !compact;
        TopmostToggle.IsChecked = topmost;
        TopmostToggle.IsEnabled = topmostAllowed;
        BrowserPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarSplitter.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSidebarSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        Modes.OnSidebarResized(SidebarColumn.ActualWidth);

    private void OnAppMenuClick(object sender, RoutedEventArgs e)
    {
        var p = AppServices.Settings.Window;
        Menus.Show(AppMenuButton,
            Menus.Sub("Window mode", new object[]
            {
                Menus.Item("Floating window", () => Modes.SetMode(DisplayMode.Floating), isChecked: p.Mode == DisplayMode.Floating),
                Menus.Item("Docked left (reserves desktop space)", () => Modes.SetMode(DisplayMode.Docked, DockEdge.Left), isChecked: p.Mode == DisplayMode.Docked && p.DockEdge == DockEdge.Left),
                Menus.Item("Docked right (reserves desktop space)", () => Modes.SetMode(DisplayMode.Docked, DockEdge.Right), isChecked: p.Mode == DisplayMode.Docked && p.DockEdge == DockEdge.Right),
                Menus.Item("On the desktop (stays behind other windows)", () => Modes.SetMode(DisplayMode.Desktop), isChecked: p.Mode == DisplayMode.Desktop),
                new Separator(),
                Menus.Item("Always on top", () => Modes.SetTopmost(!p.Topmost), isChecked: p.Topmost, enabled: p.Mode != DisplayMode.Desktop),
                Menus.Item("Show file browser", () => SetCompact(!Modes.Compact), "Ctrl+B", isChecked: !Modes.Compact),
            }),
            Menus.Item("Settings…", OpenSettings),
            Menus.Item("Detection rules…", OpenRules),
            new Separator(),
            Menus.Item("Back up configuration now", () => AppServices.Status("Backup created: " + Path.GetFileName(AppServices.Backups.CreateBackup()))),
            Menus.Item("Restore configuration from backup…", RestoreBackup),
            Menus.Item("Open configuration folder", () => Launcher.OpenInExplorer(AppServices.Store.Root)),
            new Separator(),
            Menus.Item("Exit NOVA Workspace", () => ((App)Application.Current).ExitApplication()));
    }

    private void OnNewWorkspaceClick(object sender, RoutedEventArgs e)
    {
        var dlg = new WorkspaceEditorWindow("New workspace", "", "▣", isNew: true) { Owner = this };
        if (dlg.ShowDialog() == true) _vm.CreateWorkspace(dlg.WorkspaceName, dlg.WorkspaceIcon, dlg.IncludeDownloads);
    }

    private void OnWorkspaceMenuClick(object sender, RoutedEventArgs e)
    {
        var ws = _vm.ActiveWorkspace;
        if (ws == null) return;
        Menus.Show(WorkspaceMenuButton,
            Menus.Item("Rename / change icon…", () =>
            {
                var dlg = new WorkspaceEditorWindow("Edit workspace", ws.Name, ws.Icon, isNew: false) { Owner = this };
                if (dlg.ShowDialog() == true) { ws.Name = dlg.WorkspaceName; ws.Icon = dlg.WorkspaceIcon; RefreshWorkspaceCombo(); }
            }),
            Menus.Item("Move up in list", () => _vm.MoveWorkspace(ws, -1)),
            Menus.Item("Move down in list", () => _vm.MoveWorkspace(ws, +1)),
            new Separator(),
            Menus.Item("Associate with an application…", OpenRules),
            Menus.Item("Export workspace…", () => ExportWorkspace(ws)),
            Menus.Item("Import workspace…", ImportWorkspace),
            new Separator(),
            Menus.Item("Delete workspace…", () =>
            {
                if (_vm.Workspaces.Count <= 1) { AppServices.Status("The last workspace cannot be deleted."); return; }
                if (MessageBox.Show(this, $"Delete the workspace \"{ws.Name}\"?\n\nIts pins and sessions are archived in the backup folder. No files on disk are touched.",
                        "Delete workspace", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                {
                    _vm.DeleteWorkspace(ws);
                    ShowActiveWorkspaceView();
                }
            }, enabled: _vm.Workspaces.Count > 1));
    }

    private void RefreshWorkspaceCombo()
    {
        var items = WorkspaceCombo.ItemsSource;
        WorkspaceCombo.ItemsSource = null;
        WorkspaceCombo.ItemsSource = items;
        WorkspaceCombo.SelectedItem = _vm.ActiveWorkspace;
    }

    private void OnSessionMenuClick(object sender, RoutedEventArgs e)
    {
        var ws = _vm.ActiveWorkspace;
        if (ws == null) return;
        var session = ws.ActiveSession;
        var snaps = ws.Snapshots.Snapshots;
        Menus.Show(SessionMenuButton,
            Menus.Item("New empty session…", () =>
            {
                var n = TextPromptWindow.Ask(this, "New session", "Session name:", "New session");
                if (!string.IsNullOrWhiteSpace(n)) ws.AddSession(n, copyCurrent: false);
            }),
            Menus.Item("Duplicate current session…", () =>
            {
                var n = TextPromptWindow.Ask(this, "Duplicate session", "Session name:", session.Name + " (copy)");
                if (!string.IsNullOrWhiteSpace(n)) ws.AddSession(n, copyCurrent: true);
            }),
            Menus.Item("Rename session…", () =>
            {
                var n = TextPromptWindow.Ask(this, "Rename session", "Session name:", session.Name);
                if (!string.IsNullOrWhiteSpace(n)) ws.RenameSession(session, n);
            }),
            Menus.Item("Delete session", () =>
            {
                if (MessageBox.Show(this, $"Delete the session \"{session.Name}\" and its tabs?", "NOVA Workspace", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                    ws.RemoveSession(session);
            }, enabled: ws.Sessions.Count > 1),
            new Separator(),
            Menus.Item("Save snapshot…", () =>
            {
                var n = TextPromptWindow.Ask(this, "Save snapshot", "Snapshot name (stores tabs and layout, not files):", $"{session.Name} {DateTime.Now:g}");
                if (n != null) { ws.CaptureSnapshot(n); AppServices.Status("Snapshot saved."); }
            }),
            Menus.Sub("Restore snapshot into this session", snaps.Select(s => (object)Menus.Item($"{s.Name}  ({s.Created.LocalDateTime:g})", () =>
            {
                if (MessageBox.Show(this, $"Replace the tabs and layout of \"{session.Name}\" with the snapshot \"{s.Name}\"?", "Restore snapshot",
                        MessageBoxButton.OKCancel) == MessageBoxResult.OK) ws.RestoreSnapshot(s);
            }))),
            Menus.Sub("Delete snapshot", snaps.Select(s => (object)Menus.Item(s.Name, () => ws.DeleteSnapshot(s)))));
    }

    // ================= Dialog launchers =================

    private void OpenSettings()
    {
        var dlg = new SettingsWindow(AppServices.Settings) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        AppServices.SaveSettings();
        RegisterHotkey();
        ApplyDetectionSetting();
        ThemeService.Apply(AppServices.Settings.Theme);
        AppServices.Status("Settings saved.");
    }

    private void OpenRules()
    {
        var dlg = new RulesWindow(AppServices.Rules.Rules, _vm.Workspaces, AppServices.AutoSwitch.LastExternalWindow) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        AppServices.Rules.Rules = dlg.Result;
        AppServices.ReloadRules();
        AppServices.SaveRules();
        AppServices.Status($"{dlg.Result.Count} detection rule(s) saved.");
    }

    private void ExportWorkspace(WorkspaceViewModel ws)
    {
        var dlg = new SaveFileDialog { FileName = ws.Name + ".novaws", Filter = "NOVA workspace|*.novaws|JSON|*.json", Title = "Export workspace" };
        if (dlg.ShowDialog(this) != true) return;
        BackupService.ExportWorkspace(dlg.FileName, ws.Definition, AppServices.Rules.Rules);
        AppServices.Status($"Exported \"{ws.Name}\".");
    }

    private void ImportWorkspace()
    {
        var dlg = new OpenFileDialog { Filter = "NOVA workspace|*.novaws;*.json|All files|*.*", Title = "Import workspace" };
        if (dlg.ShowDialog(this) != true) return;
        var (def, rules) = BackupService.ImportWorkspace(dlg.FileName);
        _vm.AddImported(def, rules);
        AppServices.Status($"Imported \"{def.Name}\".");
    }

    private void RestoreBackup()
    {
        var dlg = new OpenFileDialog { InitialDirectory = AppServices.Backups.ListBackups().FirstOrDefault()?.DirectoryName ?? AppServices.Store.BackupDirectory, Filter = "NOVA backup|nova-backup-*.zip|Zip|*.zip" };
        if (dlg.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "Replace the current configuration with this backup?\nA safety backup of the current configuration is created first.",
                "Restore backup", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        ((App)Application.Current).RestoreBackup(dlg.FileName);
    }

    // ================= Association suggestion buttons =================

    private void OnSuggestWindow(object sender, RoutedEventArgs e) => _vm.AnswerSuggestion(AssociationChoice.RememberWindow);
    private void OnSuggestApp(object sender, RoutedEventArgs e) => _vm.AnswerSuggestion(AssociationChoice.RememberApplication);
    private void OnSuggestNever(object sender, RoutedEventArgs e) => _vm.AnswerSuggestion(AssociationChoice.NeverForApplication);
    private void OnSuggestOnce(object sender, RoutedEventArgs e) => _vm.AnswerSuggestion(AssociationChoice.JustThisTime);
}

internal static class PinFactory
{
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ps1", ".bat", ".cmd", ".py", ".sh", ".vbs" };
    private static readonly HashSet<string> AppExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".com", ".lnk", ".appref-ms" };

    public static PinNode FromPath(string path, bool isFolder)
    {
        if (isFolder) return PinNode.Folder(path);
        var ext = Path.GetExtension(path);
        var kind = AppExtensions.Contains(ext) ? PinKind.Application : ScriptExtensions.Contains(ext) ? PinKind.Script : PinKind.File;
        return new PinNode { Kind = kind, Target = path };
    }
}
