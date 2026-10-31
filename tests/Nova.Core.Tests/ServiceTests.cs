using Nova.Core.Models;
using Nova.Core.Services;

namespace Nova.Core.Tests;

public class ServiceTests
{
    private static Workspace Ws(string name, params DetectionRule[] rules)
    {
        var ws = new Workspace { Name = name };
        foreach (var r in rules) ws.Rules.Add(r);
        return ws;
    }

    [Fact]
    public void Variables_ExpandAndCollapse_RoundTrip()
    {
        var vars = new[]
        {
            new WorkspaceVariable { Name = "COMFY", Value = @"D:\ComfyUI" },
            new WorkspaceVariable { Name = "MODELS", Value = @"${COMFY}\models" },
        };
        Assert.Equal(@"D:\ComfyUI\models\loras", VariableExpander.Expand(@"${MODELS}\loras", vars));
        Assert.Equal(@"${COMFY}\custom_nodes", VariableExpander.Collapse(@"D:\ComfyUI\custom_nodes", vars));
        // Most specific variable wins (MODELS expands to a longer root).
        Assert.Equal(@"${MODELS}\loras", VariableExpander.Collapse(@"d:\comfyui\models\loras", vars));
        Assert.Equal(@"${COMFY}", VariableExpander.Collapse(@"D:\ComfyUI\", vars));
        Assert.Equal(@"E:\Other", VariableExpander.Collapse(@"E:\Other", vars));
        Assert.Equal("${UNKNOWN}\\x", VariableExpander.Expand("${UNKNOWN}\\x", vars));
    }

    [Fact]
    public void Variables_CyclesTerminate()
    {
        var vars = new[] { new WorkspaceVariable { Name = "A", Value = "${A}x" } };
        var result = VariableExpander.Expand("${A}", vars);
        Assert.Contains("${A}", result);
    }

    [Fact]
    public void Detection_PrefersSpecificRules()
    {
        var general = Ws("Browser", new DetectionRule { ProcessName = "chrome" });
        var comfy = Ws("ComfyUI", new DetectionRule { ProcessName = "chrome.exe", TitlePattern = "ComfyUI" });
        var mozaik = Ws("Mozaik", new DetectionRule { ProcessName = "Mozaik*" });
        var all = new[] { general, comfy, mozaik };

        Assert.Equal(comfy, DetectionEngine.Match(new("chrome.exe", "ComfyUI - Google Chrome"), all)!.Workspace);
        Assert.Equal(general, DetectionEngine.Match(new("chrome.exe", "Inbox - Google Chrome"), all)!.Workspace);
        Assert.Equal(mozaik, DetectionEngine.Match(new(@"C:\Program Files\Mozaik\Mozaik2026.exe", "Job 17"), all)!.Workspace);
        Assert.Null(DetectionEngine.Match(new("notepad.exe", "x"), all));
    }

    [Fact]
    public void Detection_IgnoresDisabledRules()
    {
        var ws = Ws("X", new DetectionRule { ProcessName = "code", Enabled = false });
        Assert.Null(DetectionEngine.Match(new("Code.exe", "repo"), new[] { ws }));
    }

    [Theory]
    [InlineData("main.py - my-repo - Visual Studio Code", "my-repo")]
    [InlineData("ComfyUI - Google Chrome", "ComfyUI")]
    [InlineData("● app.ts - nova - Cursor", "nova")]
    [InlineData("Untitled", "Untitled")]
    public void TitleKey(string title, string expected) => Assert.Equal(expected, AssociationLearner.TitleKey(title));

    [Fact]
    public void Learner_SuggestsAfterThreshold_AndRespectsDismissal()
    {
        var state = new NovaState();
        var comfy = Ws("ComfyUI");
        state.Workspaces.Add(comfy);
        var fg = new ForegroundInfo("chrome.exe", "ComfyUI - Google Chrome");

        Assert.Null(AssociationLearner.RecordManualSelection(state, fg, comfy));
        Assert.Null(AssociationLearner.RecordManualSelection(state, fg, comfy));
        var s = AssociationLearner.RecordManualSelection(state, fg, comfy);
        Assert.NotNull(s);
        Assert.Equal("chrome", s!.Rule.ProcessName);
        Assert.Equal("ComfyUI", s.Rule.TitlePattern);

        AssociationLearner.Accept(s);
        Assert.Single(comfy.Rules);
        // Now handled by a rule → no more suggestions.
        Assert.Null(AssociationLearner.RecordManualSelection(state, fg, comfy));

        var mozaik = Ws("Mozaik");
        state.Workspaces.Add(mozaik);
        var fg2 = new ForegroundInfo("Mozaik.exe", "Job 1");
        for (int i = 0; i < 2; i++) AssociationLearner.RecordManualSelection(state, fg2, mozaik);
        var s2 = AssociationLearner.RecordManualSelection(state, fg2, mozaik)!;
        Assert.Null(s2.Rule.TitlePattern); // single-project app: process only
        AssociationLearner.Dismiss(s2);
        Assert.Null(AssociationLearner.RecordManualSelection(state, fg2, mozaik));
    }

    [Fact]
    public void Fuzzy_RanksWordStartsHigher()
    {
        Assert.Null(FuzzyMatcher.Score("xyz", "custom_nodes"));
        var a = FuzzyMatcher.Score("cn", "custom_nodes")!.Value;
        var b = FuzzyMatcher.Score("cn", "account")!.Value;
        Assert.True(a > b);
        var ranked = FuzzyMatcher.Rank("lora", new[] { "flora_backup", "loras", "models" }, x => x);
        Assert.Equal("loras", ranked[0]);
        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void Recents_DedupeMoveToFrontAndCap()
    {
        var ws = new Workspace();
        for (int i = 0; i < 10; i++) WorkspaceOps.RecordVisit(ws, $@"C:\d{i}", max: 5);
        Assert.Equal(5, ws.Recents.Count);
        WorkspaceOps.RecordVisit(ws, @"c:\D7\", max: 5);
        Assert.Equal(@"C:\d7", ws.Recents[0].Path);
        Assert.Equal(2, ws.Recents[0].VisitCount);
        Assert.Equal(5, ws.Recents.Count);
    }

    [Fact]
    public void Pins_CollapseOntoVariables_AndDetectPinned()
    {
        var ws = new Workspace();
        ws.Variables.Add(new WorkspaceVariable { Name = "COMFY", Value = @"D:\ComfyUI" });
        var pin = WorkspaceOps.PinFolder(ws, @"D:\ComfyUI\output");
        Assert.Equal(@"${COMFY}\output", pin.Target);
        Assert.Equal("output", pin.Name);
        Assert.True(WorkspaceOps.IsPinned(ws, @"d:\comfyui\output\"));
        Assert.Equal(PinKind.Script, WorkspaceOps.PinPath(ws, @"D:\ComfyUI\run.bat", false).Kind);
    }

    [Fact]
    public void Tabs_History()
    {
        var t = new TabState { Path = "A" };
        t.Navigate("B"); t.Navigate("C");
        Assert.Equal("B", t.GoBack());
        Assert.Equal("A", t.GoBack());
        Assert.Null(t.GoBack());
        Assert.Equal("B", t.GoForward());
        t.Navigate("D");
        Assert.False(t.CanGoForward);
        t.Navigate("d"); // same path, case-insensitive: no-op
        Assert.Equal(new[] { "A", "B" }, t.Back);
    }

    [Fact]
    public void WorkingSet_Dedupes()
    {
        var s = new Session();
        WorkspaceOps.AddToWorkingSet(s, @"C:\tmp\job");
        WorkspaceOps.AddToWorkingSet(s, @"c:\TMP\job\", "Job");
        Assert.Single(s.WorkingSet);
        Assert.Equal("Job", s.WorkingSet[0].Label);
    }

    [Fact]
    public void QuickJump_FindsAcrossWorkspaces_PreferringActive()
    {
        var state = new NovaState();
        var a = new Workspace { Name = "Mozaik" };
        WorkspaceOps.PinFolder(a, @"C:\Mozaik\Jobs");
        var b = new Workspace { Name = "ComfyUI" };
        WorkspaceOps.PinFolder(b, @"D:\ComfyUI\Jobs");
        state.Workspaces.Add(a); state.Workspaces.Add(b);
        state.ActiveWorkspaceId = b.Id;

        var results = QuickJump.Search(state, "jobs");
        Assert.Equal(b, results[0].Workspace);
        Assert.Contains(results, r => r.Workspace == a && r.Kind == JumpKind.Pin);
    }

    [Fact]
    public void Session_CloneIsDeep()
    {
        var s = new Session { Name = "A" };
        s.Left.Tabs.Add(new TabState { Path = @"C:\x" });
        var c = s.Clone("B");
        c.Left.Tabs[0].Path = @"C:\y";
        Assert.Equal(@"C:\x", s.Left.Tabs[0].Path);
        Assert.NotEqual(s.Id, c.Id);
    }
}
