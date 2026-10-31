using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.App.Platform;
using Nova.Core.Models;
using Nova.Core.Persistence;
using Nova.Core.Services;
using Nova.Core.Util;

namespace Nova.App.ViewModels;

public enum OpenTarget { Current, NewTab, OtherPane }

/// <summary>
/// Orchestrates workspaces, sessions, panes, detection and persistence.
/// UI-free: dialogs and window behaviour live in the views, which call into this.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly StateStore _store;
    private readonly DispatcherTimer _autosave;
    private readonly DispatcherTimer _statusTimer;
    private bool _switching;

    public MainViewModel(NovaState state, StateStore store)
    {
        State = state;
        _store = store;
        LeftPane = new BrowserPaneViewModel(this, 0) { IsActive = true };
        RightPane = new BrowserPaneViewModel(this, 1);
        _activePane = LeftPane;

        // Snapshot-compare autosave: cheap, and immune to "forgot to mark dirty" bugs.
        _autosave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autosave.Tick += (_, _) => Save();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); Status = null; };
    }

    public NovaState State { get; }
    public AppSettings Settings => State.Settings;
    public ObservableCollection<Workspace> Workspaces => State.Workspaces;
    public BrowserPaneViewModel LeftPane { get; }
    public BrowserPaneViewModel RightPane { get; }

    /// <summary>Supplies the main window handle for owner-parented shell dialogs.</summary>
    public Func<IntPtr> OwnerHandle { get; set; } = () => IntPtr.Zero;

    [ObservableProperty] private BrowserPaneViewModel _activePane;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private RuleSuggestion? _suggestion;
    [ObservableProperty] private Workspace? _detectedWorkspace;
    [ObservableProperty] private ForegroundInfo? _lastExternal;

    public Workspace ActiveWorkspace
    {
        get => State.ActiveWorkspace!;
        set { if (value is not null && value != State.ActiveWorkspace) SwitchWorkspace(value, null, manual: true); }
    }

    public Session ActiveSession
    {
        get => ActiveWorkspace.EnsureSession();
        set { if (value is not null && value != ActiveWorkspace.ActiveSession) SwitchSession(value); }
    }

    public bool IsDualPane
    {
        get => ActiveSession.IsDualPane;
        set
        {
            if (ActiveSession.IsDualPane == value) return;
            ActiveSession.IsDualPane = value;
            if (value && RightPane.Tabs.Count == 0)
                RightPane.Load(new PaneState(), LeftPane.SelectedTab?.Path ?? DefaultLocation());
            if (!value) ActivatePane(LeftPane);
            OnPropertyChanged();
        }
    }

    public bool IsCompact
    {
        get => Settings.IsCompact;
        set { if (Settings.IsCompact != value) { Settings.IsCompact = value; OnPropertyChanged(); } }
    }

    public bool IsTopmost
    {
        get => Settings.AlwaysOnTop;
        set { if (Settings.AlwaysOnTop != value) { Settings.AlwaysOnTop = value; OnPropertyChanged(); } }
    }

    public bool IsLocked
    {
        get => State.WorkspaceLocked;
        set { if (State.WorkspaceLocked != value) { State.WorkspaceLocked = value; OnPropertyChanged(); } }
    }

    public string WindowTitle => $"NOVA — {ActiveWorkspace.Name} · {ActiveSession.Name}";

    public string? SuggestionText => Suggestion is { } s
        ? $"Always use “{s.Workspace.Name}” when {s.Rule.ProcessName}{(s.Rule.TitlePattern is { } t ? $" · “{t}”" : "")} is active?"
        : null;

    partial void OnSuggestionChanged(RuleSuggestion? value) => OnPropertyChanged(nameof(SuggestionText));

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public void Initialize()
    {
        RestoreSession(ActiveSession);
        _autosave.Start();
    }

    public void Save()
    {
        CaptureSession();
        try { _store.Save(State); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Could not save settings: " + ex.Message);
        }
    }

    public void Shutdown()
    {
        _autosave.Stop();
        Save();
        foreach (var t in LeftPane.Tabs.Concat(RightPane.Tabs)) t.Dispose();
    }

    public string DefaultLocation() =>
        ActiveWorkspace.AllPinItems().Where(p => p.Kind == PinKind.Folder)
            .Select(p => VariableExpander.Expand(p.Target, ActiveWorkspace.Variables))
            .FirstOrDefault(Directory.Exists)
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void ShowStatus(string message)
    {
        Status = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    // ── Workspaces & sessions ───────────────────────────────────────────────

    public void SwitchWorkspace(Workspace ws, Session? session, bool manual)
    {
        if (_switching) return;
        _switching = true;
        try
        {
            CaptureSession();
            var changed = ws != State.ActiveWorkspace;
            State.ActiveWorkspaceId = ws.Id;
            ws.LastActivatedAt = DateTimeOffset.Now;
            var target = session ?? ws.EnsureSession();
            ws.ActiveSessionId = target.Id;
            RestoreSession(target);
            RaiseContextChanged();
            if (DetectedWorkspace == ws) DetectedWorkspace = null;

            if (manual && changed && LastExternal is { } fg && Settings.DetectionMode != DetectionMode.Off)
            {
                var suggestion = AssociationLearner.RecordManualSelection(State, fg, ws);
                if (suggestion is not null) Suggestion = suggestion;
            }
        }
        finally { _switching = false; }
    }

    public void SwitchSession(Session session)
    {
        if (_switching) return;
        _switching = true;
        try
        {
            CaptureSession();
            ActiveWorkspace.ActiveSessionId = session.Id;
            RestoreSession(session);
            RaiseContextChanged();
        }
        finally { _switching = false; }
    }

    private void CaptureSession()
    {
        var s = ActiveWorkspace.ActiveSession;
        if (s is null || LeftPane.Tabs.Count == 0) return;
        LeftPane.Capture(s.Left);
        if (s.IsDualPane || RightPane.Tabs.Count > 0) RightPane.Capture(s.Right);
        s.ActivePane = ActivePane.Index;
    }

    private void RestoreSession(Session s)
    {
        s.LastActiveAt = DateTimeOffset.Now;
        var fallback = DefaultLocation();
        LeftPane.Load(s.Left, fallback);
        if (s.IsDualPane || s.Right.Tabs.Count > 0) RightPane.Load(s.Right, fallback);
        else { foreach (var t in RightPane.Tabs) t.Dispose(); RightPane.Tabs.Clear(); RightPane.SelectedTab = null; }
        ActivatePane(s.IsDualPane && s.ActivePane == 1 ? RightPane : LeftPane);
    }

    private void RaiseContextChanged()
    {
        OnPropertyChanged(nameof(ActiveWorkspace));
        OnPropertyChanged(nameof(ActiveSession));
        OnPropertyChanged(nameof(IsDualPane));
        OnPropertyChanged(nameof(WindowTitle));
    }

    public Workspace CreateWorkspace(string name)
    {
        var palette = new[] { "#4F8EF7", "#7C5CFF", "#00B894", "#F39C12", "#E84393", "#00A8C6", "#E5534B", "#8E9AAF" };
        var ws = new Workspace { Name = name, Color = palette[Workspaces.Count % palette.Length] };
        var session = new Session { Name = "Main" };
        session.Left.Tabs.Add(new TabState { Path = ActivePane.SelectedTab?.Path ?? DefaultLocation() });
        ws.Sessions.Add(session);
        ws.ActiveSessionId = session.Id;
        Workspaces.Add(ws);
        SwitchWorkspace(ws, session, manual: false);
        return ws;
    }

    public void DeleteWorkspace(Workspace ws)
    {
        if (Workspaces.Count <= 1) { ShowStatus("NOVA needs at least one workspace."); return; }
        var next = Workspaces.FirstOrDefault(w => w != ws)!;
        if (ws == ActiveWorkspace) SwitchWorkspace(next, null, manual: false);
        Workspaces.Remove(ws);
        State.Learned.RemoveAll(l => l.WorkspaceId == ws.Id);
    }

    public Session CreateSession(string name, bool fromCurrent)
    {
        CaptureSession();
        var s = fromCurrent ? ActiveSession.Clone(name) : new Session { Name = name };
        if (!fromCurrent) s.Left.Tabs.Add(new TabState { Path = DefaultLocation() });
        ActiveWorkspace.Sessions.Add(s);
        SwitchSession(s);
        return s;
    }

    public void DeleteSession(Session s)
    {
        var ws = ActiveWorkspace;
        if (ws.Sessions.Count <= 1) { ShowStatus("A workspace needs at least one session."); return; }
        if (s == ws.ActiveSession) SwitchSession(ws.Sessions.First(x => x != s));
        ws.Sessions.Remove(s);
        foreach (var r in ws.Rules.Where(r => r.SessionId == s.Id)) r.SessionId = null;
    }

    public void ImportWorkspace(Workspace ws)
    {
        Workspaces.Add(ws);
        SwitchWorkspace(ws, null, manual: false);
    }

    // ── Navigation & pins ───────────────────────────────────────────────────

    public void ActivatePane(BrowserPaneViewModel pane)
    {
        if (pane == RightPane && !IsDualPane) pane = LeftPane;
        ActivePane = pane;
        LeftPane.IsActive = pane == LeftPane;
        RightPane.IsActive = pane == RightPane;
    }

    public BrowserPaneViewModel OtherPane => ActivePane == LeftPane ? RightPane : LeftPane;

    public async Task NavigateAsync(string path, OpenTarget target = OpenTarget.Current)
    {
        path = PathUtil.Normalize(path);
        if (!Directory.Exists(path))
        {
            ShowStatus($"Folder not found: {path}");
            return;
        }
        if (IsCompact) IsCompact = false; // navigating implies wanting the browser
        switch (target)
        {
            case OpenTarget.NewTab:
                await ActivePane.NewTabAsync(path);
                break;
            case OpenTarget.OtherPane:
                if (!IsDualPane) IsDualPane = true;
                var other = OtherPane;
                ActivatePane(other);
                if (other.SelectedTab is null) await other.NewTabAsync(path);
                else await other.SelectedTab.NavigateAsync(path);
                break;
            default:
                if (ActivePane.SelectedTab is null) await ActivePane.NewTabAsync(path);
                else await ActivePane.SelectedTab.NavigateAsync(path);
                break;
        }
    }

    internal void OnTabNavigated(string path)
    {
        WorkspaceOps.RecordVisit(ActiveWorkspace, path, Settings.MaxRecents);
        var wl = ActiveSession.WorkingSet.FirstOrDefault(w => PathUtil.PathEquals(w.Path, path));
        if (wl is not null) wl.LastUsedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(WindowTitle));
    }

    public string Resolve(PinItem pin) => VariableExpander.Expand(pin.Target, ActiveWorkspaceFor(pin).Variables);

    private Workspace ActiveWorkspaceFor(PinItem pin) =>
        Workspaces.FirstOrDefault(w => w.AllPinItems().Contains(pin)) ?? ActiveWorkspace;

    public async Task OpenPinAsync(PinItem pin, OpenTarget target = OpenTarget.Current)
    {
        var resolved = Resolve(pin);
        if (pin.Kind == PinKind.Folder)
        {
            await NavigateAsync(resolved, target);
            return;
        }
        var error = ShellLauncher.Launch(pin.Kind, resolved, pin.Arguments,
            pin.WorkingDirectory is { Length: > 0 } wd ? VariableExpander.Expand(wd, ActiveWorkspace.Variables) : null,
            pin.RunAsAdministrator);
        if (error is not null) ShowStatus($"Could not open {pin.Name}: {error}");
    }

    [RelayCommand]
    private Task OpenPin(PinItem? pin) => pin is null ? Task.CompletedTask : OpenPinAsync(pin);

    [RelayCommand]
    private Task OpenLocation(string? path) => path is null ? Task.CompletedTask : NavigateAsync(path);

    public void PinPaths(IEnumerable<string> paths, PinGroup? group = null)
    {
        int n = 0;
        foreach (var p in paths)
        {
            var isDir = Directory.Exists(p);
            if (isDir && WorkspaceOps.IsPinned(ActiveWorkspace, p)) continue;
            WorkspaceOps.PinPath(ActiveWorkspace, p, isDir, group);
            n++;
        }
        if (n > 0) ShowStatus(n == 1 ? "Pinned to " + ActiveWorkspace.Name : $"Pinned {n} items to {ActiveWorkspace.Name}");
        else ShowStatus("Already pinned.");
    }

    [RelayCommand]
    private void PinCurrentFolder()
    {
        if (ActivePane.SelectedTab?.Path is { Length: > 0 } p) PinPaths(new[] { p });
    }

    [RelayCommand]
    private void AddCurrentToWorkingSet()
    {
        if (ActivePane.SelectedTab?.Path is { Length: > 0 } p) AddToWorkingSet(p);
    }

    public void AddToWorkingSet(string path)
    {
        WorkspaceOps.AddToWorkingSet(ActiveSession, path);
        ShowStatus($"Added {PathUtil.DisplayName(path)} to the working set");
    }

    [RelayCommand]
    private void RemoveWorkingLocation(WorkingLocation? wl)
    {
        if (wl is not null) ActiveSession.WorkingSet.Remove(wl);
    }

    [RelayCommand]
    private void PromoteWorkingLocation(WorkingLocation? wl)
    {
        if (wl is null) return;
        var pin = WorkspaceOps.PinFolder(ActiveWorkspace, wl.Path, name: wl.Label);
        ActiveSession.WorkingSet.Remove(wl);
        ShowStatus($"“{pin.Name}” is now a permanent pin");
    }

    [RelayCommand]
    private void ClearWorkingSet() => ActiveSession.WorkingSet.Clear();

    [RelayCommand]
    private void ClearRecents() => ActiveWorkspace.Recents.Clear();

    // ── Detection ───────────────────────────────────────────────────────────

    public void OnForeground(ForegroundInfo fg)
    {
        LastExternal = fg;
        if (Settings.DetectionMode == DetectionMode.Off) { DetectedWorkspace = null; return; }
        var match = DetectionEngine.Match(fg, Workspaces);
        if (match is null) { DetectedWorkspace = null; return; }

        var session = match.Rule.SessionId is { } sid ? match.Workspace.Sessions.FirstOrDefault(s => s.Id == sid) : null;
        var alreadyThere = match.Workspace == ActiveWorkspace && (session is null || session == ActiveSession);
        if (alreadyThere) { DetectedWorkspace = null; return; }

        if (Settings.DetectionMode == DetectionMode.Automatic && !IsLocked)
        {
            SwitchWorkspace(match.Workspace, session, manual: false);
            ShowStatus($"Switched to {match.Workspace.Name} ({fg.BareProcessName})");
        }
        else
        {
            DetectedWorkspace = match.Workspace; // shown as a one-click chip
        }
    }

    [RelayCommand]
    private void AcceptDetected()
    {
        if (DetectedWorkspace is { } ws) SwitchWorkspace(ws, null, manual: false);
        DetectedWorkspace = null;
    }

    [RelayCommand]
    private void AcceptSuggestion()
    {
        if (Suggestion is null) return;
        AssociationLearner.Accept(Suggestion);
        ShowStatus($"Rule added to {Suggestion.Workspace.Name}");
        Suggestion = null;
    }

    [RelayCommand]
    private void DismissSuggestion()
    {
        if (Suggestion is not null) AssociationLearner.Dismiss(Suggestion);
        Suggestion = null;
    }

    public DetectionRule? AddRuleFromLastApp(Workspace ws)
    {
        if (LastExternal is not { } fg || string.IsNullOrEmpty(fg.BareProcessName)) return null;
        var rule = new DetectionRule
        {
            ProcessName = fg.BareProcessName,
            TitlePattern = AssociationLearner.IsMultiProjectHost(fg.ProcessName) ? AssociationLearner.TitleKey(fg.Title) : null,
        };
        ws.Rules.Add(rule);
        return rule;
    }

    // ── Quick capture ───────────────────────────────────────────────────────

    public string? CaptureExplorerFolder(IntPtr foregroundHwnd)
    {
        var folder = ExplorerWindows.GetFolder(foregroundHwnd);
        if (folder is null || !Directory.Exists(folder)) return null;
        WorkspaceOps.AddToWorkingSet(ActiveSession, folder);
        return folder;
    }

    // ── File operations ─────────────────────────────────────────────────────

    public async Task TransferAsync(IReadOnlyList<string> sources, string destination, bool move)
    {
        if (sources.Count == 0) return;
        if (sources.Any(s => PathUtil.IsUnder(destination, s)))
        {
            ShowStatus("Can't move or copy a folder into itself.");
            return;
        }
        var result = move
            ? await ShellFileOperations.MoveAsync(sources, destination, OwnerHandle())
            : await ShellFileOperations.CopyAsync(sources, destination, OwnerHandle());
        Report(result, move ? "Moved" : "Copied", sources.Count);
    }

    public async Task DeleteAsync(IReadOnlyList<string> paths, bool permanent)
    {
        if (paths.Count == 0) return;
        var result = await ShellFileOperations.DeleteAsync(paths, permanent, OwnerHandle());
        Report(result, permanent ? "Deleted" : "Moved to Recycle Bin:", paths.Count);
    }

    public async Task<bool> RenameAsync(string path, string newName)
    {
        var error = FileNameHelper.Validate(newName);
        if (error is not null) { ShowStatus(error); return false; }
        var result = await ShellFileOperations.RenameAsync(path, newName, OwnerHandle());
        if (!result.Success && result.Error is not null) ShowStatus("Rename failed: " + result.Error);
        return result.Success;
    }

    public string? CreateFolder(string parent, string name)
    {
        var error = FileNameHelper.Validate(name);
        if (error is not null) { ShowStatus(error); return null; }
        try
        {
            var unique = FileNameHelper.MakeUnique(name, n => Path.Exists(Path.Combine(parent, n)), isDirectory: true);
            var full = Path.Combine(parent, unique);
            Directory.CreateDirectory(full);
            return unique;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Could not create folder: " + ex.Message);
            return null;
        }
    }

    private void Report(FileOpResult r, string verb, int count)
    {
        if (r.Error is not null) ShowStatus("Operation failed: " + r.Error);
        else if (r.Aborted) ShowStatus("Operation cancelled.");
        else ShowStatus($"{verb} {count} item{(count == 1 ? "" : "s")}");
    }
}
