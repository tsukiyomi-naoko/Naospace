using Nova.Core.Detection;
using Nova.Core.Model;

namespace Nova.Core.Tests;

public class DetectionTests
{
    private static readonly DetectionRule VsCodeGeneric = new() { ExecutableName = "Code.exe", WorkspaceId = "dev" };
    private static readonly DetectionRule VsCodeNova = new() { ExecutableName = "code", TitleContains = "NovaWorkspace", WorkspaceId = "nova" };
    private static readonly DetectionRule Mozaik = new() { ExecutableName = "Mozaik.exe", WorkspaceId = "mozaik" };
    private static readonly DetectionRule ComfyBrowser = new() { TitleContains = "ComfyUI", WorkspaceId = "comfy" };

    private static DetectionEngine Engine(params DetectionRule[] rules) => new(rules);

    [Fact]
    public void ExecutableNameMatchesRegardlessOfPathAndExtension()
    {
        var e = Engine(Mozaik);
        Assert.Equal("mozaik", e.ResolveWorkspace(new ForegroundInfo(@"C:\Program Files\Mozaik 11\MOZAIK.EXE", "Job 42")));
        Assert.Equal("mozaik", e.ResolveWorkspace(new ForegroundInfo("Mozaik.exe", "")));
    }

    [Fact]
    public void TitleRuleBeatsExecutableOnlyRule()
    {
        var e = Engine(VsCodeGeneric, VsCodeNova);
        Assert.Equal("nova", e.ResolveWorkspace(new ForegroundInfo("Code.exe", "App.cs - NovaWorkspace - Visual Studio Code")));
        Assert.Equal("dev", e.ResolveWorkspace(new ForegroundInfo("Code.exe", "x - Other - Visual Studio Code")));
    }

    [Fact]
    public void TitleOnlyRuleMatchesAnyBrowser()
    {
        var e = Engine(ComfyBrowser);
        Assert.Equal("comfy", e.ResolveWorkspace(new ForegroundInfo("chrome.exe", "ComfyUI - Google Chrome")));
        Assert.Null(e.ResolveWorkspace(new ForegroundInfo("chrome.exe", "News - Google Chrome")));
    }

    [Fact]
    public void PriorityBreaksTies()
    {
        var low = new DetectionRule { ExecutableName = "a.exe", WorkspaceId = "low", Priority = 0 };
        var high = new DetectionRule { ExecutableName = "a.exe", WorkspaceId = "high", Priority = 5 };
        Assert.Equal("high", Engine(low, high).ResolveWorkspace(new ForegroundInfo("a.exe", "")));
    }

    [Fact]
    public void NeverSwitchSuppresses()
    {
        var never = new DetectionRule { ExecutableName = "Code.exe", Action = RuleAction.NeverSwitch };
        Assert.Null(Engine(VsCodeGeneric, never).ResolveWorkspace(new ForegroundInfo("Code.exe", "x")));
        // A more specific associate rule still wins over an exe-level suppression.
        Assert.Equal("nova", Engine(never, VsCodeNova).ResolveWorkspace(new ForegroundInfo("Code.exe", "NovaWorkspace")));
    }

    [Fact]
    public void RegexPatternAndDisabledAndEmptyRules()
    {
        var rx = new DetectionRule { TitlePattern = @"^Job \d+ - Mozaik$", WorkspaceId = "mozaik" };
        var disabled = new DetectionRule { ExecutableName = "x.exe", WorkspaceId = "w", Enabled = false };
        var empty = new DetectionRule { WorkspaceId = "everything" };
        var e = Engine(rx, disabled, empty);
        Assert.Equal(1, e.Count);
        Assert.Equal("mozaik", e.ResolveWorkspace(new ForegroundInfo("m.exe", "job 12 - mozaik")));
        Assert.Null(e.ResolveWorkspace(new ForegroundInfo("x.exe", "")));
    }

    [Fact]
    public void ControllerSwitchesOnlyOnReliableMatch()
    {
        var c = new AutoSwitchController(Engine(Mozaik, VsCodeNova), "Nova.App.exe");
        c.SetActiveWorkspace("comfy");
        Assert.Null(c.OnForegroundChanged(new ForegroundInfo("notepad.exe", "Untitled")));
        Assert.Equal("mozaik", c.OnForegroundChanged(new ForegroundInfo("Mozaik.exe", "Job")));
        Assert.Null(c.OnForegroundChanged(new ForegroundInfo("Mozaik.exe", "Job 2"))); // already active
        Assert.Null(c.OnForegroundChanged(new ForegroundInfo("Nova.App.exe", "NOVA"))); // own window ignored
        Assert.Equal("Mozaik.exe", c.LastExternalWindow!.Value.ExecutableName);
    }

    [Fact]
    public void LockAndManualOverride()
    {
        var c = new AutoSwitchController(Engine(Mozaik), "nova.exe");
        c.SetActiveWorkspace("comfy");
        c.Locked = true;
        Assert.Null(c.OnForegroundChanged(new ForegroundInfo("Mozaik.exe", "Job")));
        c.Locked = false;

        // The user manually picks "comfy" while Mozaik is the last external window.
        var suggestion = c.OnManualSelection("comfy");
        Assert.NotNull(suggestion);
        Assert.Equal("comfy", suggestion!.WorkspaceId);
        // Returning to the same Mozaik window must not undo the manual choice...
        Assert.Null(c.OnForegroundChanged(new ForegroundInfo("Mozaik.exe", "Job")));
        // ...but after another window has been in front, Mozaik switches again.
        Assert.Null(c.OnForegroundChanged(new ForegroundInfo("notepad.exe", "x")));
        Assert.Equal("mozaik", c.OnForegroundChanged(new ForegroundInfo("Mozaik.exe", "Job")));
    }

    [Fact]
    public void NoSuggestionWhenAlreadyAssociated()
    {
        var c = new AutoSwitchController(Engine(Mozaik), "nova.exe");
        c.OnForegroundChanged(new ForegroundInfo("Mozaik.exe", "Job"));
        Assert.Null(c.OnManualSelection("mozaik"));
    }

    [Fact]
    public void LearnedRules()
    {
        var s = new AssociationSuggestion(new ForegroundInfo("Code", "a.cs - Proj - Visual Studio Code"), "w", "Proj");
        var window = AutoSwitchController.CreateRule(s, AssociationChoice.RememberWindow)!;
        Assert.Equal("Code.exe", window.ExecutableName);
        Assert.Equal("Proj", window.TitleContains);
        var app = AutoSwitchController.CreateRule(s, AssociationChoice.RememberApplication)!;
        Assert.Equal("", app.TitleContains);
        var never = AutoSwitchController.CreateRule(s, AssociationChoice.NeverForApplication)!;
        Assert.Equal(RuleAction.NeverSwitch, never.Action);
        Assert.Null(AutoSwitchController.CreateRule(s, AssociationChoice.JustThisTime));
    }

    [Theory]
    [InlineData("main.py - MyProject - Visual Studio Code", "MyProject")]
    [InlineData("● main.py - MyProject - Visual Studio Code", "MyProject")]
    [InlineData("Report.docx - Word", "Report.docx")]
    [InlineData("ComfyUI", "ComfyUI")]
    [InlineData("", "")]
    public void TitleTokenHeuristic(string title, string expected) =>
        Assert.Equal(expected, TitleHeuristics.SuggestProjectToken(title));
}
