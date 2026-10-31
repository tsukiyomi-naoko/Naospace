namespace Nova.Core.Model;

/// <summary>Stable, persistable identifiers. Window handles / PIDs are never persisted.</summary>
public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N");
}
