using Nova.Core.Models;
using Nova.Core.Util;

namespace Nova.Core.Services;

/// <summary>Mutations on workspaces that encode product rules (dedupe, caps, ordering).</summary>
public static class WorkspaceOps
{
    public static void RecordVisit(Workspace ws, string path, int max)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = PathUtil.Normalize(path);
        var existing = ws.Recents.FirstOrDefault(r => PathUtil.PathEquals(r.Path, path));
        if (existing is not null)
        {
            existing.VisitCount++;
            existing.LastVisited = DateTimeOffset.Now;
            var idx = ws.Recents.IndexOf(existing);
            if (idx > 0) ws.Recents.Move(idx, 0);
        }
        else
        {
            ws.Recents.Insert(0, new RecentEntry { Path = path });
        }
        while (ws.Recents.Count > Math.Max(5, max)) ws.Recents.RemoveAt(ws.Recents.Count - 1);
    }

    public static WorkingLocation AddToWorkingSet(Session session, string path, string? label = null)
    {
        path = PathUtil.Normalize(path);
        var existing = session.WorkingSet.FirstOrDefault(w => PathUtil.PathEquals(w.Path, path));
        if (existing is not null)
        {
            existing.LastUsedAt = DateTimeOffset.Now;
            if (label is not null) existing.Label = label;
            return existing;
        }
        var wl = new WorkingLocation { Path = path, Label = label };
        session.WorkingSet.Add(wl);
        return wl;
    }

    /// <summary>Creates a folder pin, collapsing the path onto a workspace variable when possible.</summary>
    public static PinItem PinFolder(Workspace ws, string path, PinGroup? group = null, string? name = null)
    {
        var target = VariableExpander.Collapse(PathUtil.Normalize(path), ws.Variables);
        var pin = new PinItem { Kind = PinKind.Folder, Target = target, Name = name ?? PathUtil.DisplayName(path) };
        (group?.Children ?? ws.Pins).Add(pin);
        return pin;
    }

    public static PinItem PinPath(Workspace ws, string path, bool isDirectory, PinGroup? group = null)
    {
        if (isDirectory) return PinFolder(ws, path, group);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var kind = ext switch
        {
            ".exe" or ".lnk" or ".appref-ms" => PinKind.Application,
            ".bat" or ".cmd" or ".ps1" or ".py" or ".sh" => PinKind.Script,
            ".url" => PinKind.Url,
            _ => PinKind.File,
        };
        var name = kind == PinKind.Application ? Path.GetFileNameWithoutExtension(path) : PathUtil.DisplayName(path);
        var pin = new PinItem { Kind = kind, Target = VariableExpander.Collapse(path, ws.Variables), Name = name };
        (group?.Children ?? ws.Pins).Add(pin);
        return pin;
    }

    public static bool IsPinned(Workspace ws, string path) =>
        ws.AllPinItems().Any(p => p.Kind == PinKind.Folder &&
                                  PathUtil.PathEquals(VariableExpander.Expand(p.Target, ws.Variables), path));

    public static bool RemoveNode(Workspace ws, PinNode node)
    {
        var parent = PinNode.FindParentCollection(ws.Pins, node);
        return parent?.Remove(node) ?? false;
    }

    public static void MoveNode(Workspace ws, PinNode node, int delta)
    {
        var parent = PinNode.FindParentCollection(ws.Pins, node);
        if (parent is null) return;
        var i = parent.IndexOf(node);
        var j = Math.Clamp(i + delta, 0, parent.Count - 1);
        if (i != j) parent.Move(i, j);
    }

    public static Session DuplicateSession(Workspace ws, Session source)
    {
        var copy = source.Clone(source.Name + " (copy)");
        ws.Sessions.Insert(ws.Sessions.IndexOf(source) + 1, copy);
        return copy;
    }

    /// <summary>The default state on first launch: one useful, editable workspace.</summary>
    public static NovaState CreateDefaultState()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string downloads = string.IsNullOrEmpty(home) ? "" : Path.Combine(home, "Downloads");

        var ws = new Workspace { Name = "General", Color = "#4F8EF7", Description = "Everyday folders" };
        ws.Variables.Add(new WorkspaceVariable { Name = "HOME", Value = home });
        var group = new PinGroup { Name = "Personal" };
        ws.Pins.Add(group);
        foreach (var (name, path) in new[] { ("Desktop", desktop), ("Documents", docs), ("Downloads", downloads) })
            if (!string.IsNullOrEmpty(path))
                PinFolder(ws, path, group, name);

        var session = new Session { Name = "Main" };
        session.Left.Tabs.Add(new TabState { Path = home });
        ws.Sessions.Add(session);
        ws.ActiveSessionId = session.Id;

        var state = new NovaState();
        state.Workspaces.Add(ws);
        state.ActiveWorkspaceId = ws.Id;
        return state;
    }
}
