namespace Nova.Core.Model;

public sealed class TabState
{
    public string Id { get; set; } = Ids.New();
    public NavigationHistory History { get; set; } = new();

    public string? CurrentPath => History.Current;

    public static TabState At(string path)
    {
        var t = new TabState();
        t.History.Navigate(path);
        return t;
    }

    public TabState Clone(bool newId = false) => new() { Id = newId ? Ids.New() : Id, History = History.Clone() };
}

public sealed class PaneState
{
    public List<TabState> Tabs { get; set; } = new();
    public int ActiveTabIndex { get; set; }

    public TabState? ActiveTab => ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count ? Tabs[ActiveTabIndex] : null;

    public void Normalize()
    {
        Tabs ??= new();
        Tabs.RemoveAll(t => t == null);
        foreach (var t in Tabs)
        {
            t.History ??= new();
            t.History.Normalize();
            if (string.IsNullOrEmpty(t.Id)) t.Id = Ids.New();
        }
        if (Tabs.Count == 0) ActiveTabIndex = -1;
        else ActiveTabIndex = Math.Clamp(ActiveTabIndex, 0, Tabs.Count - 1);
    }

    public PaneState Clone(bool newIds = false) => new()
    {
        Tabs = Tabs.Select(t => t.Clone(newIds)).ToList(),
        ActiveTabIndex = ActiveTabIndex,
    };
}

/// <summary>
/// A named working session: the open tabs/panes/layout of one task inside a workspace.
/// A workspace can hold several sessions and switch between them without rebuilding anything.
/// </summary>
public sealed class WorkspaceSession
{
    public const int PaneCount = 2;

    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "Default";
    public bool DualPane { get; set; }
    public int ActivePaneIndex { get; set; }
    public double SplitRatio { get; set; } = 0.5;
    public bool BrowserExpanded { get; set; } = true;
    public List<PaneState> Panes { get; set; } = new() { new PaneState(), new PaneState() };
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;

    public PaneState ActivePane => Panes[Math.Clamp(ActivePaneIndex, 0, Panes.Count - 1)];

    public void Normalize()
    {
        Panes ??= new();
        Panes.RemoveAll(p => p == null);
        while (Panes.Count < PaneCount) Panes.Add(new PaneState());
        if (Panes.Count > PaneCount) Panes.RemoveRange(PaneCount, Panes.Count - PaneCount);
        foreach (var p in Panes) p.Normalize();
        if (!DualPane) ActivePaneIndex = 0;
        ActivePaneIndex = Math.Clamp(ActivePaneIndex, 0, PaneCount - 1);
        if (double.IsNaN(SplitRatio) || SplitRatio < 0.1 || SplitRatio > 0.9) SplitRatio = 0.5;
        if (string.IsNullOrWhiteSpace(Name)) Name = "Session";
        if (string.IsNullOrEmpty(Id)) Id = Ids.New();
    }

    public WorkspaceSession Clone(bool newIds = false) => new()
    {
        Id = newIds ? Ids.New() : Id,
        Name = Name,
        DualPane = DualPane,
        ActivePaneIndex = ActivePaneIndex,
        SplitRatio = SplitRatio,
        BrowserExpanded = BrowserExpanded,
        Panes = Panes.Select(p => p.Clone(newIds)).ToList(),
        Modified = Modified,
    };
}

/// <summary>A manual, named copy of a session's navigation/layout. Never contains file contents.</summary>
public sealed class SessionSnapshot
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "Snapshot";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public string SourceSessionName { get; set; } = string.Empty;
    public WorkspaceSession Session { get; set; } = new();
}

/// <summary>A temporary location used for the current task. Separate from permanent pins.</summary>
public sealed class WorkingLocation
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset Added { get; set; } = DateTimeOffset.UtcNow;

    public string DisplayLabel => !string.IsNullOrWhiteSpace(Label) ? Label : PinNode.DeriveLabel(Path, PinKind.Folder);
}
