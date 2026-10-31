using System.Text.RegularExpressions;
using Nova.Core.Models;
using Nova.Core.Util;

namespace Nova.Core.Services;

/// <summary>
/// Resolves ${VAR} workspace variables and %ENV% variables in pin targets, and does the
/// reverse ("collapse") when pinning, so a workspace stays portable: move ComfyUI to
/// another drive, change one variable, and every pin follows.
/// </summary>
public static partial class VariableExpander
{
    [GeneratedRegex(@"\$\{(?<n>[A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex VarRegex();

    public static string Expand(string input, IEnumerable<WorkspaceVariable> variables, bool expandEnvironment = true)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var map = variables
            .Where(v => !string.IsNullOrWhiteSpace(v.Name))
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

        var result = input;
        for (int depth = 0; depth < 8 && result.Contains("${"); depth++)
        {
            var next = VarRegex().Replace(result, m =>
                map.TryGetValue(m.Groups["n"].Value, out var val) ? val : m.Value);
            if (next == result) break;
            result = next;
        }
        return expandEnvironment ? Environment.ExpandEnvironmentVariables(result) : result;
    }

    /// <summary>Rewrites an absolute path to use the most specific matching variable, if any.</summary>
    public static string Collapse(string path, IEnumerable<WorkspaceVariable> variables)
    {
        var all = variables as IReadOnlyCollection<WorkspaceVariable> ?? variables.ToList();
        WorkspaceVariable? best = null;
        string? bestRoot = null;
        foreach (var v in all)
        {
            if (string.IsNullOrWhiteSpace(v.Name) || string.IsNullOrWhiteSpace(v.Value)) continue;
            var root = PathUtil.Normalize(Expand(v.Value, all));
            if (root.Contains("${")) continue; // unresolved or cyclic
            if (!PathUtil.IsUnder(path, root)) continue;
            if (bestRoot is null || root.Length > bestRoot.Length) { best = v; bestRoot = root; }
        }
        if (best is null || bestRoot is null) return path;
        var normalized = PathUtil.Normalize(path);
        var rest = normalized.Length > bestRoot.Length ? normalized[bestRoot.Length..].TrimStart('\\', '/') : "";
        return rest.Length == 0 ? $"${{{best.Name}}}" : $"${{{best.Name}}}\\{rest}";
    }
}
