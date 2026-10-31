namespace Nova.Core.Util;

/// <summary>
/// Path helpers with Windows semantics (case-insensitive, '\' or '/' separators),
/// written without OS calls so they behave identically in tests on any platform.
/// </summary>
public static class PathUtil
{
    private static readonly char[] Separators = { '\\', '/' };

    public static bool IsSeparator(char c) => c is '\\' or '/';

    /// <summary>Trims trailing separators except for roots like "C:\" or "/".</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var p = path.Trim().Trim('"');
        while (p.Length > 1 && IsSeparator(p[^1]) && !(p.Length == 3 && p[1] == ':'))
            p = p[..^1];
        if (p.Length == 2 && p[1] == ':') p += '\\';
        return p;
    }

    public static bool PathEquals(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(Unify(Normalize(a)), Unify(Normalize(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>True if <paramref name="path"/> equals or lies beneath <paramref name="root"/>.</summary>
    public static bool IsUnder(string path, string root)
    {
        var p = Unify(Normalize(path));
        var r = Unify(Normalize(root));
        if (p.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
        if (!r.EndsWith('/')) r += '/';
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Last path segment, or the root itself ("C:\") for drive roots.</summary>
    public static string DisplayName(string path)
    {
        var p = Normalize(path);
        var idx = p.LastIndexOfAny(Separators);
        if (idx < 0 || idx == p.Length - 1) return p;
        return p[(idx + 1)..];
    }

    /// <summary>Splits a path into cumulative breadcrumb segments: (label, fullPath).</summary>
    public static IReadOnlyList<(string Label, string Path)> Breadcrumbs(string path)
    {
        var result = new List<(string, string)>();
        var p = Normalize(path);
        if (string.IsNullOrEmpty(p)) return result;

        string prefix;
        int start;
        if (p.StartsWith(@"\\"))
        {
            // UNC: \\server\share is the first crumb.
            var parts = p[2..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { result.Add((p, p)); return result; }
            prefix = $@"\\{parts[0]}\{parts[1]}";
            result.Add((prefix, prefix));
            AddRest(parts.Skip(2), prefix, '\\');
            return result;
        }
        if (p.Length >= 2 && p[1] == ':')
        {
            prefix = p[..2] + "\\";
            result.Add((p[..2], prefix));
            start = 3;
            AddRest(SplitFrom(p, start), prefix, '\\');
            return result;
        }
        if (p.StartsWith('/'))
        {
            result.Add(("/", "/"));
            AddRest(SplitFrom(p, 1), "/", '/');
            return result;
        }
        AddRest(SplitFrom(p, 0), "", '\\');
        return result;

        static IEnumerable<string> SplitFrom(string s, int i) =>
            i >= s.Length ? Array.Empty<string>() : s[i..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        void AddRest(IEnumerable<string> parts, string acc, char sep)
        {
            foreach (var part in parts)
            {
                acc = acc.Length == 0 ? part : (IsSeparator(acc[^1]) ? acc + part : acc + sep + part);
                result.Add((part, acc));
            }
        }
    }

    /// <summary>Parent directory, or null at a root.</summary>
    public static string? Parent(string path)
    {
        var crumbs = Breadcrumbs(path);
        return crumbs.Count >= 2 ? crumbs[^2].Path : null;
    }

    private static string Unify(string p) => p.Replace('\\', '/');
}
