namespace Nova.Core.Model;

/// <summary>
/// Per-tab back/forward history. Owned by the model (not by the native browser) so it survives
/// workspace switches, browser disposal and application restarts.
/// </summary>
public sealed class NavigationHistory
{
    public const int DefaultCapacity = 100;

    public List<string> Entries { get; set; } = new();
    public int Index { get; set; } = -1;

    public string? Current => Index >= 0 && Index < Entries.Count ? Entries[Index] : null;
    public bool CanGoBack => Index > 0;
    public bool CanGoForward => Index >= 0 && Index < Entries.Count - 1;

    public void Navigate(string location, int capacity = DefaultCapacity)
    {
        if (string.IsNullOrEmpty(location)) return;
        if (Current != null && PathComparer.Equal(Current, location))
            return;

        if (Index < Entries.Count - 1)
            Entries.RemoveRange(Index + 1, Entries.Count - Index - 1);

        Entries.Add(location);
        Index = Entries.Count - 1;

        if (Entries.Count > capacity)
        {
            var excess = Entries.Count - capacity;
            Entries.RemoveRange(0, excess);
            Index -= excess;
        }
    }

    public string? GoBack()
    {
        if (!CanGoBack) return null;
        Index--;
        return Current;
    }

    public string? GoForward()
    {
        if (!CanGoForward) return null;
        Index++;
        return Current;
    }

    /// <summary>Clamps state after deserialization of possibly hand-edited data.</summary>
    public void Normalize()
    {
        Entries ??= new();
        Entries.RemoveAll(string.IsNullOrWhiteSpace);
        if (Entries.Count == 0) Index = -1;
        else if (Index < 0 || Index >= Entries.Count) Index = Entries.Count - 1;
    }

    public NavigationHistory Clone() => new() { Entries = new List<string>(Entries), Index = Index };
}

public static class PathComparer
{
    /// <summary>Windows paths are case-insensitive; trailing separators are not significant.</summary>
    public static bool Equal(string? a, string? b)
    {
        if (a == null || b == null) return a == b;
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path)
    {
        var p = path.Trim();
        if (p.Length > 3) p = p.TrimEnd('\\', '/');
        return p;
    }
}
