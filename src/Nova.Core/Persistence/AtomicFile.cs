using System.Text;

namespace Nova.Core.Persistence;

/// <summary>
/// Crash-safe file replacement. Content is written to a temporary sibling file, flushed to disk, and then swapped
/// into place. The previous version is kept as "&lt;file&gt;.bak". An interrupted write therefore leaves either the old
/// file or the new file intact — never a truncated one.
/// </summary>
public static class AtomicFile
{
    public const string BackupSuffix = ".bak";
    public const string TempSuffix = ".tmp";

    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + TempSuffix;
        var backup = path + BackupSuffix;

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true);
        }

        try
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, backup, ignoreMetadataErrors: true);
                    return;
                }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                // Fallback path (e.g. file systems without ReplaceFile support): keep a backup copy, then move.
                File.Copy(path, backup, overwrite: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    /// <summary>Removes orphaned temp files left by a crash during <see cref="WriteAllText"/>.</summary>
    public static void CleanupTempFiles(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var f in Directory.EnumerateFiles(directory, "*" + TempSuffix, SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddMinutes(-1))
                    File.Delete(f);
            }
            catch { }
        }
    }
}
