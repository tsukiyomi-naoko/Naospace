using System.Text.Json.Serialization;

namespace Nova.Core.Model;

public enum PinKind
{
    Group,
    Folder,
    File,
    Application,
    Script,
    Url,
}

/// <summary>
/// A permanent, deliberately-added item of a workspace. Groups contain other nodes and can nest arbitrarily.
/// A pin whose target is currently unreachable is never removed automatically.
/// </summary>
public sealed class PinNode
{
    public string Id { get; set; } = Ids.New();
    public PinKind Kind { get; set; } = PinKind.Folder;

    /// <summary>User-visible label. When empty the UI derives one from <see cref="Target"/>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Path, executable, script or URL. Unused for groups.</summary>
    public string Target { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>For applications: activate an existing window of the same executable instead of starting a new instance.</summary>
    public bool ReuseExistingWindow { get; set; }

    public bool IsExpanded { get; set; } = true;

    public List<PinNode> Children { get; set; } = new();

    [JsonIgnore]
    public bool IsGroup => Kind == PinKind.Group;

    public string DisplayLabel => !string.IsNullOrWhiteSpace(Label) ? Label : DeriveLabel(Target, Kind);

    public static string DeriveLabel(string target, PinKind kind)
    {
        if (string.IsNullOrWhiteSpace(target))
            return kind == PinKind.Group ? "New group" : "(unnamed)";
        if (kind == PinKind.Url)
            return target;
        var trimmed = target.TrimEnd('\\', '/');
        if (trimmed.Length == 0)
            return target;
        // Drive roots such as "D:" keep their full form.
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return trimmed + "\\";
        var idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
    }

    public static PinNode Group(string label, params PinNode[] children) =>
        new() { Kind = PinKind.Group, Label = label, Children = children.ToList() };

    public static PinNode Folder(string path, string? label = null) =>
        new() { Kind = PinKind.Folder, Target = path, Label = label ?? string.Empty };

    public PinNode DeepClone(bool newIds = false)
    {
        var copy = (PinNode)MemberwiseClone();
        if (newIds) copy.Id = Ids.New();
        copy.Children = Children.Select(c => c.DeepClone(newIds)).ToList();
        return copy;
    }
}

public static class PinTree
{
    public static IEnumerable<PinNode> Flatten(IEnumerable<PinNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }

    /// <summary>Finds the list that directly contains the node with <paramref name="id"/>.</summary>
    public static List<PinNode>? FindParentList(List<PinNode> roots, string id)
    {
        if (roots.Any(n => n.Id == id))
            return roots;
        foreach (var n in roots)
        {
            var found = FindParentList(n.Children, id);
            if (found != null) return found;
        }
        return null;
    }

    public static PinNode? Find(IEnumerable<PinNode> roots, string id) => Flatten(roots).FirstOrDefault(n => n.Id == id);

    public static bool IsDescendantOf(PinNode candidate, PinNode ancestor) =>
        Flatten(ancestor.Children).Any(n => n.Id == candidate.Id);

    public static bool Remove(List<PinNode> roots, string id)
    {
        var list = FindParentList(roots, id);
        if (list == null) return false;
        return list.RemoveAll(n => n.Id == id) > 0;
    }

    /// <summary>
    /// Moves a node. If <paramref name="targetId"/> is a group and <paramref name="intoGroup"/> is true the node is appended
    /// to the group; otherwise it is inserted before (or after) the target in the target's list.
    /// Returns false for invalid moves (e.g. a group into its own descendant).
    /// </summary>
    public static bool Move(List<PinNode> roots, string nodeId, string? targetId, bool intoGroup, bool after = false)
    {
        if (nodeId == targetId) return false;
        var node = Find(roots, nodeId);
        if (node == null) return false;

        if (targetId == null)
        {
            Remove(roots, nodeId);
            roots.Add(node);
            return true;
        }

        var target = Find(roots, targetId);
        if (target == null) return false;
        if (IsDescendantOf(target, node)) return false;

        Remove(roots, nodeId);
        if (intoGroup && target.IsGroup)
        {
            target.Children.Add(node);
            target.IsExpanded = true;
            return true;
        }

        var list = FindParentList(roots, targetId)!;
        var index = list.FindIndex(n => n.Id == targetId);
        list.Insert(after ? index + 1 : index, node);
        return true;
    }
}
