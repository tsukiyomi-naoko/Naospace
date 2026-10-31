namespace Nova.Core.IO;

public readonly record struct PathSegment(string Label, string FullPath);

/// <summary>Splits a location into clickable breadcrumb segments.</summary>
public static class PathSegments
{
    public static IReadOnlyList<PathSegment> Split(string? path, string? virtualDisplayName = null)
    {
        var result = new List<PathSegment>();
        if (string.IsNullOrWhiteSpace(path)) return result;
        if (PathProbe.IsVirtual(path))
        {
            result.Add(new PathSegment(string.IsNullOrWhiteSpace(virtualDisplayName) ? path : virtualDisplayName, path));
            return result;
        }

        var p = path.Replace('/', '\\');
        string root;
        string rest;
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // UNC: \\server\share is the root.
            var parts = p[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return result;
            root = parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : $@"\\{parts[0]}";
            rest = string.Join('\\', parts.Skip(2));
        }
        else if (p.Length >= 2 && p[1] == ':')
        {
            root = p[..2].ToUpperInvariant() + "\\";
            rest = p.Length > 3 ? p[3..] : string.Empty;
        }
        else
        {
            var parts = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return result;
            root = parts[0];
            rest = string.Join('\\', parts.Skip(1));
        }

        result.Add(new PathSegment(root, root));
        var current = root;
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.EndsWith('\\') ? current + part : current + "\\" + part;
            result.Add(new PathSegment(part, current));
        }
        return result;
    }

    /// <summary>Parent location, or null at a root. Drive roots return the "This PC" shell folder.</summary>
    public static string? Parent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || PathProbe.IsVirtual(path)) return null;
        var segs = Split(path);
        if (segs.Count >= 2) return segs[^2].FullPath;
        if (segs.Count == 1 && segs[0].FullPath.Length == 3 && segs[0].FullPath[1] == ':')
            return ThisPc;
        return null;
    }

    public const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
}
