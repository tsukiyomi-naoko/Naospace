namespace Nova.Core.IO;

public enum PathStatus { Available, Missing, TimedOut, Virtual }

/// <summary>
/// Checks whether a location exists without ever blocking the caller. Disconnected network shares and sleeping drives
/// can block <see cref="Directory.Exists(string)"/> for many seconds, so probes run on the thread pool with a timeout.
/// </summary>
public static class PathProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    /// <summary>True for Shell namespace locations that are not plain file-system paths (e.g. "::{GUID}", "shell:Downloads").</summary>
    public static bool IsVirtual(string path) =>
        path.StartsWith("::", StringComparison.Ordinal) ||
        path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("::{", StringComparison.Ordinal);

    public static async Task<PathStatus> ProbeAsync(string path, bool expectDirectory, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return PathStatus.Missing;
        if (IsVirtual(path)) return PathStatus.Virtual;
        var probe = Task.Run(() => expectDirectory ? Directory.Exists(path) : File.Exists(path) || Directory.Exists(path), ct);
        var finished = await Task.WhenAny(probe, Task.Delay(timeout ?? DefaultTimeout, ct)).ConfigureAwait(false);
        if (finished != probe) return PathStatus.TimedOut;
        return await probe.ConfigureAwait(false) ? PathStatus.Available : PathStatus.Missing;
    }
}
