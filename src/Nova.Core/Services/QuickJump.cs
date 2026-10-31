using Nova.Core.Models;
using Nova.Core.Util;

namespace Nova.Core.Services;

public enum JumpKind { Pin, WorkingLocation, Recent, Workspace, Session }

public sealed record JumpTarget(JumpKind Kind, string Title, string Subtitle, Workspace Workspace, object Payload)
{
    public string SearchText => $"{Title} {Subtitle}";
}

/// <summary>
/// Builds the candidate list for the Ctrl+K palette: every pin, working location,
/// recent folder, session and workspace, across all workspaces — the current one ranked first.
/// </summary>
public static class QuickJump
{
    public static IEnumerable<JumpTarget> Candidates(NovaState state)
    {
        var active = state.ActiveWorkspace;
        foreach (var ws in state.Workspaces.OrderByDescending(w => w == active))
        {
            yield return new JumpTarget(JumpKind.Workspace, ws.Name, "Workspace", ws, ws);
            foreach (var s in ws.Sessions)
                yield return new JumpTarget(JumpKind.Session, s.Name, $"{ws.Name} · session", ws, s);
            foreach (var p in ws.AllPinItems())
                yield return new JumpTarget(JumpKind.Pin, p.Name, $"{ws.Name} · {VariableExpander.Expand(p.Target, ws.Variables)}", ws, p);
            foreach (var w in ws.Sessions.SelectMany(s => s.WorkingSet))
                yield return new JumpTarget(JumpKind.WorkingLocation, w.Label ?? PathUtil.DisplayName(w.Path), $"{ws.Name} · {w.Path}", ws, w);
            foreach (var r in ws.Recents.Take(25))
                yield return new JumpTarget(JumpKind.Recent, PathUtil.DisplayName(r.Path), $"{ws.Name} · {r.Path}", ws, r);
        }
    }

    public static IReadOnlyList<JumpTarget> Search(NovaState state, string query, int take = 40)
    {
        var active = state.ActiveWorkspace;
        var all = Candidates(state).ToList();
        if (string.IsNullOrWhiteSpace(query))
            return all.Where(c => c.Workspace == active && c.Kind is JumpKind.Pin or JumpKind.WorkingLocation or JumpKind.Recent)
                      .Take(take).ToList();
        return FuzzyMatcher.Rank(query, all, c => c.SearchText, take,
            c => (c.Workspace == active ? 6 : 0) + c.Kind switch
            {
                JumpKind.Pin => 4, JumpKind.WorkingLocation => 3, JumpKind.Workspace => 2, _ => 0,
            });
    }
}
