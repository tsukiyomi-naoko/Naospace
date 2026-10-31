using Nova.Core.Detection;
using Nova.Core.Model;
using Nova.Core.Persistence;

namespace Nova.App.Services;

public enum StatusKind { Info, Warning, Error }

/// <summary>
/// Composition root for application-wide services and the loaded configuration documents. Each persisted document has
/// exactly one owner here and is saved through the debounced <see cref="SaveScheduler"/>.
/// </summary>
public static class AppServices
{
    public static ConfigStore Store { get; private set; } = null!;
    public static SaveScheduler Saver { get; private set; } = null!;
    public static BackupService Backups { get; private set; } = null!;
    public static AppSettings Settings { get; set; } = new();
    public static WorkspaceCatalog Catalog { get; set; } = new();
    public static RuleSet Rules { get; set; } = new();
    public static AutoSwitchController AutoSwitch { get; private set; } = null!;

    public static event Action<string, StatusKind>? StatusChanged;

    public static void Initialize(ConfigStore store, SynchronizationContext uiContext)
    {
        Store = store;
        Saver = new SaveScheduler(TimeSpan.FromMilliseconds(600), uiContext);
        Saver.SaveFailed += (key, ex) => Status($"Could not save settings ({key}): {ex.Message}", StatusKind.Error);
        Backups = new BackupService(store);
        AutoSwitch = new AutoSwitchController(new DetectionEngine(Array.Empty<DetectionRule>()), Environment.ProcessPath ?? "NovaWorkspace.exe");
    }

    public static void Status(string message, StatusKind kind = StatusKind.Info) => StatusChanged?.Invoke(message, kind);

    public static void ReloadRules()
    {
        AutoSwitch.ReplaceEngine(new DetectionEngine(Rules.Rules));
    }

    public static void SaveSettings() =>
        Saver.Request("settings", () => JsonDocumentStore.Serialize(Settings), json => Store.WriteSerialized(Store.SettingsPath, json));

    public static void SaveCatalog() =>
        Saver.Request("catalog", () => JsonDocumentStore.Serialize(Catalog), json => Store.WriteSerialized(Store.CatalogPath, json));

    public static void SaveRules() =>
        Saver.Request("rules", () => JsonDocumentStore.Serialize(Rules), json => Store.WriteSerialized(Store.RulesPath, json));

    public static void SaveState(WorkspaceState state)
    {
        var id = state.WorkspaceId;
        Saver.Request("state:" + id, () => JsonDocumentStore.Serialize(state), json => Store.WriteSerialized(Store.StatePath(id), json));
    }

    public static void SaveSnapshots(SnapshotCollection snapshots)
    {
        var id = snapshots.WorkspaceId;
        Saver.Request("snapshots:" + id, () => JsonDocumentStore.Serialize(snapshots), json => Store.WriteSerialized(Store.SnapshotPath(id), json));
    }
}
