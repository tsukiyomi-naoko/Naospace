using System.Text.Json.Nodes;
using Nova.Core.Model;

namespace Nova.Core.Persistence;

/// <summary>Describes how one persisted document type is versioned, migrated, validated and defaulted.</summary>
public sealed class DocumentSchema<T> where T : class, IVersionedDocument
{
    public required int CurrentVersion { get; init; }
    public required Func<T> CreateDefault { get; init; }

    /// <summary>Migration steps keyed by the version they upgrade FROM. Each step must return a document at version+1.</summary>
    public IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> Migrations { get; init; } =
        new Dictionary<int, Func<JsonObject, JsonObject>>();

    /// <summary>Repairs a loaded document in place and returns human-readable notes about what was repaired.</summary>
    public Func<T, IList<string>> Validate { get; init; } = _ => Array.Empty<string>();
}
