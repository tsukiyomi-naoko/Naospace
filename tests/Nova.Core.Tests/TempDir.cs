namespace Nova.Core.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nova-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
