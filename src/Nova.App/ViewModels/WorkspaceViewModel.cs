using System.Collections.ObjectModel;
using Nova.App.Infrastructure;
using Nova.App.Services;
using Nova.Core.IO;
using Nova.Core.Model;

namespace Nova.App.ViewModels;

/// <summary>
/// One workspace: its permanent definition (pins/groups/tools), its transient state (sessions, tabs, working
/// locations, recents) and lazily-loaded snapshots. Switching away from a workspace never discards any of this.
/// </summary>
public sealed class WorkspaceViewModel : ObservableObject
{
    private SnapshotCollection? _snapshots;
    private PaneViewModel[] _panes = Array.Empty<PaneViewModel>();

    public WorkspaceViewModel(WorkspaceDefinition definition, WorkspaceState state)
    {
        Definition = definition;
        State = state;
        State.WorkspaceId = definition.Id;
        Sessions = new ObservableCollection<WorkspaceSession>(state.Sessions);
        RebuildPins();
        RebuildWorkingLocations();
        RebuildRecents();
        LoadSession();
    }

    public WorkspaceDefinition Definition { get; }
    public WorkspaceState State { get; }
    public string Id => Definition.Id;

    public string Name
    {
        get => Definition.Name;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == Definition.Name) return;
            Definition.Name = value.Trim();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            AppServices.SaveCatalog();
        }
    }

    public string Icon
    {
        get => Definition.Icon;
        set
        {
            Definition.Icon = string.IsNullOrWhiteSpace(value) ? "▣" : value.Trim();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            AppServices.SaveCatalog();
        }
    }

    public string DisplayName => $"{Icon}  {Name}";

    public ObservableCollection<PinNodeViewModel> Pins { get; } = new();
    public ObservableCollection<WorkingLocation> WorkingLocations { get; } = new();
    public ObservableCollection<RecentItem> RecentFolders { get; } = new();
    public ObservableCollection<WorkspaceSession> Sessions { get; }

    /// <summary>Raised after the active session changed and the panes were rebuilt (the view must recreate its browsers).</summary>
    public event EventHandler? SessionLoaded;

    /// <summary>Raised whenever the active pane, active tab or the active tab's location changes.</summary>
    public event EventHandler? ActiveTabChanged;

    public WorkspaceSession ActiveSession
    {
        get => State.ActiveSession;
        set
        {
            // Ignore values that do not belong to this workspace (WPF can push stale selections while DataContext changes).
            if (value == null || value.Id == State.ActiveSessionId || !State.Sessions.Contains(value)) return;
            State.ActiveSessionId = value.Id;
            LoadSession();
            SaveState();
        }
    }

    public IReadOnlyList<PaneViewModel> Panes => _panes;

    public bool DualPane
    {
        get => ActiveSession.DualPane;
        set
        {
            if (ActiveSession.DualPane == value) return;
            ActiveSession.DualPane = value;
            if (!value) ActivePaneIndex = 0;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int ActivePaneIndex
    {
        get => ActiveSession.ActivePaneIndex;
        set
        {
            var v = Math.Clamp(value, 0, WorkspaceSession.PaneCount - 1);
            if (!DualPane) v = 0;
            if (ActiveSession.ActivePaneIndex == v) return;
            ActiveSession.ActivePaneIndex = v;
            OnPropertyChanged();
            foreach (var p in _panes) p.RaiseIsActive();
            ActiveTabChanged?.Invoke(this, EventArgs.Empty);
            SaveState();
        }
    }

    public PaneViewModel ActivePane => _panes[Math.Clamp(ActivePaneIndex, 0, _panes.Length - 1)];
    public PaneViewModel OtherPane => _panes[ActivePaneIndex == 0 ? 1 : 0];
    public TabViewModel? ActiveTab => ActivePane.ActiveTab;

    public SnapshotCollection Snapshots => _snapshots ??= AppServices.Store.LoadSnapshots(Id).Value;

    private void LoadSession()
    {
        var s = ActiveSession;
        s.Normalize();
        _panes = Enumerable.Range(0, WorkspaceSession.PaneCount).Select(i => new PaneViewModel(s.Panes[i], this, i)).ToArray();
        OnPropertyChanged(nameof(ActiveSession));
        OnPropertyChanged(nameof(Panes));
        OnPropertyChanged(nameof(DualPane));
        OnPropertyChanged(nameof(ActivePaneIndex));
        SessionLoaded?.Invoke(this, EventArgs.Empty);
        ActiveTabChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-reads the active session after its content was replaced (e.g. snapshot restore).</summary>
    public void ReloadSession() => LoadSession();

    internal void OnActiveTabChanged(PaneViewModel pane)
    {
        SaveState();
        if (pane.Index == ActivePaneIndex) ActiveTabChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void OnTabNavigated(TabViewModel tab)
    {
        if (tab.Path is { } path)
        {
            State.RecentFolders.Touch(path);
            RebuildRecents();
        }
        ActiveSession.Modified = DateTimeOffset.UtcNow;
        SaveState();
        if (ReferenceEquals(tab, ActiveTab)) ActiveTabChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveState() => AppServices.SaveState(State);

    // ---------- Sessions & snapshots ----------

    public WorkspaceSession AddSession(string name, bool copyCurrent)
    {
        var s = State.AddSession(name, copyCurrent ? ActiveSession : null);
        Sessions.Add(s);
        ActiveSession = s;
        return s;
    }

    public void RenameSession(WorkspaceSession s, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        s.Name = name.Trim();
        var i = Sessions.IndexOf(s);
        if (i >= 0) { Sessions.RemoveAt(i); Sessions.Insert(i, s); }
        OnPropertyChanged(nameof(ActiveSession));
        SaveState();
    }

    public bool RemoveSession(WorkspaceSession s)
    {
        var wasActive = s.Id == State.ActiveSessionId;
        if (!State.RemoveSession(s.Id)) return false;
        Sessions.Remove(s);
        if (wasActive) LoadSession();
        SaveState();
        return true;
    }

    public SessionSnapshot CaptureSnapshot(string name)
    {
        var snap = Snapshots.Capture(ActiveSession, name);
        AppServices.SaveSnapshots(Snapshots);
        return snap;
    }

    public void RestoreSnapshot(SessionSnapshot snap)
    {
        SnapshotCollection.RestoreInto(snap, ActiveSession);
        LoadSession();
        SaveState();
    }

    public void DeleteSnapshot(SessionSnapshot snap)
    {
        Snapshots.Snapshots.Remove(snap);
        AppServices.SaveSnapshots(Snapshots);
    }

    // ---------- Pins ----------

    public void RebuildPins()
    {
        Pins.Clear();
        foreach (var p in Definition.Pins)
            Pins.Add(new PinNodeViewModel(p, null, AppServices.SaveCatalog));
    }

    /// <summary>Adds a pin to <paramref name="group"/> (or the root), optionally before <paramref name="before"/>.</summary>
    public void AddPin(PinNode node, PinNodeViewModel? group = null)
    {
        var list = group is { IsGroup: true } ? group.Model.Children : Definition.Pins;
        list.Add(node);
        if (group != null) group.Model.IsExpanded = true;
        RebuildPins();
        AppServices.SaveCatalog();
        _ = ProbePinsAsync();
    }

    public void RemovePin(PinNodeViewModel vm)
    {
        if (PinTree.Remove(Definition.Pins, vm.Id))
        {
            RebuildPins();
            AppServices.SaveCatalog();
        }
    }

    public bool MovePin(string nodeId, string? targetId, bool intoGroup, bool after)
    {
        if (!PinTree.Move(Definition.Pins, nodeId, targetId, intoGroup, after)) return false;
        RebuildPins();
        AppServices.SaveCatalog();
        _ = ProbePinsAsync();
        return true;
    }

    public void ReplacePin(PinNodeViewModel vm, PinNode updated)
    {
        var m = vm.Model;
        m.Kind = updated.Kind;
        m.Label = updated.Label;
        m.Target = updated.Target;
        m.Arguments = updated.Arguments;
        m.WorkingDirectory = updated.WorkingDirectory;
        m.ReuseExistingWindow = updated.ReuseExistingWindow;
        RebuildPins();
        AppServices.SaveCatalog();
        _ = ProbePinsAsync();
    }

    /// <summary>Marks unreachable pins without ever removing them. Runs off the UI thread with per-path timeouts.</summary>
    public async Task ProbePinsAsync()
    {
        var items = Pins.SelectMany(p => p.SelfAndDescendants())
            .Where(p => p.Kind is PinKind.Folder or PinKind.File or PinKind.Application or PinKind.Script && !string.IsNullOrWhiteSpace(p.Target))
            .ToList();
        var tasks = items.Select(async vm =>
        {
            var status = await PathProbe.ProbeAsync(vm.Target, expectDirectory: vm.Kind == PinKind.Folder).ConfigureAwait(true);
            vm.Status = status;
        });
        try { await Task.WhenAll(tasks); }
        catch (Exception ex) { Nova.Core.Diagnostics.Log.Warn("Probing pins failed", ex); }
    }

    // ---------- Working locations & recents ----------

    public void RebuildWorkingLocations()
    {
        WorkingLocations.Clear();
        foreach (var w in State.WorkingLocations) WorkingLocations.Add(w);
    }

    public bool AddWorkingLocation(string path)
    {
        if (!State.AddWorkingLocation(path)) return false;
        RebuildWorkingLocations();
        SaveState();
        return true;
    }

    public void RemoveWorkingLocation(WorkingLocation w)
    {
        State.WorkingLocations.Remove(w);
        RebuildWorkingLocations();
        SaveState();
    }

    public void ClearWorkingLocations()
    {
        State.WorkingLocations.Clear();
        RebuildWorkingLocations();
        SaveState();
    }

    public void RebuildRecents()
    {
        RecentFolders.Clear();
        foreach (var r in State.RecentFolders.MostRecent(15)) RecentFolders.Add(r);
    }

    public void ClearRecents()
    {
        State.RecentFolders.Items.Clear();
        RebuildRecents();
        SaveState();
    }
}
