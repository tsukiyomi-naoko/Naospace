using System.Text.RegularExpressions;
using Nova.Core.Model;

namespace Nova.Core.Detection;

/// <summary>What NOVA knows about the current foreground window. Never persisted as an identity.</summary>
public readonly record struct ForegroundInfo(string ExecutableName, string Title)
{
    public string NormalizedExe => DetectionEngine.NormalizeExe(ExecutableName);
}

public sealed record MatchResult(DetectionRule Rule, int Specificity)
{
    public bool IsSuppression => Rule.Action == RuleAction.NeverSwitch;
}

/// <summary>
/// Small, predictable rule matcher: executable name + window title → workspace.
/// Ordering: more specific rules (title conditions) beat executable-only rules; then higher priority; then rule order.
/// A matching <see cref="RuleAction.NeverSwitch"/> rule of equal or higher rank suppresses switching.
/// </summary>
public sealed class DetectionEngine
{
    private readonly List<(DetectionRule Rule, Regex? Pattern)> _rules = new();

    public DetectionEngine(IEnumerable<DetectionRule> rules) => Load(rules);

    public void Load(IEnumerable<DetectionRule> rules)
    {
        _rules.Clear();
        foreach (var r in rules)
        {
            if (!r.Enabled) continue;
            if (string.IsNullOrWhiteSpace(r.ExecutableName) && string.IsNullOrWhiteSpace(r.TitleContains) && string.IsNullOrWhiteSpace(r.TitlePattern))
                continue; // A rule without conditions would match everything; ignore it.
            if (r.Action == RuleAction.Associate && string.IsNullOrWhiteSpace(r.WorkspaceId))
                continue;
            Regex? rx = null;
            if (!string.IsNullOrWhiteSpace(r.TitlePattern))
            {
                try
                {
                    rx = new Regex(r.TitlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }
            _rules.Add((r, rx));
        }
    }

    public int Count => _rules.Count;

    public static string NormalizeExe(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return string.Empty;
        var name = exe.Trim();
        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant();
    }

    public MatchResult? Match(ForegroundInfo info)
    {
        MatchResult? best = null;
        var index = 0;
        var bestIndex = int.MaxValue;
        foreach (var (rule, rx) in _rules)
        {
            var specificity = Evaluate(rule, rx, info);
            if (specificity < 0) { index++; continue; }

            var candidate = new MatchResult(rule, specificity);
            if (best == null || Compare(candidate, index, best, bestIndex) > 0)
            {
                best = candidate;
                bestIndex = index;
            }
            index++;
        }
        return best;
    }

    /// <summary>Returns the workspace to switch to, or null when nothing reliable matches or switching is suppressed.</summary>
    public string? ResolveWorkspace(ForegroundInfo info)
    {
        var m = Match(info);
        return m == null || m.IsSuppression ? null : m.Rule.WorkspaceId;
    }

    private static int Compare(MatchResult a, int aIndex, MatchResult b, int bIndex)
    {
        if (a.Specificity != b.Specificity) return a.Specificity.CompareTo(b.Specificity);
        if (a.Rule.Priority != b.Rule.Priority) return a.Rule.Priority.CompareTo(b.Rule.Priority);
        // Equal rank: suppression wins (safer), then earlier rules win.
        if (a.IsSuppression != b.IsSuppression) return a.IsSuppression ? 1 : -1;
        return bIndex.CompareTo(aIndex);
    }

    /// <returns>-1 if no match, otherwise a specificity score.</returns>
    private static int Evaluate(DetectionRule rule, Regex? rx, ForegroundInfo info)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(rule.ExecutableName))
        {
            if (NormalizeExe(rule.ExecutableName) != info.NormalizedExe) return -1;
            score += 1;
        }
        var title = info.Title ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(rule.TitleContains))
        {
            if (title.IndexOf(rule.TitleContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0) return -1;
            score += 2;
        }
        if (rx != null)
        {
            try
            {
                if (!rx.IsMatch(title)) return -1;
            }
            catch (RegexMatchTimeoutException)
            {
                return -1;
            }
            score += 2;
        }
        return score;
    }
}

public static class TitleHeuristics
{
    private static readonly string[] Separators = { " - ", " — ", " – ", " | " };

    /// <summary>
    /// Suggests a project token from a window title for "remember this project/window" associations.
    /// "main.py - MyProject - Visual Studio Code" → "MyProject"; "Report.docx - Word" → "Report.docx".
    /// The user can always edit the suggestion before it is saved.
    /// </summary>
    public static string SuggestProjectToken(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var parts = title.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.TrimStart('●', '*', '•').Trim())
            .Where(p => p.Length > 0)
            .ToArray();
        return parts.Length switch
        {
            0 => title.Trim(),
            1 => parts[0],
            2 => parts[0],
            _ => parts[^2],
        };
    }
}
