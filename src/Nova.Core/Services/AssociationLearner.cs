using Nova.Core.Models;

namespace Nova.Core.Services;

public sealed record RuleSuggestion(Workspace Workspace, DetectionRule Rule, LearnedAssociation Source);

/// <summary>
/// Learns from manual workspace selections. After the user picks the same workspace
/// <see cref="Threshold"/> times while the same app was in front, NOVA offers to remember
/// it as a rule. Nothing is ever applied silently.
/// </summary>
public static class AssociationLearner
{
    public const int Threshold = 3;

    /// <summary>Apps that host many unrelated projects; their window title matters.</summary>
    private static readonly HashSet<string> MultiProjectHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc",
        "code", "code - insiders", "cursor", "windsurf", "devenv", "rider64", "idea64", "pycharm64",
        "explorer", "windowsterminal", "cmd", "powershell", "pwsh", "notepad++",
    };

    public static bool IsMultiProjectHost(string processName) =>
        MultiProjectHosts.Contains(DetectionEngine.Bare(processName));

    /// <summary>
    /// Extracts a stable project token from a title: drop the trailing app name, keep the
    /// last remaining segment. "main.py - my-repo - Visual Studio Code" → "my-repo",
    /// "ComfyUI - Google Chrome" → "ComfyUI".
    /// </summary>
    public static string? TitleKey(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var parts = title.Split(new[] { " - ", " — ", " – ", " | " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2) parts = parts[..^1];
        var key = parts[^1].TrimStart('●', '*', ' ').Trim();
        return key.Length is > 0 and <= 80 ? key : null;
    }

    public static RuleSuggestion? RecordManualSelection(NovaState state, ForegroundInfo fg, Workspace chosen)
    {
        var proc = fg.BareProcessName;
        if (string.IsNullOrWhiteSpace(proc)) return null;

        // Already handled by an existing rule? Nothing to learn.
        var current = DetectionEngine.Match(fg, state.Workspaces);
        if (current?.Workspace.Id == chosen.Id) return null;

        var titleKey = IsMultiProjectHost(proc) ? TitleKey(fg.Title) : null;
        if (IsMultiProjectHost(proc) && titleKey is null) return null;

        var entry = state.Learned.FirstOrDefault(l =>
            l.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.TitleKey, titleKey, StringComparison.OrdinalIgnoreCase) &&
            l.WorkspaceId == chosen.Id);
        if (entry is null)
        {
            entry = new LearnedAssociation { ProcessName = proc, TitleKey = titleKey, WorkspaceId = chosen.Id };
            state.Learned.Add(entry);
        }
        entry.Count++;
        entry.LastSeen = DateTimeOffset.Now;

        // Keep the learning table small.
        if (state.Learned.Count > 200)
            state.Learned.RemoveAll(l => l.LastSeen < DateTimeOffset.Now.AddDays(-60));

        if (entry.Dismissed || entry.Count < Threshold) return null;
        var rule = new DetectionRule { ProcessName = proc, TitlePattern = titleKey };
        return new RuleSuggestion(chosen, rule, entry);
    }

    public static void Accept(RuleSuggestion s)
    {
        s.Workspace.Rules.Add(s.Rule);
        s.Source.Count = 0;
    }

    public static void Dismiss(RuleSuggestion s) => s.Source.Dismissed = true;
}
