using Nova.Core.Diagnostics;
using Nova.Core.Model;

namespace Nova.Core.Persistence;

/// <summary>
/// Owns the on-disk layout of all NOVA configuration:
/// <code>
/// root/
///   settings.json              user preferences + window placement
///   workspaces.json            workspace definitions (pins, groups, tools)
///   rules.json                 detection rules
///   state/&lt;id&gt;.json           sessions, tabs, history, working locations, recents (changes often)
///   snapshots/&lt;id&gt;.json       manual snapshots
///   backups/                   automatic + manual zip backups
///   logs/
/// </code>
/// Splitting rarely-changing definitions from frequently-changing state means an autosave of tab state can never
/// damage the user's carefully built workspace definitions.
/// </summary>
public sealed class ConfigStore
{
    private readonly HashSet<string> _readOnly = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ConfigStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(SnapshotDirectory);
        Directory.CreateDirectory(BackupDirectory);
        AtomicFile.CleanupTempFiles(Root);
    }

    public string Root { get; }
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string CatalogPath => Path.Combine(Root, "workspaces.json");
    public string RulesPath => Path.Combine(Root, "rules.json");
    public string StateDirectory => Path.Combine(Root, "state");
    public string SnapshotDirectory => Path.Combine(Root, "snapshots");
    public string BackupDirectory => Path.Combine(Root, "backups");
    public string LogDirectory => Path.Combine(Root, "logs");

    public string StatePath(string workspaceId) => Path.Combine(StateDirectory, SafeId(workspaceId) + ".json");
    public string SnapshotPath(string workspaceId) => Path.Combine(SnapshotDirectory, SafeId(workspaceId) + ".json");

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA Workspace");

    public LoadResult<AppSettings> LoadSettings() => Track(SettingsPath, JsonDocumentStore.Load(SettingsPath, Schemas.Settings));
    public LoadResult<WorkspaceCatalog> LoadCatalog() => Track(CatalogPath, JsonDocumentStore.Load(CatalogPath, Schemas.Catalog));
    public LoadResult<RuleSet> LoadRules() => Track(RulesPath, JsonDocumentStore.Load(RulesPath, Schemas.Rules));

    public LoadResult<WorkspaceState> LoadState(string workspaceId)
    {
        var path = StatePath(workspaceId);
        var r = Track(path, JsonDocumentStore.Load(path, Schemas.State));
        r.Value.WorkspaceId = workspaceId;
        return r;
    }

    public LoadResult<SnapshotCollection> LoadSnapshots(string workspaceId)
    {
        var path = SnapshotPath(workspaceId);
        var r = Track(path, JsonDocumentStore.Load(path, Schemas.Snapshots));
        r.Value.WorkspaceId = workspaceId;
        return r;
    }

    public bool IsReadOnly(string path)
    {
        lock (_gate) return _readOnly.Contains(Path.GetFullPath(path));
    }

    /// <summary>Writes already-serialized JSON. Refuses (returns false) for files that came from a newer schema.</summary>
    public bool WriteSerialized(string path, string json)
    {
        if (IsReadOnly(path))
        {
            Log.Warn($"Skipped saving {Path.GetFileName(path)}: it belongs to a newer NOVA version.");
            return false;
        }
        AtomicFile.WriteAllText(path, json);
        return true;
    }

    public bool Save<T>(string path, T document) => WriteSerialized(path, JsonDocumentStore.Serialize(document));

    /// <summary>Moves a deleted workspace's state files into the backup folder instead of erasing them.</summary>
    public void ArchiveWorkspaceFiles(string workspaceId)
    {
        var archive = Path.Combine(BackupDirectory, "deleted-workspaces", $"{SafeId(workspaceId)}-{DateTime.UtcNow:yyyyMMddHHmmss}");
        foreach (var path in new[] { StatePath(workspaceId), SnapshotPath(workspaceId) })
        {
            if (!File.Exists(path)) continue;
            try
            {
                Directory.CreateDirectory(archive);
                File.Move(path, Path.Combine(archive, Path.GetFileName(Path.GetDirectoryName(path)!) + "-" + Path.GetFileName(path)));
                var bak = path + AtomicFile.BackupSuffix;
                if (File.Exists(bak)) File.Delete(bak);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not archive files of deleted workspace", ex);
            }
        }
    }

    private LoadResult<T> Track<T>(string path, LoadResult<T> result)
    {
        lock (_gate)
        {
            var full = Path.GetFullPath(path);
            if (result.ReadOnly) _readOnly.Add(full);
            else _readOnly.Remove(full);
        }
        return result;
    }

    internal static string SafeId(string id)
    {
        var chars = id.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray();
        if (chars.Length == 0) throw new ArgumentException("Invalid workspace id", nameof(id));
        return new string(chars);
    }
}
