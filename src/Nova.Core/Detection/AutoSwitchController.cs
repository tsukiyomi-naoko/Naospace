using Nova.Core.Model;

namespace Nova.Core.Detection;

public sealed record AssociationSuggestion(ForegroundInfo Window, string WorkspaceId, string SuggestedTitleToken);

public enum AssociationChoice
{
    JustThisTime,
    RememberWindow,
    RememberApplication,
    NeverForApplication,
}

/// <summary>
/// Decides when the foreground application should change the active workspace. Pure logic, no Win32, fully testable.
/// Rules:
/// <list type="bullet">
/// <item>Never switches while locked or disabled.</item>
/// <item>Never switches when no rule matches (unrelated apps do not disturb the current workspace).</item>
/// <item>NOVA's own windows are ignored.</item>
/// <item>After a manual selection, automatic switching is suspended until a different window comes to the foreground.</item>
/// </list>
/// It only returns decisions; it never touches focus or other applications' windows.
/// </summary>
public sealed class AutoSwitchController
{
    private DetectionEngine _engine;
    private ForegroundInfo? _lastExternal;
    private ForegroundInfo? _overrideWindow;

    public AutoSwitchController(DetectionEngine engine, string ownExecutableName)
    {
        _engine = engine;
        OwnExecutable = DetectionEngine.NormalizeExe(ownExecutableName);
    }

    public string OwnExecutable { get; }
    public bool Enabled { get; set; } = true;
    public bool Locked { get; set; }
    public string? ActiveWorkspaceId { get; private set; }
    public ForegroundInfo? LastExternalWindow => _lastExternal;

    public void ReplaceEngine(DetectionEngine engine) => _engine = engine;

    /// <summary>Informs the controller about the current workspace without implying a manual choice.</summary>
    public void SetActiveWorkspace(string? workspaceId) => ActiveWorkspaceId = workspaceId;

    /// <summary>Processes a foreground change. Returns the workspace id to switch to, or null to do nothing.</summary>
    public string? OnForegroundChanged(ForegroundInfo info)
    {
        if (string.IsNullOrEmpty(info.ExecutableName) || info.NormalizedExe == OwnExecutable)
            return null;

        _lastExternal = info;

        if (_overrideWindow is { } ov)
        {
            if (ov.NormalizedExe == info.NormalizedExe && string.Equals(ov.Title, info.Title, StringComparison.Ordinal))
                return null;
            _overrideWindow = null;
        }

        if (!Enabled || Locked) return null;

        var target = _engine.ResolveWorkspace(info);
        if (target == null || target == ActiveWorkspaceId) return null;
        ActiveWorkspaceId = target;
        return target;
    }

    /// <summary>
    /// Records a manual workspace selection. Returns a suggestion to remember an association with the window that was
    /// active before the user came to NOVA, if that window is not already associated with the chosen workspace.
    /// </summary>
    public AssociationSuggestion? OnManualSelection(string workspaceId)
    {
        ActiveWorkspaceId = workspaceId;
        if (_lastExternal is not { } last) return null;
        _overrideWindow = last;

        var match = _engine.Match(last);
        if (match != null && (match.IsSuppression || match.Rule.WorkspaceId == workspaceId))
            return null;
        return new AssociationSuggestion(last, workspaceId, TitleHeuristics.SuggestProjectToken(last.Title));
    }

    /// <summary>Converts the user's answer into a new rule (or null for "just this time").</summary>
    public static DetectionRule? CreateRule(AssociationSuggestion s, AssociationChoice choice, string? editedTitleToken = null)
    {
        var exe = s.Window.ExecutableName;
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
        switch (choice)
        {
            case AssociationChoice.RememberWindow:
                var token = string.IsNullOrWhiteSpace(editedTitleToken) ? s.SuggestedTitleToken : editedTitleToken.Trim();
                if (string.IsNullOrWhiteSpace(token)) goto case AssociationChoice.RememberApplication;
                return new DetectionRule { ExecutableName = exe, TitleContains = token, WorkspaceId = s.WorkspaceId, Note = "Learned from manual selection" };
            case AssociationChoice.RememberApplication:
                return new DetectionRule { ExecutableName = exe, WorkspaceId = s.WorkspaceId, Note = "Learned from manual selection" };
            case AssociationChoice.NeverForApplication:
                return new DetectionRule { ExecutableName = exe, Action = RuleAction.NeverSwitch, Note = "Never switch automatically" };
            default:
                return null;
        }
    }
}
