using System.Text.Json;
using System.Text.Json.Serialization;
using Nova.Core.Models;

namespace Nova.Core.Persistence;

/// <summary>Export/import of single workspaces (".novaws" files) for backup, migration and sharing.</summary>
public static class WorkspacePorter
{
    public const string Format = "nova-workspace";
    public const string FileExtension = ".novaws";

    private sealed class Envelope
    {
        [JsonPropertyOrder(-2)] public string Format { get; set; } = WorkspacePorter.Format;
        [JsonPropertyOrder(-1)] public int Version { get; set; } = 1;
        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
        public Workspace? Workspace { get; set; }
    }

    public static string Export(Workspace ws, bool includeHistory)
    {
        var json = JsonSerializer.Serialize(ws, NovaJson.Options);
        var copy = JsonSerializer.Deserialize<Workspace>(json, NovaJson.Options)!;
        if (!includeHistory)
        {
            copy.Recents.Clear();
            foreach (var s in copy.Sessions)
            {
                foreach (var t in s.Left.Tabs.Concat(s.Right.Tabs)) { t.Back.Clear(); t.Forward.Clear(); }
            }
        }
        return JsonSerializer.Serialize(new Envelope { Workspace = copy }, NovaJson.Options);
    }

    /// <summary>Parses an export and assigns fresh ids so it never collides with existing data.</summary>
    public static Workspace Import(string json, IEnumerable<string> existingNames)
    {
        var env = JsonSerializer.Deserialize<Envelope>(json, NovaJson.Options)
                  ?? throw new InvalidDataException("Empty workspace file.");
        if (env.Format != Format || env.Workspace is null)
            throw new InvalidDataException("This is not a NOVA workspace export.");

        var ws = env.Workspace;
        ws.Id = Ids.New();
        var sessionMap = new Dictionary<string, string>();
        foreach (var s in ws.Sessions)
        {
            var newId = Ids.New();
            sessionMap[s.Id] = newId;
            s.Id = newId;
        }
        ws.ActiveSessionId = ws.ActiveSessionId is not null && sessionMap.TryGetValue(ws.ActiveSessionId, out var a) ? a : null;
        foreach (var node in PinNode.Flatten(ws.Pins)) node.Id = Ids.New();
        foreach (var r in ws.Rules)
        {
            r.Id = Ids.New();
            r.SessionId = r.SessionId is not null && sessionMap.TryGetValue(r.SessionId, out var m) ? m : null;
        }

        var names = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var baseName = ws.Name;
        for (int i = 2; names.Contains(ws.Name); i++) ws.Name = $"{baseName} ({i})";
        ws.EnsureSession();
        return ws;
    }
}
