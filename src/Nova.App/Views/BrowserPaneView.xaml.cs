using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.App.Platform;
using Nova.App.ViewModels;
using Nova.Core.Models;
using Nova.Core.Services;
using Nova.Core.Util;

namespace Nova.App.Views;

public partial class BrowserPaneView : UserControl
{
    private BrowserPaneViewModel? _pane;
    private BrowserTabViewModel? _tab;
    private Point _dragStart;
    private FileItemViewModel? _pressedItem;
    private bool _deferSingleSelect;

    public BrowserPaneView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachPane(DataContext as BrowserPaneViewModel);
    }

    private MainViewModel Main => (MainViewModel)Window.GetWindow(this)!.DataContext;
    private MainWindow Shell => (MainWindow)Window.GetWindow(this)!;
    private BrowserTabViewModel? Tab => _pane?.SelectedTab;

    // ── Wiring ──────────────────────────────────────────────────────────────

    private void AttachPane(BrowserPaneViewModel? pane)
    {
        if (_pane is not null) _pane.PropertyChanged -= OnPaneChanged;
        _pane = pane;
        if (_pane is not null) _pane.PropertyChanged += OnPaneChanged;
        AttachTab(_pane?.SelectedTab);
        UpdateActiveFrame();
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserPaneViewModel.SelectedTab)) AttachTab(_pane?.SelectedTab);
        if (e.PropertyName == nameof(BrowserPaneViewModel.IsActive)) UpdateActiveFrame();
    }

    private void AttachTab(BrowserTabViewModel? tab)
    {
        if (_tab is not null)
        {
            _tab.SelectionRequested -= ApplySelection;
            _tab.PropertyChanged -= OnTabChanged;
            _tab.CurrentSelectionProvider = null;
        }
        _tab = tab;
        if (_tab is not null)
        {
            _tab.SelectionRequested += ApplySelection;
            _tab.PropertyChanged += OnTabChanged;
            _tab.CurrentSelectionProvider = SelectedNames;
        }
        UpdateSortHeaders();
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserTabViewModel.SortBy) or nameof(BrowserTabViewModel.SortDescending)) UpdateSortHeaders();
    }

    /// <summary>Accent line marks the active pane; only meaningful in dual-pane mode.</summary>
    public void UpdateActiveFrame()
    {
        var dual = (Window.GetWindow(this)?.DataContext as MainViewModel)?.IsDualPane == true;
        Frame.BorderBrush = _pane?.IsActive == true && dual
            ? TryFindResource("AccentFillColorDefaultBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DodgerBlue
            : System.Windows.Media.Brushes.Transparent;
    }

    private void OnAnyFocus(object sender, KeyboardFocusChangedEventArgs e) { if (_pane is not null) Main.ActivatePane(_pane); }
    private void OnAnyMouseDown(object sender, MouseButtonEventArgs e) { if (_pane is not null) Main.ActivatePane(_pane); }

    public void FocusList()
    {
        if (FileList.SelectedItem is not null && FileList.ItemContainerGenerator.ContainerFromItem(FileList.SelectedItem) is ListViewItem li) li.Focus();
        else FileList.Focus();
    }

    public void FocusAddress()
    {
        if (Tab is null) return;
        Tab.AddressText = Tab.Path;
        Tab.IsEditingAddress = true;
        Dispatcher.BeginInvoke(() => { AddressBox.Focus(); AddressBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public void FocusFilter() { FilterBox.Focus(); FilterBox.SelectAll(); }

    public IReadOnlyCollection<string> SelectedNames() =>
        FileList.SelectedItems.Cast<FileItemViewModel>().Select(i => i.Name).ToList();

    private List<FileItemViewModel> Selection => FileList.SelectedItems.Cast<FileItemViewModel>().ToList();
    private List<string> SelectedPaths => Selection.Select(i => i.FullPath).ToList();

    private void ApplySelection(IReadOnlyCollection<string> names)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        FileList.SelectedItems.Clear();
        FileItemViewModel? first = null;
        foreach (var item in Tab?.Items ?? Enumerable.Empty<FileItemViewModel>())
        {
            if (!set.Contains(item.Name)) continue;
            FileList.SelectedItems.Add(item);
            first ??= item;
        }
        if (first is not null)
        {
            FileList.ScrollIntoView(first);
            Dispatcher.BeginInvoke(() =>
            {
                if (FileList.ItemContainerGenerator.ContainerFromItem(first) is ListViewItem li && IsKeyboardFocusWithin) li.Focus();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tab is null) return;
        var sel = Selection;
        if (sel.Count == 0) { Tab.SelectionText = null; return; }
        long bytes = sel.Where(i => i.Size is not null).Sum(i => i.Size!.Value);
        Tab.SelectionText = sel.Count == 1
            ? $"{sel[0].Name}{(sel[0].Size is { } s ? " · " + FileNameHelper.FormatSize(s) : "")}"
            : $"{sel.Count} selected" + (bytes > 0 ? $" · {FileNameHelper.FormatSize(bytes)}" : "");
    }

    // ── Navigation bar ──────────────────────────────────────────────────────

    private void OnBack(object s, RoutedEventArgs e) => _ = Tab?.GoBackAsync();
    private void OnForward(object s, RoutedEventArgs e) => _ = Tab?.GoForwardAsync();
    private void OnUp(object s, RoutedEventArgs e) => _ = Tab?.GoUpAsync();
    private void OnRefresh(object s, RoutedEventArgs e) => _ = Tab?.ReloadAsync(preserveSelection: true, currentSelection: SelectedNames());

    private void OnCrumbClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Crumb c) _ = Tab?.NavigateAsync(c.Path);
        e.Handled = true;
    }

    /// <summary>Long paths: keep the current folder (the right end) in view.</summary>
    private void OnCrumbsSizeChanged(object sender, SizeChangedEventArgs e) => CrumbScroll.ScrollToRightEnd();

    private void OnCrumbBarClick(object sender, MouseButtonEventArgs e)
    {
        if (MainWindow.ItemUnder<Button>(e.OriginalSource) is null) FocusAddress();
    }

    private async void OnAddressKey(object sender, KeyEventArgs e)
    {
        if (Tab is null) return;
        if (e.Key == Key.Escape) { Tab.IsEditingAddress = false; FocusList(); e.Handled = true; }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var text = Tab.AddressText.Trim().Trim('"');
            var expanded = VariableExpander.Expand(text, Main.ActiveWorkspace.Variables);
            if (Directory.Exists(expanded)) { await Tab.NavigateAsync(expanded); FocusList(); }
            else if (File.Exists(expanded)) { Report(ShellLauncher.Open(expanded)); Tab.IsEditingAddress = false; }
            else Main.ShowStatus($"Can't find “{text}”.");
        }
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Tab is not null) Tab.IsEditingAddress = false;
    }

    private void OnFilterKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Tab is not null) { Tab.FilterText = ""; FocusList(); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Enter) { FocusList(); if (FileList.Items.Count > 0 && FileList.SelectedIndex < 0) FileList.SelectedIndex = 0; e.Handled = true; }
    }

    // ── Tabs ────────────────────────────────────────────────────────────────

    private void OnTabMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && MainWindow.ItemUnder<ListBoxItem>(e.OriginalSource)?.DataContext is BrowserTabViewModel t)
        {
            _pane?.CloseTabCommand.Execute(t);
            e.Handled = true;
        }
    }

    private BrowserTabViewModel? MenuTab => TabStrip.SelectedItem as BrowserTabViewModel;
    private void OnDuplicateTab(object s, RoutedEventArgs e) => _pane?.DuplicateTabCommand.Execute(MenuTab);
    private void OnCloseTab(object s, RoutedEventArgs e) => _pane?.CloseTabCommand.Execute(MenuTab);
    private void OnCloseOtherTabs(object s, RoutedEventArgs e)
    {
        if (_pane is null || MenuTab is not { } keep) return;
        foreach (var t in _pane.Tabs.Where(t => t != keep).ToList()) _pane.CloseTabCommand.Execute(t);
    }

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        // Hovering a tab while dragging files switches to it (like Explorer/browsers).
        if (MainWindow.ItemUnder<ListBoxItem>(e.OriginalSource)?.DataContext is BrowserTabViewModel t && _pane is not null && _pane.SelectedTab != t)
            _pane.SelectedTab = t;
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTabDrop(object sender, DragEventArgs e) => e.Handled = true;

    // ── Sorting ─────────────────────────────────────────────────────────────

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Tag: string tag } && Enum.TryParse<SortField>(tag, out var field))
            Tab?.SetSort(field);
    }

    private void UpdateSortHeaders()
    {
        var t = Tab;
        foreach (var (header, field, label) in new[]
                 {
                     (NameHeader, SortField.Name, "Name"), (ModifiedHeader, SortField.Modified, "Date modified"),
                     (TypeHeader, SortField.Type, "Type"), (SizeHeader, SortField.Size, "Size"),
                 })
            header.Content = t is not null && t.SortBy == field ? label + (t.SortDescending ? "  ▾" : "  ▴") : label;
    }

    // ── Opening items ───────────────────────────────────────────────────────

    private async Task OpenAsync(FileItemViewModel item, OpenTarget target)
    {
        if (item.IsDirectory)
        {
            if (target == OpenTarget.Current && Tab is not null) await Tab.NavigateAsync(item.FullPath);
            else await Main.NavigateAsync(item.FullPath, target);
        }
        else Report(ShellLauncher.Open(item.FullPath));
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (MainWindow.ItemUnder<ListViewItem>(e.OriginalSource)?.DataContext is FileItemViewModel item)
        {
            _ = OpenAsync(item, ModifierTarget());
            e.Handled = true;
        }
    }

    private void OnListMiddleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && MainWindow.ItemUnder<ListViewItem>(e.OriginalSource)?.DataContext is FileItemViewModel { IsDirectory: true } item)
        {
            _ = OpenAsync(item, OpenTarget.NewTab);
            e.Handled = true;
        }
    }

    private static OpenTarget ModifierTarget() =>
        Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? OpenTarget.NewTab
        : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? OpenTarget.OtherPane
        : OpenTarget.Current;

    // ── Keyboard ────────────────────────────────────────────────────────────

    private async void OnListKey(object sender, KeyEventArgs e)
    {
        if (Tab is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var sel = Selection;
        e.Handled = true;
        switch (key)
        {
            case Key.Enter when alt && sel.Count > 0: ShellLauncher.ShowProperties(sel[0].FullPath, Main.OwnerHandle()); break;
            case Key.Enter when sel.Count > 0:
                foreach (var i in sel.Take(sel.Count == 1 ? 1 : 16)) await OpenAsync(i, ModifierTarget());
                break;
            case Key.Back: await Tab.GoUpAsync(); break;
            case Key.F2 when sel.Count == 1: await RenameAsync(sel[0]); break;
            case Key.Delete when sel.Count > 0: await Main.DeleteAsync(SelectedPaths, permanent: shift); break;
            case Key.C when ctrl && shift && sel.Count > 0: Shell.TrySetClipboard(string.Join(Environment.NewLine, SelectedPaths.Select(p => $"\"{p}\""))); break;
            case Key.C when ctrl: if (!ClipboardFiles.Set(SelectedPaths, cut: false)) Main.ShowStatus("Clipboard is busy."); else Main.ShowStatus($"Copied {sel.Count} item(s)"); break;
            case Key.X when ctrl: if (ClipboardFiles.Set(SelectedPaths, cut: true)) Main.ShowStatus($"Cut {sel.Count} item(s)"); break;
            case Key.V when ctrl: await PasteAsync(Tab.Path); break;
            case Key.N when ctrl && shift: await NewFolderAsync(); break;
            case Key.Escape when !string.IsNullOrEmpty(Tab.FilterText): Tab.FilterText = ""; break;
            default: e.Handled = false; break;
        }
    }

    // ── File operations ─────────────────────────────────────────────────────

    private async Task RenameAsync(FileItemViewModel item)
    {
        var name = InputDialog.Ask(Shell, "Rename", "New name:", item.Name, selectStem: !item.IsDirectory);
        if (name is null || name == item.Name) return;
        if (await Main.RenameAsync(item.FullPath, name) && Tab is not null)
            await Tab.ReloadAsync(select: new[] { name });
    }

    private async Task NewFolderAsync()
    {
        if (Tab is null) return;
        var name = InputDialog.Ask(Shell, "New folder", "Folder name:", "New folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Main.CreateFolder(Tab.Path, name.Trim()) is { } created) await Tab.ReloadAsync(select: new[] { created });
    }

    private async Task PasteAsync(string destination)
    {
        if (!ClipboardFiles.TryGet(out var paths, out var cut)) { Main.ShowStatus("Nothing to paste."); return; }
        await Main.TransferAsync(paths, destination, move: cut);
        if (cut) ClipboardFiles.Clear();
    }

    // ── Context menu ────────────────────────────────────────────────────────

    private void OnListContextMenu(object sender, ContextMenuEventArgs e)
    {
        var menu = FileList.ContextMenu!;
        menu.Items.Clear();
        if (Tab is null) { e.Handled = true; return; }

        var onItem = MainWindow.ItemUnder<ListViewItem>(e.OriginalSource);
        if (onItem is not null && !onItem.IsSelected) { FileList.SelectedItems.Clear(); onItem.IsSelected = true; }
        var sel = onItem is null ? new List<FileItemViewModel>() : Selection;
        if (onItem is null) FileList.SelectedItems.Clear();

        foreach (var i in sel.Count > 0 ? ItemMenu(sel) : BackgroundMenu()) menu.Items.Add(i is null ? new Separator() : i);
    }

    private IEnumerable<MenuItem?> ItemMenu(List<FileItemViewModel> sel)
    {
        var first = sel[0];
        var paths = sel.Select(i => i.FullPath).ToList();
        var single = sel.Count == 1;
        var M = (Func<string, Action, bool, bool, MenuItem>)((h, a, en, b) => MainWindow.Item(h, a, en, b));

        yield return M("Open", () => { foreach (var i in sel.Take(16)) _ = OpenAsync(i, OpenTarget.Current); }, true, true);
        if (first.IsDirectory)
        {
            yield return M("Open in new tab", () => _ = OpenAsync(first, OpenTarget.NewTab), single, false);
            yield return M("Open in other pane", () => _ = OpenAsync(first, OpenTarget.OtherPane), single, false);
            yield return M("Open in File Explorer", () => Report(ShellLauncher.OpenInExplorer(first.FullPath)), single, false);
            yield return M("Open terminal here", () => Report(ShellLauncher.OpenTerminal(first.FullPath)), single, false);
        }
        else
        {
            yield return M("Open with…", () => Report(ShellLauncher.OpenWith(first.FullPath)), single, false);
            yield return M("Show in File Explorer", () => Report(ShellLauncher.RevealInExplorer(first.FullPath)), single, false);
        }
        yield return null;
        yield return M("Cut", () => ClipboardFiles.Set(paths, cut: true), true, false);
        yield return M("Copy", () => ClipboardFiles.Set(paths, cut: false), true, false);
        if (first.IsDirectory && single) yield return M("Paste into folder", () => _ = PasteAsync(first.FullPath), true, false);
        yield return M("Copy path", () => Shell.TrySetClipboard(string.Join(Environment.NewLine, paths)), true, false);
        yield return null;

        // Project-aware transfer targets: the workspace's folder pins and working set.
        yield return TransferMenu("Move to", paths, move: true);
        yield return TransferMenu("Copy to", paths, move: false);
        yield return null;
        yield return M("Pin to " + Main.ActiveWorkspace.Name, () => Main.PinPaths(paths), true, false);
        if (sel.Any(i => i.IsDirectory))
            yield return M("Add to working set", () => { foreach (var i in sel.Where(i => i.IsDirectory)) Main.AddToWorkingSet(i.FullPath); }, true, false);
        yield return null;
        yield return M("Rename…", () => _ = RenameAsync(first), single, false);
        yield return M("Delete", () => _ = Main.DeleteAsync(paths, permanent: false), true, false);
        yield return null;
        yield return M("Properties", () => ShellLauncher.ShowProperties(first.FullPath, Main.OwnerHandle()), true, false);
    }

    private IEnumerable<MenuItem?> BackgroundMenu()
    {
        var path = Tab!.Path;
        var hasClip = false;
        try { hasClip = Clipboard.ContainsFileDropList(); } catch (System.Runtime.InteropServices.COMException) { }
        yield return MainWindow.Item("Paste", () => _ = PasteAsync(path), hasClip);
        yield return MainWindow.Item("New folder…", () => _ = NewFolderAsync());
        yield return MainWindow.Item("Refresh", () => _ = Tab.ReloadAsync(preserveSelection: true, currentSelection: SelectedNames()));
        var sort = new MenuItem { Header = "Sort by" };
        foreach (var f in Enum.GetValues<SortField>())
        {
            var field = f;
            sort.Items.Add(new MenuItem { Header = field.ToString(), IsCheckable = true, IsChecked = Tab.SortBy == field });
            ((MenuItem)sort.Items[^1]!).Click += (_, _) => Tab.SetSort(field);
        }
        yield return sort;
        yield return null;
        yield return MainWindow.Item("Pin this folder", () => Main.PinPaths(new[] { path }));
        yield return MainWindow.Item("Add to working set", () => Main.AddToWorkingSet(path));
        yield return MainWindow.Item("Copy path", () => Shell.TrySetClipboard(path));
        yield return null;
        yield return MainWindow.Item("Open in File Explorer", () => Report(ShellLauncher.OpenInExplorer(path)));
        yield return MainWindow.Item("Open terminal here", () => Report(ShellLauncher.OpenTerminal(path)));
        yield return MainWindow.Item("Properties", () => ShellLauncher.ShowProperties(path, Main.OwnerHandle()));
    }

    private MenuItem TransferMenu(string header, List<string> paths, bool move)
    {
        var root = new MenuItem { Header = header };
        var ws = Main.ActiveWorkspace;
        var targets = ws.AllPinItems().Where(p => p.Kind == PinKind.Folder)
            .Select(p => (Label: p.Name, Path: VariableExpander.Expand(p.Target, ws.Variables)))
            .Concat(Main.ActiveSession.WorkingSet.Select(w => (Label: (w.Label ?? PathUtil.DisplayName(w.Path)) + "  (working)", w.Path)))
            .Where(t => Directory.Exists(t.Path) && !PathUtil.PathEquals(t.Path, Tab?.Path))
            .DistinctBy(t => t.Path.ToUpperInvariant())
            .ToList();
        foreach (var t in targets)
        {
            var target = t;
            var mi = MainWindow.Item(target.Label, () => _ = Main.TransferAsync(paths, target.Path, move));
            mi.ToolTip = target.Path;
            root.Items.Add(mi);
        }
        if (Main.IsDualPane && Main.OtherPane.SelectedTab?.Path is { } other && !PathUtil.PathEquals(other, Tab?.Path))
        {
            if (root.Items.Count > 0) root.Items.Add(new Separator());
            root.Items.Add(MainWindow.Item($"Other pane  ({PathUtil.DisplayName(other)})", () => _ = Main.TransferAsync(paths, other, move)));
        }
        root.Items.Add(new Separator());
        root.Items.Add(MainWindow.Item("Choose folder…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Tab?.Path };
            if (dlg.ShowDialog(Shell) == true) _ = Main.TransferAsync(paths, dlg.FolderName, move);
        }));
        return root;
    }

    // ── Drag & drop ─────────────────────────────────────────────────────────

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _pressedItem = MainWindow.ItemUnder<ListViewItem>(e.OriginalSource)?.DataContext as FileItemViewModel;
        _deferSingleSelect = false;
        // Keep a multi-selection intact when pressing on one of its items, so it can be dragged.
        if (_pressedItem is not null && FileList.SelectedItems.Contains(_pressedItem) && FileList.SelectedItems.Count > 1 &&
            Keyboard.Modifiers == ModifierKeys.None && e.ClickCount == 1)
        {
            _deferSingleSelect = true;
            e.Handled = true;
            FileList.Focus();
        }
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_deferSingleSelect && _pressedItem is not null)
        {
            FileList.SelectedItems.Clear();
            FileList.SelectedItem = _pressedItem;
        }
        _deferSingleSelect = false;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedItem is null) return;
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var paths = SelectedPaths;
        if (paths.Count == 0) return;
        _deferSingleSelect = false;
        _pressedItem = null;
        var data = new DataObject();
        var list = new System.Collections.Specialized.StringCollection();
        list.AddRange(paths.ToArray());
        data.SetFileDropList(list);
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    private string? DropTarget(DragEventArgs e) =>
        MainWindow.ItemUnder<ListViewItem>(e.OriginalSource)?.DataContext is FileItemViewModel { IsDirectory: true } dir ? dir.FullPath : Tab?.Path;

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var target = DropTarget(e);
        if (target is null || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) { e.Effects = DragDropEffects.None; return; }
        // Dropping onto the folder the items already live in (or onto themselves) is a no-op.
        if (paths.All(p => PathUtil.PathEquals(Path.GetDirectoryName(p), target)) || paths.Any(p => PathUtil.IsUnder(target, p)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropHelper.ChooseEffect(e, paths, target);
    }

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var target = DropTarget(e);
        if (target is null || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        if (paths.All(p => PathUtil.PathEquals(Path.GetDirectoryName(p), target))) return;
        var effect = DragDropHelper.ChooseEffect(e, paths, target);
        if (effect == DragDropEffects.None) return;
        await Main.TransferAsync(paths, target, move: effect == DragDropEffects.Move);
    }

    private void OnCrumbDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Crumb c && e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            !paths.All(p => PathUtil.PathEquals(Path.GetDirectoryName(p), c.Path)))
            e.Effects = DragDropHelper.ChooseEffect(e, paths, c.Path);
        else e.Effects = DragDropEffects.None;
    }

    private async void OnCrumbDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not Crumb c || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var effect = DragDropHelper.ChooseEffect(e, paths, c.Path);
        if (effect != DragDropEffects.None) await Main.TransferAsync(paths, c.Path, move: effect == DragDropEffects.Move);
    }

    private void Report(string? error) { if (error is not null) Main.ShowStatus(error); }
}
