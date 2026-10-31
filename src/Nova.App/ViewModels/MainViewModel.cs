using System.Collections.ObjectModel;
using System.Windows.Threading;
using Nova.App.Infrastructure;
using Nova.App.Services;
using Nova.Core.Detection;
using Nova.Core.Diagnostics;
using Nova.Core.Model;
using Nova.Core.Persistence;
using Nova.Windows.Shell;

namespace Nova.App.ViewModels;

public enum OpenTarget { CurrentTab, NewTab, OtherPane }

public sealed class MainViewModel : ObservableObject
{
    private WorkspaceViewModel? _active;
    private AssociationSuggestion? _suggestion;
    private string _suggestionToken = string.Empty;
    private string _status = "Ready";
    private StatusKind _statusKind;
    private readonly DispatcherTimer _statusTimer;

    public MainViewModel()
    {
        foreach (var def in AppServices.Catalog.Workspaces)
            Workspaces.Add(LoadWorkspace(def));

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); SetStatus("Ready", StatusKind.Info, autoClear: false); };
        AppServices.StatusChanged += (m, k) => SetStatus(m, k, autoClear: k == StatusKind.Info);
        AppServices.AutoSwitch.Locked = AppServices.Settings.WorkspaceLocked;
        AppServices.AutoSwitch.Enabled = AppServices.Settings.AutoDetectionEnabled;

        var initial = Workspaces.FirstOrDefault(w => w.Id == AppServices.Settings.ActiveWorkspaceId) ?? Workspaces.FirstOrDefault();
        if (initial != null) Activate(initial, manual: false);
    }

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = new();

    /// <summary>Raised after the active workspace changed (manually or automatically).</summary>
    public event EventHandler? ActiveWorkspaceChanged;

    /// <summary>Raised when the active tab or its location changes in the active workspace.</summary>
    public event EventHandler? ActiveTabChanged;

    /// <summary>Bound to the workspace selector; a user selection is a manual choice.</summary>
    public WorkspaceViewModel? ActiveWorkspace
    {
        get => _active;
        set { if (value != null && !ReferenceEquals(value, _active)) Activate(value, manual: true); }
    }

    public TabViewModel? ActiveTab => _active?.ActiveTab;

    public void Activate(WorkspaceViewModel ws, bool manual)
    {
        if (_active != null) _active.ActiveTabChanged -= OnWorkspaceActiveTabChanged;
        _active = ws;
        _active.ActiveTabChanged += OnWorkspaceActiveTabChanged;
        OnPropertyChanged(nameof(ActiveWorkspace));
        OnPropertyChanged(nameof(ActiveTab));

        AppServices.Settings.ActiveWorkspaceId = ws.Id;
        AppServices.SaveSettings();

        if (manual)
        {
            var s = AppServices.AutoSwitch.OnManualSelection(ws.Id);
            Suggestion = AppServices.Settings.OfferAssociationOnManualSwitch && AppServices.Settings.AutoDetectionEnabled ? s : null;
        }
        else
        {
            AppServices.AutoSwitch.SetActiveWorkspace(ws.Id);
            Suggestion = null;
        }

        _ = ws.ProbePinsAsync();
        ActiveWorkspaceChanged?.Invoke(this, EventArgs.Empty);
        ActiveTabChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWorkspaceActiveTabChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ActiveTab));
        ActiveTabChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnForegroundChanged(ForegroundInfo info)
    {
        var target = AppServices.AutoSwitch.OnForegroundChanged(info);
        if (target == null) return;
        var ws = Workspaces.FirstOrDefault(w => w.Id == target);
        if (ws == null || ReferenceEquals(ws, _active)) return;
        Activate(ws, manual: false);
        SetStatus($"Switched to {ws.Name} ({info.ExecutableName})", StatusKind.Info, autoClear: true);
    }

    // ---------- Lock ----------

    public bool IsLocked
    {
        get => AppServices.Settings.WorkspaceLocked;
        set
        {
            AppServices.Settings.WorkspaceLocked = value;
            AppServices.AutoSwitch.Locked = value;
            AppServices.SaveSettings();
            OnPropertyChanged();
            SetStatus(value ? "Workspace locked: automatic switching is paused." : "Workspace unlocked.", StatusKind.Info, true);
        }
    }

    // ---------- Association suggestion ("remember this?") ----------

    public AssociationSuggestion? Suggestion
    {
        get => _suggestion;
        private set
        {
            SetProperty(ref _suggestion, value);
            SuggestionToken = value?.SuggestedTitleToken ?? string.Empty;
            OnPropertyChanged(nameof(HasSuggestion));
            OnPropertyChanged(nameof(SuggestionText));
        }
    }

    public bool HasSuggestion => _suggestion != null;

    public string SuggestionText => _suggestion == null ? string.Empty :
        $"Use \"{Workspaces.FirstOrDefault(w => w.Id == _suggestion.WorkspaceId)?.Name}\" automatically for {_suggestion.Window.ExecutableName}?";

    public string SuggestionToken
    {
        get => _suggestionToken;
        set => SetProperty(ref _suggestionToken, value);
    }

    public void AnswerSuggestion(AssociationChoice choice)
    {
        if (_suggestion == null) return;
        var rule = AutoSwitchController.CreateRule(_suggestion, choice, SuggestionToken);
        if (rule != null)
        {
            AppServices.Rules.Rules.Add(rule);
            AppServices.ReloadRules();
            AppServices.SaveRules();
            SetStatus(rule.Action == RuleAction.NeverSwitch
                ? $"NOVA will not switch automatically for {rule.ExecutableName}."
                : $"Rule added: {rule.ExecutableName}{(string.IsNullOrEmpty(rule.TitleContains) ? "" : $" + \"{rule.TitleContains}\"")}.", StatusKind.Info, true);
        }
        Suggestion = null;
    }

    // ---------- Status ----------

    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public StatusKind StatusKind { get => _statusKind; private set => SetProperty(ref _statusKind, value); }

    public void SetStatus(string message, StatusKind kind, bool autoClear)
    {
        StatusText = message;
        StatusKind = kind;
        _statusTimer.Stop();
        if (autoClear || kind == StatusKind.Warning) _statusTimer.Start();
    }

    // ---------- Workspaces ----------

    private static WorkspaceViewModel LoadWorkspace(WorkspaceDefinition def)
    {
        var state = AppServices.Store.LoadState(def.Id);
        if (state.Notes.Count > 0) AppServices.Status(string.Join(" ", state.Notes), StatusKind.Warning);
        return new WorkspaceViewModel(def, state.Value);
    }

    public WorkspaceViewModel CreateWorkspace(string name, string icon, bool includeDownloads)
    {
        var def = new WorkspaceDefinition { Name = name.Trim(), Icon = string.IsNullOrWhiteSpace(icon) ? "▣" : icon.Trim() };
        if (includeDownloads) def.Pins.Add(PinNode.Folder(ShellNative.DownloadsFolder, "Downloads"));
        AppServices.Catalog.Workspaces.Add(def);
        AppServices.SaveCatalog();
        var vm = new WorkspaceViewModel(def, new WorkspaceState { WorkspaceId = def.Id });
        vm.SaveState();
        Workspaces.Add(vm);
        Activate(vm, manual: true);
        return vm;
    }

    public WorkspaceViewModel AddImported(WorkspaceDefinition def, IEnumerable<DetectionRule> rules)
    {
        AppServices.Catalog.Workspaces.Add(def);
        AppServices.SaveCatalog();
        AppServices.Rules.Rules.AddRange(rules);
        AppServices.ReloadRules();
        AppServices.SaveRules();
        var vm = new WorkspaceViewModel(def, new WorkspaceState { WorkspaceId = def.Id });
        vm.SaveState();
        Workspaces.Add(vm);
        Activate(vm, manual: true);
        return vm;
    }

    /// <summary>Deletes a workspace. Its state files are archived in the backup folder, not erased.</summary>
    public bool DeleteWorkspace(WorkspaceViewModel ws)
    {
        if (Workspaces.Count <= 1) return false;
        var index = Workspaces.IndexOf(ws);
        AppServices.Catalog.Workspaces.RemoveAll(w => w.Id == ws.Id);
        AppServices.Rules.Rules.RemoveAll(r => r.WorkspaceId == ws.Id);
        AppServices.ReloadRules();
        AppServices.SaveRules();
        AppServices.SaveCatalog();
        AppServices.Saver.FlushAll();
        AppServices.Store.ArchiveWorkspaceFiles(ws.Id);
        Workspaces.Remove(ws);
        if (ReferenceEquals(_active, ws)) Activate(Workspaces[Math.Clamp(index, 0, Workspaces.Count - 1)], manual: false);
        Log.Info("Workspace deleted (state archived).");
        return true;
    }

    public void MoveWorkspace(WorkspaceViewModel ws, int delta)
    {
        var i = Workspaces.IndexOf(ws);
        var j = Math.Clamp(i + delta, 0, Workspaces.Count - 1);
        if (i < 0 || i == j) return;
        Workspaces.Move(i, j);
        var defs = AppServices.Catalog.Workspaces;
        var d = defs.First(x => x.Id == ws.Id);
        defs.Remove(d);
        defs.Insert(j, d);
        AppServices.SaveCatalog();
        OnPropertyChanged(nameof(ActiveWorkspace));
    }

    // ---------- Opening locations ----------

    /// <summary>Opens a folder inside NOVA's own browser (never a new Explorer window).</summary>
    public void OpenFolder(string path, OpenTarget target)
    {
        var ws = _active;
        if (ws == null) return;
        PaneViewModel pane;
        if (target == OpenTarget.OtherPane)
        {
            ws.DualPane = true;
            pane = ws.OtherPane;
            ws.ActivePaneIndex = pane.Index;
        }
        else
        {
            pane = ws.ActivePane;
        }

        if (pane.ActiveTab == null || target == OpenTarget.NewTab)
        {
            var tab = pane.AddTab(null);
            tab.Navigate(path);
        }
        else
        {
            pane.ActiveTab.Navigate(path);
        }
    }

    public void NewTab(PaneViewModel? pane = null)
    {
        var ws = _active;
        if (ws == null) return;
        pane ??= ws.ActivePane;
        var path = pane.ActiveTab?.Path ?? FirstFolderPin(ws) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        pane.AddTab(path);
    }

    private static string? FirstFolderPin(WorkspaceViewModel ws) =>
        PinTree.Flatten(ws.Definition.Pins).FirstOrDefault(p => p.Kind == PinKind.Folder)?.Target;

    public void MoveTabToOtherPane(TabViewModel tab)
    {
        var ws = _active;
        if (ws == null) return;
        ws.DualPane = true;
        var from = tab.Pane;
        var to = ws.Panes[from.Index == 0 ? 1 : 0];
        from.Remove(tab);
        to.Insert(new TabViewModel(tab.State, to));
        ws.ActivePaneIndex = to.Index;
    }

    public void ReloadAllFromDisk()
    {
        // Used after restoring a backup: drop all view models and rebuild from the freshly loaded documents.
        Workspaces.Clear();
        foreach (var def in AppServices.Catalog.Workspaces)
            Workspaces.Add(LoadWorkspace(def));
        AppServices.ReloadRules();
        var initial = Workspaces.FirstOrDefault(w => w.Id == AppServices.Settings.ActiveWorkspaceId) ?? Workspaces.FirstOrDefault();
        _active = null;
        if (initial != null) Activate(initial, manual: false);
    }
}
