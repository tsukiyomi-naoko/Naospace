using System.IO.Compression;
using System.Text.Json;
using Nova.Core.Diagnostics;
using Nova.Core.Model;

namespace Nova.Core.Persistence;

/// <summary>Zip backups of the whole configuration plus portable single-workspace export/import.</summary>
public sealed class BackupService
{
    public const int DefaultKeep = 20;
    private const string ExportFormat = "nova-workspace-export";
    private readonly ConfigStore _store;

    public BackupService(ConfigStore store) => _store = store;

    public IReadOnlyList<FileInfo> ListBackups() =>
        new DirectoryInfo(_store.BackupDirectory).Exists
            ? new DirectoryInfo(_store.BackupDirectory).GetFiles("nova-backup-*.zip").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList()
            : new List<FileInfo>();

    /// <summary>Creates a zip of all configuration documents (excluding logs and other backups).</summary>
    public string CreateBackup(string reason = "manual", int keep = DefaultKeep)
    {
        Directory.CreateDirectory(_store.BackupDirectory);
        var name = $"nova-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Sanitize(reason)}.zip";
        var target = Path.Combine(_store.BackupDirectory, name);
        var temp = target + AtomicFile.TempSuffix;
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var file in EnumerateConfigFiles())
            {
                var rel = Path.GetRelativePath(_store.Root, file).Replace('\\', '/');
                try
                {
                    using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                    using var dst = entry.Open();
                    src.CopyTo(dst);
                }
                catch (IOException ex)
                {
                    Log.Warn($"Backup skipped {rel}: {ex.Message}");
                }
            }
        }
        File.Move(temp, target, overwrite: true);
        Prune(keep);
        Log.Info($"Created configuration backup {name}");
        return target;
    }

    /// <summary>Creates an automatic backup when the newest one is older than <paramref name="interval"/>.</summary>
    public string? CreateAutomaticBackupIfDue(TimeSpan interval)
    {
        if (!File.Exists(_store.CatalogPath)) return null;
        var newest = ListBackups().FirstOrDefault();
        if (newest != null && DateTime.UtcNow - newest.LastWriteTimeUtc < interval) return null;
        return CreateBackup("auto");
    }

    /// <summary>
    /// Restores a backup. A safety backup of the current configuration is taken first. The archive is validated before
    /// anything is overwritten. The application must reload its configuration afterwards.
    /// </summary>
    public void RestoreBackup(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (!entries.Any(e => e.FullName.Equals("workspaces.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The selected file is not a NOVA Workspace backup (workspaces.json missing).");

        foreach (var e in entries)
        {
            var dest = Path.GetFullPath(Path.Combine(_store.Root, e.FullName));
            if (!dest.StartsWith(_store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Backup contains an unsafe path: {e.FullName}");
            if (e.FullName.StartsWith("backups/", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("logs/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Backup contains an unexpected entry: {e.FullName}");
        }

        CreateBackup("before-restore");

        foreach (var e in entries)
        {
            var dest = Path.GetFullPath(Path.Combine(_store.Root, e.FullName));
            using var reader = new StreamReader(e.Open());
            AtomicFile.WriteAllText(dest, reader.ReadToEnd());
        }
        Log.Info($"Restored configuration from {Path.GetFileName(zipPath)}");
    }

    public sealed class WorkspaceExport
    {
        public string Format { get; set; } = ExportFormat;
        public int SchemaVersion { get; set; } = WorkspaceCatalog.CurrentVersion;
        public WorkspaceDefinition Workspace { get; set; } = new();
        public List<DetectionRule> Rules { get; set; } = new();
    }

    public static void ExportWorkspace(string path, WorkspaceDefinition workspace, IEnumerable<DetectionRule> rules)
    {
        var export = new WorkspaceExport
        {
            Workspace = workspace,
            Rules = rules.Where(r => r.WorkspaceId == workspace.Id).ToList(),
        };
        AtomicFile.WriteAllText(path, JsonDocumentStore.Serialize(export));
    }

    /// <summary>Reads an exported workspace. Ids are regenerated so the import never collides with existing data.</summary>
    public static (WorkspaceDefinition Workspace, List<DetectionRule> Rules) ImportWorkspace(string path)
    {
        var export = JsonSerializer.Deserialize<WorkspaceExport>(File.ReadAllText(path), JsonDocumentStore.Options)
                     ?? throw new InvalidDataException("Empty workspace file.");
        if (!string.Equals(export.Format, ExportFormat, StringComparison.Ordinal))
            throw new InvalidDataException("The selected file is not an exported NOVA workspace.");
        if (export.SchemaVersion > WorkspaceCatalog.CurrentVersion)
            throw new InvalidDataException("This workspace was exported by a newer version of NOVA Workspace.");

        var catalog = new WorkspaceCatalog { Workspaces = { export.Workspace } };
        Schemas.Catalog.Validate(catalog);

        var ws = export.Workspace;
        ws.Id = Ids.New();
        ws.Pins = ws.Pins.Select(p => p.DeepClone(newIds: true)).ToList();
        var rules = (export.Rules ?? new()).Where(r => r != null).Select(r =>
        {
            r.Id = Ids.New();
            r.WorkspaceId = ws.Id;
            return r;
        }).ToList();
        var ruleSet = new RuleSet { Rules = rules };
        Schemas.Rules.Validate(ruleSet);
        return (ws, ruleSet.Rules);
    }

    private IEnumerable<string> EnumerateConfigFiles()
    {
        foreach (var f in new[] { _store.SettingsPath, _store.CatalogPath, _store.RulesPath })
            if (File.Exists(f)) yield return f;
        foreach (var dir in new[] { _store.StateDirectory, _store.SnapshotDirectory })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
                yield return f;
        }
    }

    private void Prune(int keep)
    {
        foreach (var f in ListBackups().Skip(keep))
        {
            try { f.Delete(); } catch { }
        }
    }

    private static string Sanitize(string s) => new(s.Where(char.IsLetterOrDigit).Take(24).ToArray());
}
