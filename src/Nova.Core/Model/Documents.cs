namespace Nova.Core.Model;

/// <summary>Every persisted file carries a schema version so it can be migrated and so that a file written by a
/// newer NOVA version is never silently overwritten by an older one.</summary>
public interface IVersionedDocument
{
    int SchemaVersion { get; set; }
}

/// <summary>Permanent definition of one workspace (portable: exported/imported as-is).</summary>
public sealed class WorkspaceDefinition
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "New workspace";

    /// <summary>A short glyph (emoji or one or two characters) shown as the workspace icon.</summary>
    public string Icon { get; set; } = "▣";

    public string Notes { get; set; } = string.Empty;
    public List<PinNode> Pins { get; set; } = new();
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>workspaces.json</summary>
public sealed class WorkspaceCatalog : IVersionedDocument
{
    public const int CurrentVersion = 1;
    public int SchemaVersion { get; set; } = CurrentVersion;
    public List<WorkspaceDefinition> Workspaces { get; set; } = new();
}

/// <summary>state/&lt;workspaceId&gt;.json — the frequently-changing, transient side of a workspace.</summary>
public sealed class WorkspaceState : IVersionedDocument
{
    public const int CurrentVersion = 1;
    public int SchemaVersion { get; set; } = CurrentVersion;
    public string WorkspaceId { get; set; } = string.Empty;
    public string ActiveSessionId { get; set; } = string.Empty;
    public List<WorkspaceSession> Sessions { get; set; } = new();
    public List<WorkingLocation> WorkingLocations { get; set; } = new();
    public RecentList RecentFolders { get; set; } = new();
    public RecentList RecentFiles { get; set; } = new();

    public WorkspaceSession ActiveSession
    {
        get
        {
            var s = Sessions.FirstOrDefault(x => x.Id == ActiveSessionId);
            if (s != null) return s;
            if (Sessions.Count == 0) Sessions.Add(new WorkspaceSession());
            ActiveSessionId = Sessions[0].Id;
            return Sessions[0];
        }
    }

    public bool AddWorkingLocation(string path, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (WorkingLocations.Any(w => PathComparer.Equal(w.Path, path))) return false;
        WorkingLocations.Add(new WorkingLocation { Path = PathComparer.Normalize(path), Label = label ?? string.Empty });
        return true;
    }

    public WorkspaceSession AddSession(string name, WorkspaceSession? template = null)
    {
        var s = template?.Clone(newIds: true) ?? new WorkspaceSession();
        s.Name = string.IsNullOrWhiteSpace(name) ? "Session" : name.Trim();
        s.Modified = DateTimeOffset.UtcNow;
        Sessions.Add(s);
        return s;
    }

    /// <summary>Removes a session; the last remaining session can never be removed.</summary>
    public bool RemoveSession(string id)
    {
        if (Sessions.Count <= 1) return false;
        var removed = Sessions.RemoveAll(s => s.Id == id) > 0;
        if (removed && ActiveSessionId == id) ActiveSessionId = Sessions[0].Id;
        return removed;
    }
}

/// <summary>snapshots/&lt;workspaceId&gt;.json</summary>
public sealed class SnapshotCollection : IVersionedDocument
{
    public const int CurrentVersion = 1;
    public int SchemaVersion { get; set; } = CurrentVersion;
    public string WorkspaceId { get; set; } = string.Empty;
    public List<SessionSnapshot> Snapshots { get; set; } = new();

    public SessionSnapshot Capture(WorkspaceSession session, string name)
    {
        var snap = new SessionSnapshot
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Snapshot {DateTime.Now:g}" : name.Trim(),
            SourceSessionName = session.Name,
            Session = session.Clone(newIds: false),
        };
        Snapshots.Insert(0, snap);
        return snap;
    }

    /// <summary>Restores a snapshot into <paramref name="target"/> (keeping the target's id and name).</summary>
    public static void RestoreInto(SessionSnapshot snapshot, WorkspaceSession target)
    {
        var copy = snapshot.Session.Clone(newIds: true);
        target.DualPane = copy.DualPane;
        target.ActivePaneIndex = copy.ActivePaneIndex;
        target.SplitRatio = copy.SplitRatio;
        target.BrowserExpanded = copy.BrowserExpanded;
        target.Panes = copy.Panes;
        target.Modified = DateTimeOffset.UtcNow;
        target.Normalize();
    }
}

public enum ThemeMode { System, Light, Dark }

public enum DisplayMode
{
    /// <summary>Movable, resizable window.</summary>
    Floating,
    /// <summary>Registered Windows AppBar that reserves desktop space on a screen edge.</summary>
    Docked,
    /// <summary>Stays on the desktop below other applications (never above them).</summary>
    Desktop,
}

public enum DockEdge { Left, Right }

public sealed class WindowPlacement
{
    /// <summary>Bounds in device-independent units (1/96 inch) in virtual-screen coordinates.</summary>
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 720;

    /// <summary>Device name of the monitor the window belongs to (e.g. \\.\DISPLAY2). Informational; bounds are re-validated on load.</summary>
    public string MonitorDeviceName { get; set; } = string.Empty;

    public DisplayMode Mode { get; set; } = DisplayMode.Floating;
    public DockEdge DockEdge { get; set; } = DockEdge.Left;
    public bool Topmost { get; set; }

    /// <summary>When true only the sidebar is shown (browser collapsed, tabs preserved).</summary>
    public bool Compact { get; set; }
    public double SidebarWidth { get; set; } = 280;
    public double DockedWidth { get; set; } = 300;
    public bool Maximized { get; set; }
}

/// <summary>settings.json — user preferences that are not part of any workspace.</summary>
public sealed class AppSettings : IVersionedDocument
{
    public const int CurrentVersion = 1;
    public int SchemaVersion { get; set; } = CurrentVersion;

    public string SummonHotkey { get; set; } = "Ctrl+Alt+N";
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool CloseToTray { get; set; } = true;
    public bool StartHidden { get; set; }
    public bool AutoDetectionEnabled { get; set; } = true;
    public bool OfferAssociationOnManualSwitch { get; set; } = true;
    public bool ExpandBrowserWhenOpeningFolder { get; set; } = true;
    public bool ConfirmTabClose { get; set; }

    public string ActiveWorkspaceId { get; set; } = string.Empty;
    public bool WorkspaceLocked { get; set; }

    public WindowPlacement Window { get; set; } = new();
}

public enum RuleAction
{
    /// <summary>Switch to the associated workspace when the rule matches.</summary>
    Associate,
    /// <summary>Never auto-switch while this application/window is in the foreground.</summary>
    NeverSwitch,
}

/// <summary>
/// One transparent, user-editable detection rule: foreground executable name and/or window title → workspace.
/// Absolute executable paths are deliberately not required because they change between application versions.
/// </summary>
public sealed class DetectionRule
{
    public string Id { get; set; } = Ids.New();
    public bool Enabled { get; set; } = true;
    public RuleAction Action { get; set; } = RuleAction.Associate;
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Executable file name, e.g. "Code.exe" (".exe" optional). Empty = any executable.</summary>
    public string ExecutableName { get; set; } = string.Empty;

    /// <summary>Case-insensitive substring the window title must contain. Empty = no title condition.</summary>
    public string TitleContains { get; set; } = string.Empty;

    /// <summary>Optional .NET regular expression the title must match (case-insensitive).</summary>
    public string TitlePattern { get; set; } = string.Empty;

    /// <summary>Higher wins when several rules of equal specificity match.</summary>
    public int Priority { get; set; }

    public string Note { get; set; } = string.Empty;
}

/// <summary>rules.json</summary>
public sealed class RuleSet : IVersionedDocument
{
    public const int CurrentVersion = 1;
    public int SchemaVersion { get; set; } = CurrentVersion;
    public List<DetectionRule> Rules { get; set; } = new();
}
