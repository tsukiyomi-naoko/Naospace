namespace Nova.Core.Util;

public static class FileNameHelper
{
    private static readonly char[] InvalidChars = "<>:\"/\\|?*".ToCharArray();
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Validates a single file/folder name against Windows rules. Returns null when valid.</summary>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Name cannot be empty.";
        if (name.IndexOfAny(InvalidChars) >= 0) return "Names cannot contain < > : \" / \\ | ? *";
        if (name.Any(c => c < 32)) return "Names cannot contain control characters.";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "Names cannot end with a period or space.";
        var stem = name.Split('.')[0];
        if (Reserved.Contains(stem)) return $"\"{stem}\" is reserved by Windows.";
        if (name.Length > 255) return "Name is too long.";
        return null;
    }

    /// <summary>"New folder" → "New folder (2)", "a.txt" → "a (2).txt" until <paramref name="exists"/> is false.</summary>
    public static string MakeUnique(string name, Func<string, bool> exists, bool isDirectory = false)
    {
        if (!exists(name)) return name;
        string stem = name, ext = "";
        if (!isDirectory)
        {
            var dot = name.LastIndexOf('.');
            if (dot > 0) { stem = name[..dot]; ext = name[dot..]; }
        }
        for (int i = 2; i < 10_000; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!exists(candidate)) return candidate;
        }
        return $"{stem} ({Guid.NewGuid():N}){ext}";
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }
}
