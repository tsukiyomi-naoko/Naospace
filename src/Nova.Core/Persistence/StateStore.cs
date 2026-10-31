using System.Text.Json;
using System.Text.Json.Nodes;
using Nova.Core.Models;
using Nova.Core.Services;

namespace Nova.Core.Persistence;

public sealed record LoadReport(bool CreatedNew, bool Recovered, string? RecoveredFrom, string? Error);

/// <summary>
/// Crash-safe JSON persistence:
///  • writes go to a temp file, are flushed to disk, then atomically swapped in;
///  • unchanged state is never rewritten (cheap to call on a timer);
///  • rolling timestamped backups, at most one per <see cref="BackupInterval"/>;
///  • a corrupt state file is quarantined and the newest readable backup is used;
///  • a schema version hook allows forward migrations.
/// </summary>
public sealed class StateStore
{
    public const string StateFileName = "state.json";
    public static readonly TimeSpan BackupInterval = TimeSpan.FromHours(1);
    public const int MaxBackups = 30;

    private readonly object _gate = new();
    private string? _lastSavedJson;
    private DateTimeOffset _lastBackup = DateTimeOffset.MinValue;

    public StateStore(string directory)
    {
        Directory = directory;
        BackupDirectory = Path.Combine(directory, "backups");
    }

    public string Directory { get; }
    public string BackupDirectory { get; }
    public string StatePath => Path.Combine(Directory, StateFileName);

    public (NovaState State, LoadReport Report) Load()
    {
        System.IO.Directory.CreateDirectory(Directory);
        if (!File.Exists(StatePath))
        {
            var fresh = WorkspaceOps.CreateDefaultState();
            var recovered = TryLoadNewestBackup(out var fromBackup, out var backupName);
            if (recovered)
            {
                _lastSavedJson = null;
                return (fromBackup!, new LoadReport(false, true, backupName, "state.json was missing"));
            }
            return (fresh, new LoadReport(true, false, null, null));
        }

        try
        {
            var json = File.ReadAllText(StatePath);
            var state = Deserialize(json);
            _lastSavedJson = json;
            Backup(force: true);
            return (state, new LoadReport(false, false, null, null));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            var quarantine = Path.Combine(Directory, $"state.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Move(StatePath, quarantine, overwrite: true); } catch (IOException) { }

            if (TryLoadNewestBackup(out var fromBackup, out var name))
                return (fromBackup!, new LoadReport(false, true, name, ex.Message));
            return (WorkspaceOps.CreateDefaultState(), new LoadReport(true, true, null, ex.Message));
        }
    }

    /// <summary>Persists the state if it changed. Returns true when a write happened.</summary>
    public bool Save(NovaState state)
    {
        var json = Serialize(state);
        lock (_gate)
        {
            if (json == _lastSavedJson) return false;
            System.IO.Directory.CreateDirectory(Directory);
            WriteAtomic(StatePath, json);
            _lastSavedJson = json;
        }
        Backup(force: false);
        return true;
    }

    public static string Serialize(NovaState state) => JsonSerializer.Serialize(state, NovaJson.Options);

    public static NovaState Deserialize(string json)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        }) as JsonObject ?? throw new InvalidDataException("State root is not an object.");

        var version = node["schemaVersion"]?.GetValue<int>() ?? 0;
        if (version > NovaState.CurrentSchemaVersion)
            throw new NotSupportedException($"State was written by a newer NOVA (schema {version}).");
        Migrate(node, version);

        var state = node.Deserialize<NovaState>(NovaJson.Options)
                    ?? throw new InvalidDataException("State deserialized to null.");
        Repair(state);
        return state;
    }

    /// <summary>Upgrades older documents in place. Add a case per schema bump.</summary>
    private static void Migrate(JsonObject node, int fromVersion)
    {
        // v0 → v1: no structural changes; just stamp the version.
        node["schemaVersion"] = NovaState.CurrentSchemaVersion;
    }

    /// <summary>Makes a loaded state internally consistent (never trust the disk).</summary>
    private static void Repair(NovaState state)
    {
        if (state.Workspaces.Count == 0)
        {
            var d = WorkspaceOps.CreateDefaultState();
            foreach (var w in d.Workspaces) state.Workspaces.Add(w);
        }
        foreach (var ws in state.Workspaces)
        {
            if (string.IsNullOrEmpty(ws.Id)) ws.Id = Ids.New();
            ws.EnsureSession();
            foreach (var s in ws.Sessions)
            {
                s.Left ??= new PaneState();
                s.Right ??= new PaneState();
                s.Left.SelectedIndex = Math.Clamp(s.Left.SelectedIndex, 0, Math.Max(0, s.Left.Tabs.Count - 1));
                s.Right.SelectedIndex = Math.Clamp(s.Right.SelectedIndex, 0, Math.Max(0, s.Right.Tabs.Count - 1));
            }
        }
        if (state.Workspaces.All(w => w.Id != state.ActiveWorkspaceId))
            state.ActiveWorkspaceId = state.Workspaces[0].Id;
    }

    public void Backup(bool force)
    {
        if (!File.Exists(StatePath)) return;
        var now = DateTimeOffset.Now;
        if (!force && now - _lastBackup < BackupInterval) return;
        try
        {
            System.IO.Directory.CreateDirectory(BackupDirectory);
            var target = Path.Combine(BackupDirectory, $"state-{now:yyyyMMdd-HHmmss}.json");
            File.Copy(StatePath, target, overwrite: true);
            _lastBackup = now;
            foreach (var old in ListBackups().Skip(MaxBackups))
                try { File.Delete(old); } catch (IOException) { }
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Backups, newest first.</summary>
    public IEnumerable<string> ListBackups() =>
        System.IO.Directory.Exists(BackupDirectory)
            ? System.IO.Directory.GetFiles(BackupDirectory, "state-*.json").OrderByDescending(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    public NovaState RestoreBackup(string path)
    {
        var state = Deserialize(File.ReadAllText(path));
        Save(state);
        return state;
    }

    private bool TryLoadNewestBackup(out NovaState? state, out string? name)
    {
        foreach (var file in ListBackups())
        {
            try
            {
                state = Deserialize(File.ReadAllText(file));
                name = Path.GetFileName(file);
                return true;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException or IOException) { }
        }
        state = null;
        name = null;
        return false;
    }

    public static void WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
        {
            writer.Write(contents);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
