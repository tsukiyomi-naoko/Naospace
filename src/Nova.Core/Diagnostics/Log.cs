using System.Collections.Concurrent;
using System.Text;

namespace Nova.Core.Diagnostics;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Minimal, dependency-free file logger. Messages are written on a background thread to daily files and retained for
/// a limited period. Callers must avoid logging window titles or file contents; paths are logged only where they are
/// needed to diagnose a failure.
/// </summary>
public static class Log
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 10_000);
    private static string? _directory;
    private static Thread? _writer;
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;
    public static int RetentionDays { get; set; } = 14;

    /// <summary>Raised (on the calling thread) for every warning or error, e.g. to surface it in a status bar.</summary>
    public static event Action<LogLevel, string>? Reported;

    public static void Initialize(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        PruneOldFiles();
        if (_writer != null) return;
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "NOVA log writer" };
        _writer.Start();
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);
    public static void Info(string message) => Write(LogLevel.Info, message, null);
    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warning, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    public static void Write(LogLevel level, string message, Exception? ex)
    {
        if (level >= LogLevel.Warning)
        {
            try { Reported?.Invoke(level, message); } catch { /* never let a listener break logging */ }
        }
        if (level < MinimumLevel || _directory == null) return;
        var sb = new StringBuilder();
        sb.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")).Append(" [").Append(level).Append("] ")
          .Append('[').Append(Environment.CurrentManagedThreadId).Append("] ").Append(message);
        if (ex != null) sb.AppendLine().Append(ex);
        Queue.TryAdd(sb.ToString());
    }

    /// <summary>Blocks briefly until queued messages are written (used on shutdown and in crash handlers).</summary>
    public static void Flush(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (Queue.Count > 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
    }

    private static void WriterLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                var file = Path.Combine(_directory!, $"nova-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the application.
            }
        }
    }

    private static void PruneOldFiles()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_directory!, "nova-*.log"))
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-RetentionDays))
                    File.Delete(f);
            }
        }
        catch { }
    }
}
