using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.Core.Models;

/// <summary>
/// A named working environment inside a workspace: open tabs in one or two panes,
/// plus the "working set" of task-scoped folders that are not permanent pins.
/// </summary>
public partial class Session : ObservableObject
{
    public string Id { get; set; } = Ids.New();
    [ObservableProperty] private string _name = "Session";
    [ObservableProperty] private bool _isDualPane;
    [ObservableProperty] private int _activePane;
    [ObservableProperty] private string? _notes;

    public PaneState Left { get; set; } = new();
    public PaneState Right { get; set; } = new();

    /// <summary>Task-scoped folders: easy to reach, easy to discard, promotable to pins.</summary>
    public ObservableCollection<WorkingLocation> WorkingSet { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.Now;

    public Session Clone(string newName)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, NovaJson.Options);
        var copy = System.Text.Json.JsonSerializer.Deserialize<Session>(json, NovaJson.Options)!;
        copy.Id = Ids.New();
        copy.Name = newName;
        copy.CreatedAt = copy.LastActiveAt = DateTimeOffset.Now;
        return copy;
    }
}

public class PaneState
{
    public List<TabState> Tabs { get; set; } = new();
    public int SelectedIndex { get; set; }
}

public enum SortField { Name, Modified, Type, Size }

/// <summary>A browser tab: location, history and view preferences.</summary>
public class TabState
{
    public const int MaxHistory = 50;

    public string Path { get; set; } = "";
    public List<string> Back { get; set; } = new();
    public List<string> Forward { get; set; } = new();
    public SortField SortBy { get; set; } = SortField.Name;
    public bool SortDescending { get; set; }

    [JsonIgnore] public bool CanGoBack => Back.Count > 0;
    [JsonIgnore] public bool CanGoForward => Forward.Count > 0;

    /// <summary>Navigates to a new location, pushing the current one onto the back stack.</summary>
    public void Navigate(string path)
    {
        if (string.Equals(path, Path, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.IsNullOrEmpty(Path))
        {
            Back.Add(Path);
            if (Back.Count > MaxHistory) Back.RemoveAt(0);
        }
        Forward.Clear();
        Path = path;
    }

    public string? GoBack()
    {
        if (Back.Count == 0) return null;
        var target = Back[^1];
        Back.RemoveAt(Back.Count - 1);
        Forward.Add(Path);
        Path = target;
        return target;
    }

    public string? GoForward()
    {
        if (Forward.Count == 0) return null;
        var target = Forward[^1];
        Forward.RemoveAt(Forward.Count - 1);
        Back.Add(Path);
        Path = target;
        return target;
    }
}

public partial class WorkingLocation : ObservableObject
{
    public string Path { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string? _label;

    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Util.PathUtil.DisplayName(Path) : Label!;
    [JsonIgnore] public string ParentPath => Util.PathUtil.Parent(Path) ?? "";
    [ObservableProperty] private DateTimeOffset _lastUsedAt = DateTimeOffset.Now;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}
