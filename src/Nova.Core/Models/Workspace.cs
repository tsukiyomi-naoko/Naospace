using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.Core.Models;

/// <summary>
/// A project context: permanent pins, named sessions (each with its own tabs and
/// working set), per-workspace recents, path variables and detection rules.
/// Models are observable so the UI can bind to them directly; they are also the
/// serialization shape, which keeps model/view-model sync code to a minimum.
/// </summary>
public partial class Workspace : ObservableObject
{
    public string Id { get; set; } = Ids.New();

    [ObservableProperty] private string _name = "Workspace";
    [ObservableProperty] private string _color = "#4F8EF7";
    [ObservableProperty] private string? _description;

    /// <summary>Named roots such as COMFY = D:\ComfyUI. Pins may reference them as ${COMFY}.</summary>
    public ObservableCollection<WorkspaceVariable> Variables { get; set; } = new();

    /// <summary>Root of the pin tree. Contains <see cref="PinGroup"/>s and <see cref="PinItem"/>s.</summary>
    public ObservableCollection<PinNode> Pins { get; set; } = new();

    public ObservableCollection<Session> Sessions { get; set; } = new();

    [ObservableProperty] private string? _activeSessionId;

    /// <summary>Automatic, workspace-scoped history of visited folders (most recent first).</summary>
    public ObservableCollection<RecentEntry> Recents { get; set; } = new();

    public ObservableCollection<DetectionRule> Rules { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastActivatedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public Session? ActiveSession =>
        Sessions.FirstOrDefault(s => s.Id == ActiveSessionId) ?? Sessions.FirstOrDefault();

    public Session EnsureSession()
    {
        var s = ActiveSession;
        if (s is null)
        {
            s = new Session { Name = "Main" };
            Sessions.Add(s);
        }
        ActiveSessionId = s.Id;
        return s;
    }

    /// <summary>Enumerates every pin item in the tree (depth first).</summary>
    public IEnumerable<PinItem> AllPinItems() => PinNode.Flatten(Pins).OfType<PinItem>();
}

public partial class WorkspaceVariable : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _value = "";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(PinGroup), "group")]
[JsonDerivedType(typeof(PinItem), "item")]
public abstract partial class PinNode : ObservableObject
{
    public string Id { get; set; } = Ids.New();

    [ObservableProperty] private string _name = "";

    public static IEnumerable<PinNode> Flatten(IEnumerable<PinNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if (n is PinGroup g)
                foreach (var c in Flatten(g.Children))
                    yield return c;
        }
    }

    /// <summary>Finds the collection that directly contains <paramref name="node"/>.</summary>
    public static ObservableCollection<PinNode>? FindParentCollection(ObservableCollection<PinNode> root, PinNode node)
    {
        if (root.Contains(node)) return root;
        foreach (var g in root.OfType<PinGroup>())
        {
            var found = FindParentCollection(g.Children, node);
            if (found is not null) return found;
        }
        return null;
    }
}

public partial class PinGroup : PinNode
{
    [ObservableProperty] private bool _isExpanded = true;
    public ObservableCollection<PinNode> Children { get; set; } = new();
}

public enum PinKind
{
    Folder,
    File,
    Application,
    Script,
    Url,
    /// <summary>Arbitrary command line, run through the shell.</summary>
    Command,
}

public partial class PinItem : PinNode
{
    [ObservableProperty] private PinKind _kind = PinKind.Folder;

    /// <summary>Path, URL or executable. May contain ${VAR} and %ENV% references.</summary>
    [ObservableProperty] private string _target = "";

    [ObservableProperty] private string? _arguments;
    [ObservableProperty] private string? _workingDirectory;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _runAsAdministrator;

    [JsonIgnore] public bool IsNavigable => Kind == PinKind.Folder;

    /// <summary>Leaves have nothing to expand; present so tree bindings resolve uniformly.</summary>
    [JsonIgnore] public bool IsExpanded { get => false; set { } }
}

public partial class RecentEntry : ObservableObject
{
    public string Path { get; set; } = "";
    [JsonIgnore] public string DisplayName => Util.PathUtil.DisplayName(Path);
    [JsonIgnore] public string ParentPath => Util.PathUtil.Parent(Path) ?? "";
    [ObservableProperty] private DateTimeOffset _lastVisited = DateTimeOffset.Now;
    [ObservableProperty] private int _visitCount = 1;
}
