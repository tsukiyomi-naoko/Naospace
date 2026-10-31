namespace Nova.Core.Model;

public sealed class RecentItem
{
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset LastUsed { get; set; }
    public int Count { get; set; }
}

/// <summary>Bounded most-recently-used list with use counts, scoped to one workspace.</summary>
public sealed class RecentList
{
    public int Capacity { get; set; } = 40;
    public List<RecentItem> Items { get; set; } = new();

    public void Touch(string path, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var existing = Items.FindIndex(i => PathComparer.Equal(i.Path, path));
        RecentItem item;
        if (existing >= 0)
        {
            item = Items[existing];
            Items.RemoveAt(existing);
        }
        else
        {
            item = new RecentItem { Path = PathComparer.Normalize(path) };
        }
        item.Count++;
        item.LastUsed = now ?? DateTimeOffset.UtcNow;
        Items.Insert(0, item);
        if (Items.Count > Capacity)
            Items.RemoveRange(Capacity, Items.Count - Capacity);
    }

    public bool Remove(string path) => Items.RemoveAll(i => PathComparer.Equal(i.Path, path)) > 0;

    public IEnumerable<RecentItem> MostRecent(int n) => Items.Take(n);

    /// <summary>Frequency ranking with a simple recency decay (half-life of one week).</summary>
    public IEnumerable<RecentItem> MostFrequent(int n, DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.UtcNow;
        return Items
            .OrderByDescending(i => i.Count * Math.Pow(0.5, Math.Max(0, (t - i.LastUsed).TotalDays) / 7.0))
            .Take(n);
    }

    public void Normalize()
    {
        Items ??= new();
        Items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Path));
        if (Capacity < 5 || Capacity > 1000) Capacity = 40;
        if (Items.Count > Capacity) Items.RemoveRange(Capacity, Items.Count - Capacity);
    }
}
