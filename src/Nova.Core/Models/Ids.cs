namespace Nova.Core.Models;

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N");
}
