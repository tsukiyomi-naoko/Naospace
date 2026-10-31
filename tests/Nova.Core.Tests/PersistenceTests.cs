using Nova.Core.Models;
using Nova.Core.Persistence;
using Nova.Core.Services;

namespace Nova.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nova-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static NovaState SampleState()
    {
        var state = new NovaState();
        var ws = new Workspace { Name = "ComfyUI", Color = "#FF8800" };
        ws.Variables.Add(new WorkspaceVariable { Name = "COMFY", Value = @"D:\ComfyUI" });
        var g = new PinGroup { Name = "Models" };
        g.Children.Add(new PinItem { Name = "LoRAs", Target = @"${COMFY}\models\loras" });
        g.Children.Add(new PinGroup { Name = "Nested", Children = { new PinItem { Name = "x", Kind = PinKind.Url, Target = "https://x" } } });
        ws.Pins.Add(g);
        var s = new Session { Name = "Animation", IsDualPane = true };
        s.Left.Tabs.Add(new TabState { Path = @"D:\ComfyUI\output", Back = { @"D:\ComfyUI" } });
        s.WorkingSet.Add(new WorkingLocation { Path = @"D:\tmp\shots", Label = "Shots" });
        ws.Sessions.Add(s);
        ws.ActiveSessionId = s.Id;
        ws.Rules.Add(new DetectionRule { ProcessName = "chrome", TitlePattern = "ComfyUI", SessionId = s.Id });
        state.Workspaces.Add(ws);
        state.ActiveWorkspaceId = ws.Id;
        return state;
    }

    [Fact]
    public void RoundTrip_PreservesPolymorphicTree()
    {
        var json = StateStore.Serialize(SampleState());
        var back = StateStore.Deserialize(json);
        var ws = back.Workspaces.Single();
        var group = Assert.IsType<PinGroup>(ws.Pins[0]);
        Assert.IsType<PinItem>(group.Children[0]);
        var nested = Assert.IsType<PinGroup>(group.Children[1]);
        Assert.Equal(PinKind.Url, ((PinItem)nested.Children[0]).Kind);
        Assert.True(ws.ActiveSession!.IsDualPane);
        Assert.Equal("Shots", ws.ActiveSession.WorkingSet[0].Label);
        Assert.Equal(json, StateStore.Serialize(back));
    }

    [Fact]
    public void FirstRun_CreatesDefault()
    {
        var (state, report) = new StateStore(_dir).Load();
        Assert.True(report.CreatedNew);
        Assert.NotEmpty(state.Workspaces);
        Assert.NotNull(state.ActiveWorkspace!.ActiveSession);
    }

    [Fact]
    public void Save_SkipsUnchanged_AndWritesAtomically()
    {
        var store = new StateStore(_dir);
        var state = SampleState();
        Assert.True(store.Save(state));
        Assert.False(store.Save(state));
        state.Workspaces[0].Name = "Changed";
        Assert.True(store.Save(state));
        Assert.False(File.Exists(store.StatePath + ".tmp"));
        var (loaded, report) = new StateStore(_dir).Load();
        Assert.Equal("Changed", loaded.Workspaces[0].Name);
        Assert.False(report.Recovered);
    }

    [Fact]
    public void CorruptState_IsQuarantined_AndRecoveredFromBackup()
    {
        var store = new StateStore(_dir);
        store.Save(SampleState());
        new StateStore(_dir).Load(); // creates a backup on successful load

        File.WriteAllText(store.StatePath, "{ this is not json");
        var (state, report) = new StateStore(_dir).Load();
        Assert.True(report.Recovered);
        Assert.NotNull(report.RecoveredFrom);
        Assert.Equal("ComfyUI", state.Workspaces[0].Name);
        Assert.NotEmpty(Directory.GetFiles(_dir, "state.corrupt-*.json"));
    }

    [Fact]
    public void NewerSchema_IsNotSilentlyOverwritten()
    {
        Directory.CreateDirectory(_dir);
        var store = new StateStore(_dir);
        File.WriteAllText(store.StatePath, """{ "schemaVersion": 99, "workspaces": [] }""");
        var (_, report) = store.Load();
        Assert.True(report.Recovered);
        Assert.Contains("newer", report.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repair_FixesDanglingReferences()
    {
        var json = """{ "schemaVersion": 1, "activeWorkspaceId": "missing", "workspaces": [ { "id": "w1", "name": "A" } ] }""";
        var state = StateStore.Deserialize(json);
        Assert.Equal("w1", state.ActiveWorkspaceId);
        Assert.NotNull(state.Workspaces[0].ActiveSession);
    }

    [Fact]
    public void ExportImport_AssignsFreshIds_AndRemapsSessions()
    {
        var original = SampleState().Workspaces[0];
        WorkspaceOps.RecordVisit(original, @"D:\x", 10);
        var json = WorkspacePorter.Export(original, includeHistory: false);
        var imported = WorkspacePorter.Import(json, new[] { "ComfyUI" });

        Assert.Equal("ComfyUI (2)", imported.Name);
        Assert.NotEqual(original.Id, imported.Id);
        Assert.Empty(imported.Recents);
        Assert.Empty(imported.Sessions[0].Left.Tabs[0].Back);
        Assert.Equal(imported.Sessions[0].Id, imported.Rules[0].SessionId);
        Assert.Equal(imported.Sessions[0].Id, imported.ActiveSessionId);
        Assert.Throws<InvalidDataException>(() => WorkspacePorter.Import("""{"format":"other"}""", Array.Empty<string>()));
    }
}
