using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nova.Core.Input;
using Nova.Core.Model;

namespace Nova.Core.Persistence;

/// <summary>Schema definitions (version, migrations, validation) for every persisted document.</summary>
public static class Schemas
{
    public static readonly DocumentSchema<WorkspaceCatalog> Catalog = new()
    {
        CurrentVersion = WorkspaceCatalog.CurrentVersion,
        CreateDefault = () => new WorkspaceCatalog(),
        Migrations = new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            // v0 (pre-release prototype) stored pin targets as "path". v1 renamed it to "target".
            [0] = MigrateCatalogV0ToV1,
        },
        Validate = ValidateCatalog,
    };

    public static readonly DocumentSchema<WorkspaceState> State = new()
    {
        CurrentVersion = WorkspaceState.CurrentVersion,
        CreateDefault = () => new WorkspaceState(),
        Migrations = new Dictionary<int, Func<JsonObject, JsonObject>> { [0] = n => n },
        Validate = ValidateState,
    };

    public static readonly DocumentSchema<SnapshotCollection> Snapshots = new()
    {
        CurrentVersion = SnapshotCollection.CurrentVersion,
        CreateDefault = () => new SnapshotCollection(),
        Migrations = new Dictionary<int, Func<JsonObject, JsonObject>> { [0] = n => n },
        Validate = ValidateSnapshots,
    };

    public static readonly DocumentSchema<AppSettings> Settings = new()
    {
        CurrentVersion = AppSettings.CurrentVersion,
        CreateDefault = () => new AppSettings(),
        Migrations = new Dictionary<int, Func<JsonObject, JsonObject>> { [0] = n => n },
        Validate = ValidateSettings,
    };

    public static readonly DocumentSchema<RuleSet> Rules = new()
    {
        CurrentVersion = RuleSet.CurrentVersion,
        CreateDefault = () => new RuleSet(),
        Migrations = new Dictionary<int, Func<JsonObject, JsonObject>> { [0] = n => n },
        Validate = ValidateRules,
    };

    internal static JsonObject MigrateCatalogV0ToV1(JsonObject root)
    {
        static void FixPins(JsonNode? pins)
        {
            if (pins is not JsonArray arr) return;
            foreach (var p in arr.OfType<JsonObject>())
            {
                if (!p.ContainsKey("target") && p.TryGetPropertyValue("path", out var path))
                {
                    p.Remove("path");
                    p["target"] = path?.DeepClone();
                }
                FixPins(p["children"]);
            }
        }

        if (root["workspaces"] is JsonArray workspaces)
            foreach (var ws in workspaces.OfType<JsonObject>())
                FixPins(ws["pins"]);
        return root;
    }

    private static IList<string> ValidateCatalog(WorkspaceCatalog c)
    {
        var notes = new List<string>();
        c.Workspaces ??= new();
        c.Workspaces.RemoveAll(w => w == null);
        var ids = new HashSet<string>();
        foreach (var w in c.Workspaces)
        {
            if (string.IsNullOrWhiteSpace(w.Id) || !ids.Add(w.Id))
            {
                w.Id = Ids.New();
                ids.Add(w.Id);
                notes.Add($"Workspace '{w.Name}' had a missing or duplicate id and was given a new one.");
            }
            if (string.IsNullOrWhiteSpace(w.Name)) w.Name = "Workspace";
            w.Icon ??= "▣";
            w.Notes ??= string.Empty;
            w.Pins ??= new();
            NormalizePins(w.Pins, new HashSet<string>());
        }
        return notes;
    }

    private static void NormalizePins(List<PinNode> pins, HashSet<string> seen)
    {
        pins.RemoveAll(p => p == null);
        foreach (var p in pins)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || !seen.Add(p.Id))
            {
                p.Id = Ids.New();
                seen.Add(p.Id);
            }
            p.Label ??= string.Empty;
            p.Target ??= string.Empty;
            p.Arguments ??= string.Empty;
            p.WorkingDirectory ??= string.Empty;
            p.Children ??= new();
            NormalizePins(p.Children, seen);
        }
    }

    private static IList<string> ValidateState(WorkspaceState s)
    {
        var notes = new List<string>();
        s.Sessions ??= new();
        s.Sessions.RemoveAll(x => x == null);
        var ids = new HashSet<string>();
        foreach (var session in s.Sessions)
        {
            session.Normalize();
            if (!ids.Add(session.Id)) { session.Id = Ids.New(); ids.Add(session.Id); }
        }
        if (s.Sessions.Count == 0) s.Sessions.Add(new WorkspaceSession());
        if (s.Sessions.All(x => x.Id != s.ActiveSessionId)) s.ActiveSessionId = s.Sessions[0].Id;

        s.WorkingLocations ??= new();
        s.WorkingLocations.RemoveAll(w => w == null || string.IsNullOrWhiteSpace(w.Path));
        s.WorkingLocations = s.WorkingLocations
            .GroupBy(w => PathComparer.Normalize(w.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        foreach (var w in s.WorkingLocations) w.Label ??= string.Empty;

        s.RecentFolders ??= new();
        s.RecentFolders.Normalize();
        s.RecentFiles ??= new();
        s.RecentFiles.Normalize();
        return notes;
    }

    private static IList<string> ValidateSnapshots(SnapshotCollection c)
    {
        c.Snapshots ??= new();
        c.Snapshots.RemoveAll(s => s == null || s.Session == null);
        foreach (var s in c.Snapshots)
        {
            s.Session.Normalize();
            if (string.IsNullOrWhiteSpace(s.Name)) s.Name = "Snapshot";
            if (string.IsNullOrWhiteSpace(s.Id)) s.Id = Ids.New();
            s.SourceSessionName ??= string.Empty;
        }
        return Array.Empty<string>();
    }

    private static IList<string> ValidateSettings(AppSettings s)
    {
        var notes = new List<string>();
        if (!HotkeyGesture.TryParse(s.SummonHotkey, out _))
        {
            notes.Add($"Invalid summon hotkey '{s.SummonHotkey}' was reset to the default.");
            s.SummonHotkey = new AppSettings().SummonHotkey;
        }
        s.ActiveWorkspaceId ??= string.Empty;
        s.Window ??= new WindowPlacement();
        var w = s.Window;
        w.MonitorDeviceName ??= string.Empty;
        if (!IsFinite(w.Width) || w.Width < 200 || w.Width > 20000) w.Width = 1100;
        if (!IsFinite(w.Height) || w.Height < 200 || w.Height > 20000) w.Height = 720;
        if (!IsFinite(w.SidebarWidth) || w.SidebarWidth < 160 || w.SidebarWidth > 1000) w.SidebarWidth = 280;
        if (!IsFinite(w.DockedWidth) || w.DockedWidth < 160 || w.DockedWidth > 2000) w.DockedWidth = 300;
        if (!IsFinite(w.Left) || !IsFinite(w.Top)) { w.Left = double.NaN; w.Top = double.NaN; }
        return notes;
    }

    private static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

    private static IList<string> ValidateRules(RuleSet r)
    {
        var notes = new List<string>();
        r.Rules ??= new();
        r.Rules.RemoveAll(x => x == null);
        foreach (var rule in r.Rules)
        {
            rule.ExecutableName ??= string.Empty;
            rule.TitleContains ??= string.Empty;
            rule.TitlePattern ??= string.Empty;
            rule.WorkspaceId ??= string.Empty;
            rule.Note ??= string.Empty;
            if (string.IsNullOrWhiteSpace(rule.Id)) rule.Id = Ids.New();
            if (!string.IsNullOrWhiteSpace(rule.TitlePattern))
            {
                try { _ = new Regex(rule.TitlePattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException)
                {
                    rule.Enabled = false;
                    notes.Add($"Detection rule with invalid title pattern '{rule.TitlePattern}' was disabled.");
                }
            }
        }
        return notes;
    }
}
