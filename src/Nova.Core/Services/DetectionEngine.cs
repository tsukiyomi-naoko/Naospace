using Nova.Core.Models;
using Nova.Core.Util;

namespace Nova.Core.Services;

/// <summary>What NOVA knows about the foreground window.</summary>
public sealed record ForegroundInfo(string ProcessName, string Title)
{
    /// <summary>Executable name without directory or ".exe", e.g. "Code".</summary>
    public string BareProcessName => DetectionEngine.Bare(ProcessName);
}

public sealed record DetectionMatch(Workspace Workspace, DetectionRule Rule, int Score);

/// <summary>
/// Deliberately simple, predictable rule matching: executable name (+ optional title filter).
/// The most specific enabled rule wins; ties go to priority, then declaration order.
/// </summary>
public static class DetectionEngine
{
    public static string Bare(string processName)
    {
        var name = processName;
        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }

    public static int? ScoreRule(DetectionRule rule, ForegroundInfo fg)
    {
        if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.ProcessName)) return null;
        var pattern = Bare(rule.ProcessName.Trim());
        var proc = fg.BareProcessName;
        int score;
        if (WildcardPattern.HasWildcards(pattern))
        {
            if (!WildcardPattern.IsMatch(proc, pattern)) return null;
            score = 10;
        }
        else
        {
            if (!proc.Equals(pattern, StringComparison.OrdinalIgnoreCase)) return null;
            score = 20;
        }
        if (!string.IsNullOrWhiteSpace(rule.TitlePattern))
        {
            if (!WildcardPattern.MatchesFilter(fg.Title, rule.TitlePattern!)) return null;
            score += 30 + Math.Min(rule.TitlePattern!.Length, 20);
        }
        return score;
    }

    public static DetectionMatch? Match(ForegroundInfo fg, IEnumerable<Workspace> workspaces)
    {
        DetectionMatch? best = null;
        foreach (var ws in workspaces)
        foreach (var rule in ws.Rules)
        {
            var s = ScoreRule(rule, fg);
            if (s is null) continue;
            if (best is null || s > best.Score || (s == best.Score && rule.Priority > best.Rule.Priority))
                best = new DetectionMatch(ws, rule, s.Value);
        }
        return best;
    }
}
