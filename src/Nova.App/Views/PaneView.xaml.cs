using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nova.App.Services;
using Nova.App.ViewModels;
using Nova.Core.Diagnostics;
using Nova.Windows.Shell;

namespace Nova.App.Views;

/// <summary>
/// One browser pane: a tab strip plus one native Shell view per (recently used) tab.
/// Ownership: this control creates, shows/hides and disposes the <see cref="ShellBrowserHost"/> instances.
/// Inactive tabs keep their native view (hidden) up to <see cref="MaxLiveHosts"/>; older ones are disposed and
/// transparently recreated from the tab's saved location when reactivated.
/// </summary>
public partial class PaneView : UserControl
{
    public const int MaxLiveHosts = 6;

    private readonly Dictionary<TabViewModel, ShellBrowserHost> _hosts = new();
    private readonly LinkedList<TabViewModel> _lru = new();
    private readonly MainViewModel _main;
    private readonly Func<Key, ModifierKeys, bool> _accelerator;
    private Window? _waitingWindow;

    public PaneView(PaneViewModel pane, MainViewModel main, Func<Key, ModifierKeys, bool> accelerator)
    {
        InitializeComponent();
        Pane = pane;
        _main = main;
        _accelerator = accelerator;
        DataContext = pane;
        pane.Tabs.CollectionChanged += OnTabsChanged;
        pane.PropertyChanged += OnPanePropertyChanged;
        pane.Owner.PropertyChanged += OnOwnerPropertyChanged;
        IsVisibleChanged += (_, _) => Sync();
        Loaded += (_, _) => Sync();
        PreviewMouseDown += (_, _) => Pane.Owner.ActivePaneIndex = Pane.Index;
        UpdateActiveIndicator();
    }

    public PaneViewModel Pane { get; }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.ActiveTab)) Sync();
        if (e.PropertyName == nameof(PaneViewModel.IsActive)) UpdateActiveIndicator();
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.DualPane) or nameof(WorkspaceViewModel.ActivePaneIndex))
            UpdateActiveIndicator();
    }

    private void UpdateActiveIndicator()
    {
        // Resource references (not one-time lookups) so a theme change updates the indicator.
        TabStripBorder.SetResourceReference(Border.BorderBrushProperty, Pane.IsActive && Pane.Owner.DualPane ? "Nova.Accent" : "Nova.Border");
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var live = new HashSet<TabViewModel>(Pane.Tabs);
        foreach (var tab in _hosts.Keys.Where(t => !live.Contains(t)).ToList())
            DisposeHost(tab);
        Sync();
    }

    /// <summary>Makes the active tab's native view exist and be the only visible one.</summary>
    public void Sync()
    {
        var active = Pane.ActiveTab;
        EmptyText.Visibility = active == null ? Visibility.Visible : Visibility.Collapsed;
        if (active != null && IsVisible && !_hosts.ContainsKey(active))
        {
            var window = Window.GetWindow(this);
            if (window != null && !window.IsActive)
            {
                // Creating a Shell view while NOVA is in the background could take focus from the user's
                // application, so creation is deferred until NOVA is activated.
                if (_waitingWindow == null)
                {
                    _waitingWindow = window;
                    window.Activated += OnWindowActivated;
                }
                EmptyText.Text = "Loading when NOVA is activated…";
                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                CreateHost(active);
            }
        }

        foreach (var (tab, host) in _hosts)
            host.Visibility = ReferenceEquals(tab, active) ? Visibility.Visible : Visibility.Collapsed;

        if (active != null && _hosts.ContainsKey(active))
        {
            EmptyText.Visibility = Visibility.Collapsed;
            _lru.Remove(active);
            _lru.AddFirst(active);
            TrimHosts();
        }
        if (active == null) EmptyText.Text = "No open tabs.\nClick a pinned folder or press Ctrl+T.";
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_waitingWindow != null) _waitingWindow.Activated -= OnWindowActivated;
        _waitingWindow = null;
        Dispatcher.BeginInvoke(Sync);
    }

    private void CreateHost(TabViewModel tab)
    {
        var host = new ShellBrowserHost { ApplicationAccelerator = _accelerator };
        host.Navigated += (_, e) => tab.OnHostNavigated(e.Path, e.DisplayName);
        host.NavigationFailed += (_, e) => tab.OnHostNavigationFailed(e.Path, e.Reason);
        host.FocusEntered += (_, _) => Pane.Owner.ActivePaneIndex = Pane.Index;
        tab.Host = host;
        _hosts[tab] = host;
        try
        {
            HostGrid.Children.Add(host);
            host.Navigate(tab.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch (Exception ex)
        {
            Log.Error("Could not create a file view", ex);
            AppServices.Status("The Windows file view could not be created: " + ex.Message, StatusKind.Error);
            DisposeHost(tab);
        }
    }

    private void TrimHosts()
    {
        while (_lru.Count > MaxLiveHosts)
        {
            var oldest = _lru.Last!.Value;
            if (ReferenceEquals(oldest, Pane.ActiveTab)) break;
            DisposeHost(oldest);
        }
    }

    private void DisposeHost(TabViewModel tab)
    {
        _lru.Remove(tab);
        if (!_hosts.Remove(tab, out var host)) return;
        tab.Host = null;
        HostGrid.Children.Remove(host);
        try { host.Dispose(); }
        catch (Exception ex) { Log.Warn("Disposing a file view failed", ex); }
    }

    /// <summary>Destroys all native views (on workspace eviction, session switch or shutdown). Tab state is kept.</summary>
    public void DisposeAll()
    {
        foreach (var tab in _hosts.Keys.ToList()) DisposeHost(tab);
        Pane.Tabs.CollectionChanged -= OnTabsChanged;
        Pane.PropertyChanged -= OnPanePropertyChanged;
        Pane.Owner.PropertyChanged -= OnOwnerPropertyChanged;
        if (_waitingWindow != null) _waitingWindow.Activated -= OnWindowActivated;
        _waitingWindow = null;
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e) => _main.NewTab(Pane);

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TabViewModel tab }) CloseTab(tab);
        e.Handled = true;
    }

    public void CloseTab(TabViewModel tab)
    {
        if (AppServices.Settings.ConfirmTabClose &&
            MessageBox.Show(Window.GetWindow(this), $"Close tab \"{tab.Title}\"?", "NOVA Workspace", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            return;
        Pane.Remove(tab);
    }

    private static TabViewModel? TabFromSource(object source)
    {
        var d = source as DependencyObject;
        while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        return (d as ListBoxItem)?.DataContext as TabViewModel;
    }

    private void OnTabStripPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && TabFromSource(e.OriginalSource) is { } tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    private void OnTabContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var tab = TabFromSource(e.OriginalSource);
        var menu = TabStrip.ContextMenu!;
        menu.Items.Clear();
        if (tab == null)
        {
            menu.Items.Add(Menus.Item("New tab", () => _main.NewTab(Pane), "Ctrl+T"));
            return;
        }
        Pane.ActiveTab = tab;
        menu.Items.Add(Menus.Item("Duplicate tab", () => Pane.AddTab(tab.Path)));
        menu.Items.Add(Menus.Item("Move to other pane", () => _main.MoveTabToOtherPane(tab)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menus.Item("Add to working locations", () =>
        {
            if (tab.Path != null && Pane.Owner.AddWorkingLocation(tab.Path)) AppServices.Status("Added to working locations.");
        }, enabled: tab.Path != null));
        menu.Items.Add(Menus.Item("Pin folder to workspace", () =>
        {
            if (tab.Path != null) Pane.Owner.AddPin(Nova.Core.Model.PinNode.Folder(tab.Path));
        }, enabled: tab.Path != null));
        menu.Items.Add(Menus.Item("Open in Windows Explorer", () => Commands.TryRun(() => Nova.Windows.Launching.Launcher.OpenInExplorer(tab.Path!)), enabled: tab.Path != null));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menus.Item("Close other tabs", () =>
        {
            foreach (var t in Pane.Tabs.Where(t => !ReferenceEquals(t, tab)).ToList()) Pane.Remove(t);
        }, enabled: Pane.Tabs.Count > 1));
        menu.Items.Add(Menus.Item("Close tab", () => CloseTab(tab), "Ctrl+W"));
    }
}
