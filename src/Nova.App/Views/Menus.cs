using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nova.App.Services;
using Nova.Core.Diagnostics;

namespace Nova.App.Views;

internal static class Menus
{
    public static MenuItem Item(string header, Action action, string? gesture = null, bool enabled = true, bool? isChecked = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty, IsEnabled = enabled };
        if (isChecked.HasValue) { mi.IsCheckable = true; mi.IsChecked = isChecked.Value; }
        mi.Click += (_, _) => Commands.TryRun(action);
        return mi;
    }

    public static MenuItem Sub(string header, IEnumerable<object> items, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        foreach (var i in items) mi.Items.Add(i);
        if (mi.Items.Count == 0) { mi.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false }); }
        return mi;
    }

    /// <summary>Opens a menu below a button (for "⋯" style buttons).</summary>
    public static void Show(FrameworkElement anchor, params object[] items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = anchor is Window ? PlacementMode.MousePoint : PlacementMode.Bottom };
        foreach (var i in items) menu.Items.Add(i);
        menu.IsOpen = true;
    }
}

internal static class Commands
{
    /// <summary>Runs a UI action and turns failures into a visible status message instead of a crash.</summary>
    public static void TryRun(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Log.Warn("Command failed", ex);
            AppServices.Status(ex.Message, StatusKind.Error);
        }
    }
}
