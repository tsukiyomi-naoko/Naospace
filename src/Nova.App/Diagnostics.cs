using System.Diagnostics;
using System.IO;

namespace Nova.App;

/// <summary>Append-only error log in the data folder (nova.log), capped in size.</summary>
public static class Diagnostics
{
    private static readonly object Gate = new();
    public static string LogPath => Path.Combine(AppPaths.DataDirectory, "nova.log");
    public static int ErrorCount { get; private set; }

    public static void Log(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 1_000_000) File.Move(LogPath, LogPath + ".old", overwrite: true);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void Error(string context, Exception ex)
    {
        ErrorCount++;
        Log($"ERROR {context}: {ex}");
    }

    /// <summary>Routes WPF binding failures into the log (used by --smoke-test).</summary>
    public sealed class BindingErrorListener : TraceListener
    {
        public int Count { get; private set; }
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            Count++;
            Log("BINDING " + message);
        }
    }
}
