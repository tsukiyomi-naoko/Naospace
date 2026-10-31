using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Nova.Core.Diagnostics;
using Nova.Core.Model;

namespace Nova.Core.Persistence;

public enum LoadSource { Primary, Backup, Default }

public sealed class LoadResult<T>
{
    public required T Value { get; init; }
    public LoadSource Source { get; init; }
    public List<string> Notes { get; } = new();

    /// <summary>
    /// True when the file was written by a newer schema version. The document is loaded best-effort but must not be
    /// saved back, so that the newer data is never destroyed by an older application.
    /// </summary>
    public bool ReadOnly { get; init; }
}

/// <summary>Versioned JSON documents with migration, validation, backup recovery and atomic saving.</summary>
public static class JsonDocumentStore
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static string Serialize<T>(T document) => JsonSerializer.Serialize(document, Options);

    public static void Save<T>(string path, T document) => AtomicFile.WriteAllText(path, Serialize(document));

    public static LoadResult<T> Load<T>(string path, DocumentSchema<T> schema) where T : class, IVersionedDocument
    {
        var notes = new List<string>();

        if (TryLoadFile(path, schema, notes, out var primary, out var readOnly))
            return Finish(primary!, LoadSource.Primary, readOnly, notes, schema);

        var backupPath = path + AtomicFile.BackupSuffix;
        if (File.Exists(path))
        {
            // The primary exists but is unreadable: keep a copy for forensics rather than losing it on the next save.
            var quarantine = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try
            {
                File.Copy(path, quarantine, overwrite: false);
                notes.Add($"'{Path.GetFileName(path)}' was unreadable; a copy was kept as '{Path.GetFileName(quarantine)}'.");
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not quarantine corrupt file {Path.GetFileName(path)}", ex);
            }
        }

        if (TryLoadFile(backupPath, schema, notes, out var backup, out readOnly))
        {
            notes.Add($"Recovered '{Path.GetFileName(path)}' from its backup copy.");
            return Finish(backup!, LoadSource.Backup, readOnly, notes, schema);
        }

        return Finish(schema.CreateDefault(), LoadSource.Default, false, notes, schema);
    }

    private static LoadResult<T> Finish<T>(T value, LoadSource source, bool readOnly, List<string> notes, DocumentSchema<T> schema)
        where T : class, IVersionedDocument
    {
        IList<string> repairs;
        try
        {
            repairs = schema.Validate(value);
        }
        catch (Exception ex)
        {
            Log.Error($"Validation of {typeof(T).Name} threw; using defaults", ex);
            value = schema.CreateDefault();
            repairs = new[] { $"{typeof(T).Name} could not be validated and was reset." };
            source = LoadSource.Default;
        }
        var result = new LoadResult<T> { Value = value, Source = source, ReadOnly = readOnly };
        result.Notes.AddRange(notes);
        result.Notes.AddRange(repairs);
        foreach (var n in result.Notes) Log.Warn(n);
        return result;
    }

    private static bool TryLoadFile<T>(string path, DocumentSchema<T> schema, List<string> notes, out T? value, out bool readOnly)
        where T : class, IVersionedDocument
    {
        value = null;
        readOnly = false;
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return false;
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
            if (node == null) return false;

            var version = ReadVersion(node);
            if (version > schema.CurrentVersion)
            {
                readOnly = true;
                notes.Add($"'{Path.GetFileName(path)}' was written by a newer version of NOVA Workspace (schema {version}). " +
                          "It was loaded read-only and will not be overwritten.");
            }
            else
            {
                while (version < schema.CurrentVersion)
                {
                    if (!schema.Migrations.TryGetValue(version, out var step))
                        throw new InvalidDataException($"No migration from schema {version} for {typeof(T).Name}.");
                    node = step(node);
                    version++;
                    node["schemaVersion"] = version;
                }
            }

            value = node.Deserialize<T>(Options);
            return value != null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            Log.Warn($"Failed to read {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private static int ReadVersion(JsonObject node)
    {
        foreach (var kv in node)
        {
            if (string.Equals(kv.Key, "schemaVersion", StringComparison.OrdinalIgnoreCase) && kv.Value is JsonValue v && v.TryGetValue<int>(out var i))
                return i;
        }
        return 0; // Unversioned files are treated as the pre-release v0 format.
    }
}
