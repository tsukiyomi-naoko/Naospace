using Nova.Core.Model;
using Nova.Core.Persistence;

namespace Nova.Core.Tests;

public class PersistenceTests
{
    [Fact]
    public void RoundTripCatalog()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        var cat = new WorkspaceCatalog();
        var ws = new WorkspaceDefinition { Name = "ComfyUI", Pins = { PinNode.Group("Models", PinNode.Folder(@"D:\m")) } };
        cat.Workspaces.Add(ws);
        Assert.True(store.Save(store.CatalogPath, cat));

        var loaded = store.LoadCatalog();
        Assert.Equal(LoadSource.Primary, loaded.Source);
        Assert.Equal("ComfyUI", loaded.Value.Workspaces[0].Name);
        Assert.Equal(@"D:\m", loaded.Value.Workspaces[0].Pins[0].Children[0].Target);
        Assert.Equal(PinKind.Group, loaded.Value.Workspaces[0].Pins[0].Kind);
    }

    [Fact]
    public void SecondSaveKeepsBackup()
    {
        using var tmp = new TempDir();
        var path = tmp.File("a.json");
        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two");
        Assert.Equal("two", File.ReadAllText(path));
        Assert.Equal("one", File.ReadAllText(path + ".bak"));
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.tmp"));
    }

    [Fact]
    public void CorruptPrimaryRecoversFromBackupAndQuarantines()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        var good = new WorkspaceCatalog { Workspaces = { new WorkspaceDefinition { Name = "Good" } } };
        store.Save(store.CatalogPath, good);
        store.Save(store.CatalogPath, good); // creates .bak with valid content
        File.WriteAllText(store.CatalogPath, "{ \"workspaces\": [ { \"name\": "); // simulated torn write

        var loaded = store.LoadCatalog();
        Assert.Equal(LoadSource.Backup, loaded.Source);
        Assert.Equal("Good", loaded.Value.Workspaces[0].Name);
        Assert.Single(Directory.GetFiles(tmp.Path, "workspaces.json.corrupt-*"));
    }

    [Fact]
    public void MissingFilesYieldDefaults()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        var s = store.LoadSettings();
        Assert.Equal(LoadSource.Default, s.Source);
        Assert.Equal("Ctrl+Alt+N", s.Value.SummonHotkey);
    }

    [Fact]
    public void NewerSchemaIsLoadedReadOnlyAndNeverOverwritten()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        var future = "{ \"schemaVersion\": 99, \"workspaces\": [ { \"id\": \"abc\", \"name\": \"Future\", \"futureField\": 1 } ] }";
        File.WriteAllText(store.CatalogPath, future);

        var loaded = store.LoadCatalog();
        Assert.True(loaded.ReadOnly);
        Assert.Equal("Future", loaded.Value.Workspaces[0].Name);
        Assert.False(store.Save(store.CatalogPath, loaded.Value));
        Assert.Equal(future, File.ReadAllText(store.CatalogPath));
    }

    [Fact]
    public void V0CatalogIsMigrated()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        File.WriteAllText(store.CatalogPath,
            "{ \"workspaces\": [ { \"name\": \"Old\", \"pins\": [ { \"kind\": \"Group\", \"label\": \"G\", \"children\": [ { \"kind\": \"Folder\", \"path\": \"C:\\\\x\" } ] } ] } ] }");
        var loaded = store.LoadCatalog();
        Assert.Equal(LoadSource.Primary, loaded.Source);
        Assert.Equal(@"C:\x", loaded.Value.Workspaces[0].Pins[0].Children[0].Target);
        Assert.False(string.IsNullOrEmpty(loaded.Value.Workspaces[0].Id));
    }

    [Fact]
    public void ValidationRepairsDuplicateIdsAndBadIndices()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        File.WriteAllText(store.CatalogPath,
            "{ \"schemaVersion\": 1, \"workspaces\": [ { \"id\": \"same\", \"name\": \"A\" }, { \"id\": \"same\", \"name\": \"\" } ] }");
        var cat = store.LoadCatalog().Value;
        Assert.NotEqual(cat.Workspaces[0].Id, cat.Workspaces[1].Id);
        Assert.Equal("Workspace", cat.Workspaces[1].Name);

        var statePath = store.StatePath("ws1");
        File.WriteAllText(statePath,
            "{ \"schemaVersion\": 1, \"activeSessionId\": \"nope\", \"sessions\": [ { \"id\": \"s\", \"name\": \"S\", \"activePaneIndex\": 7, \"panes\": [ { \"activeTabIndex\": 5, \"tabs\": [ { \"history\": { \"entries\": [\"C:\\\\a\"], \"index\": 9 } } ] } ] } ] }");
        var st = store.LoadState("ws1").Value;
        Assert.Equal("s", st.ActiveSessionId);
        var session = st.ActiveSession;
        Assert.Equal(2, session.Panes.Count);
        Assert.Equal(0, session.ActivePaneIndex);
        Assert.Equal(0, session.Panes[0].ActiveTabIndex);
        Assert.Equal(@"C:\a", session.Panes[0].Tabs[0].CurrentPath);
    }

    [Fact]
    public void UnreachablePinsArePreserved()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        var cat = new WorkspaceCatalog { Workspaces = { new WorkspaceDefinition { Pins = { PinNode.Folder(@"Z:\does\not\exist") } } } };
        store.Save(store.CatalogPath, cat);
        var loaded = store.LoadCatalog().Value;
        Assert.Equal(@"Z:\does\not\exist", loaded.Workspaces[0].Pins[0].Target);
    }

    [Fact]
    public void InvalidHotkeyAndRegexAreRepaired()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        File.WriteAllText(store.SettingsPath, "{ \"schemaVersion\": 1, \"summonHotkey\": \"N\", \"window\": { \"width\": -5 } }");
        var s = store.LoadSettings();
        Assert.Equal("Ctrl+Alt+N", s.Value.SummonHotkey);
        Assert.Equal(1100, s.Value.Window.Width);
        Assert.NotEmpty(s.Notes);

        File.WriteAllText(store.RulesPath, "{ \"schemaVersion\": 1, \"rules\": [ { \"titlePattern\": \"([\", \"workspaceId\": \"w\" } ] }");
        Assert.False(store.LoadRules().Value.Rules[0].Enabled);
    }

    [Fact]
    public void SaveSchedulerDebouncesAndFlushes()
    {
        using var tmp = new TempDir();
        var path = tmp.File("x.json");
        var serializeCount = 0;
        using var s = new SaveScheduler(TimeSpan.FromSeconds(30), null);
        for (var i = 0; i < 5; i++)
        {
            var v = i;
            s.Request("x", () => { serializeCount++; return v.ToString(); }, json => AtomicFile.WriteAllText(path, json));
        }
        Assert.Equal(1, s.PendingCount);
        s.FlushAll();
        Assert.Equal(1, serializeCount);
        Assert.Equal("4", File.ReadAllText(path));
    }

    [Fact]
    public void SaveSchedulerWritesAfterDelay()
    {
        using var tmp = new TempDir();
        var path = tmp.File("y.json");
        using var done = new ManualResetEventSlim();
        using var s = new SaveScheduler(TimeSpan.FromMilliseconds(20), null);
        s.Request("y", () => "hello", json => { AtomicFile.WriteAllText(path, json); done.Set(); });
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void BackupRestoreAndWorkspaceExportImport()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        var ws = new WorkspaceDefinition { Name = "Mozaik", Pins = { PinNode.Folder(@"C:\Mozaik\Jobs") } };
        store.Save(store.CatalogPath, new WorkspaceCatalog { Workspaces = { ws } });
        var backups = new BackupService(store);
        var zip = backups.CreateBackup();
        Assert.True(File.Exists(zip));

        store.Save(store.CatalogPath, new WorkspaceCatalog());
        Assert.Empty(store.LoadCatalog().Value.Workspaces);
        backups.RestoreBackup(zip);
        Assert.Equal("Mozaik", store.LoadCatalog().Value.Workspaces[0].Name);
        Assert.Equal(2, backups.ListBackups().Count); // original + safety backup taken before restore

        var exportPath = tmp.File("mozaik.novaws");
        var rule = new DetectionRule { WorkspaceId = ws.Id, ExecutableName = "Mozaik.exe" };
        BackupService.ExportWorkspace(exportPath, ws, new[] { rule, new DetectionRule { WorkspaceId = "other", ExecutableName = "x" } });
        var (imported, rules) = BackupService.ImportWorkspace(exportPath);
        Assert.Equal("Mozaik", imported.Name);
        Assert.NotEqual(ws.Id, imported.Id);
        Assert.NotEqual(ws.Pins[0].Id, imported.Pins[0].Id);
        Assert.Single(rules);
        Assert.Equal(imported.Id, rules[0].WorkspaceId);
    }

    [Fact]
    public void RestoreRejectsNonBackupArchive()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        var zipPath = tmp.File("bogus.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            z.CreateEntry("hello.txt");
        Assert.Throws<InvalidDataException>(() => new BackupService(store).RestoreBackup(zipPath));
    }
}
