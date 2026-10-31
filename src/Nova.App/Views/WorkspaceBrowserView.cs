using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.App.ViewModels;

namespace Nova.App.Views;

/// <summary>The (one or two pane) browser area of one workspace. Kept alive while the workspace is recently used.</summary>
public sealed class WorkspaceBrowserView : Grid
{
    private readonly MainViewModel _main;
    private readonly Func<Key, ModifierKeys, bool> _accelerator;
    private readonly GridSplitter _splitter;
    private PaneView[] _panes = Array.Empty<PaneView>();

    public WorkspaceBrowserView(WorkspaceViewModel workspace, MainViewModel main, Func<Key, ModifierKeys, bool> accelerator)
    {
        Workspace = workspace;
        _main = main;
        _accelerator = accelerator;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        _splitter = new GridSplitter
        {
            Width = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Focusable = false,
            ToolTip = "Drag to resize panes",
        };
        _splitter.SetResourceReference(BackgroundProperty, "Nova.Border");
        _splitter.DragCompleted += (_, _) => SaveRatio();
        SetColumn(_splitter, 1);
        Children.Add(_splitter);

        workspace.SessionLoaded += OnSessionLoaded;
        workspace.PropertyChanged += OnWorkspacePropertyChanged;
        BuildPanes();
    }

    public WorkspaceViewModel Workspace { get; }
    public IReadOnlyList<PaneView> Panes => _panes;

    private void OnSessionLoaded(object? sender, EventArgs e) => BuildPanes();

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.DualPane)) ApplyLayout();
    }

    private void BuildPanes()
    {
        foreach (var p in _panes)
        {
            p.DisposeAll();
            Children.Remove(p);
        }
        _panes = Workspace.Panes.Select(vm => new PaneView(vm, _main, _accelerator)).ToArray();
        for (var i = 0; i < _panes.Length; i++)
        {
            SetColumn(_panes[i], i == 0 ? 0 : 2);
            Children.Add(_panes[i]);
        }
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        var dual = Workspace.DualPane;
        var ratio = Workspace.ActiveSession.SplitRatio;
        ColumnDefinitions[0].Width = new GridLength(dual ? ratio : 1, GridUnitType.Star);
        ColumnDefinitions[2].Width = dual ? new GridLength(1 - ratio, GridUnitType.Star) : new GridLength(0);
        ColumnDefinitions[2].MinWidth = dual ? 120 : 0;
        _splitter.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
        if (_panes.Length > 1) _panes[1].Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveRatio()
    {
        var a = ColumnDefinitions[0].ActualWidth;
        var b = ColumnDefinitions[2].ActualWidth;
        if (a + b <= 0) return;
        Workspace.ActiveSession.SplitRatio = Math.Clamp(a / (a + b), 0.1, 0.9);
        Workspace.SaveState();
    }

    public PaneView? ViewFor(PaneViewModel pane) => _panes.FirstOrDefault(p => ReferenceEquals(p.Pane, pane));

    public void DisposeAll()
    {
        Workspace.SessionLoaded -= OnSessionLoaded;
        Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        foreach (var p in _panes) p.DisposeAll();
        Children.Clear();
        _panes = Array.Empty<PaneView>();
    }
}
