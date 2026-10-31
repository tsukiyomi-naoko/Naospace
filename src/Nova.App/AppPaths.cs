using System.IO;

namespace Nova.App;

/// <summary>
/// Where NOVA keeps its data. Portable mode: if a file named "nova.portable" sits next to
/// the executable, data lives in ".\data" beside it (handy for USB sticks / synced folders).
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Resolve();
    public static bool IsPortable { get; private set; }

    private static string Resolve()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "nova.portable")))
        {
            IsPortable = true;
            return Path.Combine(exeDir, "data");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaWorkspace");
    }
}
