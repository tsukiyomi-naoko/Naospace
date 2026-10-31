using System.IO;
using System.Windows;

namespace Nova.App.Views;

internal static class DragDropHelper
{
    /// <summary>
    /// Explorer semantics: Ctrl = copy, Shift = move, otherwise move within the same drive
    /// and copy across drives.
    /// </summary>
    public static DragDropEffects ChooseEffect(DragEventArgs e, IReadOnlyList<string> sources, string destination)
    {
        var keys = e.KeyStates;
        DragDropEffects wanted;
        if (keys.HasFlag(DragDropKeyStates.ControlKey)) wanted = DragDropEffects.Copy;
        else if (keys.HasFlag(DragDropKeyStates.ShiftKey)) wanted = DragDropEffects.Move;
        else
        {
            var destRoot = SafeRoot(destination);
            wanted = sources.Count > 0 && sources.All(s => string.Equals(SafeRoot(s), destRoot, StringComparison.OrdinalIgnoreCase))
                ? DragDropEffects.Move
                : DragDropEffects.Copy;
        }
        if (!e.AllowedEffects.HasFlag(wanted))
            wanted = e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
        return wanted;
    }

    private static string SafeRoot(string path)
    {
        try { return Path.GetPathRoot(path) ?? ""; }
        catch (ArgumentException) { return ""; }
    }
}
