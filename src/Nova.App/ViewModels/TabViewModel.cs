using Nova.App.Infrastructure;
using Nova.App.Services;
using Nova.Core.IO;
using Nova.Core.Model;
using Nova.Windows.Shell;

namespace Nova.App.ViewModels;

/// <summary>
/// One browser tab. The model (<see cref="TabState"/> with its history) is the source of truth; the native
/// <see cref="ShellBrowserHost"/> is attached only while the tab's view exists and can be recreated at any time.
/// </summary>
public sealed class TabViewModel : ObservableObject
{
    private string? _displayName;
    private bool _historyNavigation;

    public TabViewModel(TabState state, PaneViewModel pane)
    {
        State = state;
        Pane = pane;
    }

    public TabState State { get; }
    public PaneViewModel Pane { get; internal set; }

    /// <summary>The native view currently showing this tab, if any (owned and disposed by the pane view).</summary>
    public ShellBrowserHost? Host { get; internal set; }

    public string? Path => State.CurrentPath;

    public string Title => !string.IsNullOrEmpty(_displayName) ? _displayName! : DeriveTitle(Path);

    public string ToolTip => Path ?? "(empty tab)";
    public bool CanGoBack => State.History.CanGoBack;
    public bool CanGoForward => State.History.CanGoForward;
    public bool CanGoUp => PathSegments.Parent(Path) != null;

    public static string DeriveTitle(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "New tab";
        if (string.Equals(path, PathSegments.ThisPc, StringComparison.OrdinalIgnoreCase)) return "This PC";
        return PinNode.DeriveLabel(path, PinKind.Folder);
    }

    /// <summary>Navigates after checking (off the UI thread, with a timeout) that the location is reachable.</summary>
    public async void Navigate(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (path.Length == 0) return;
        if (!await EnsureReachable(path)) return;
        NavigateUnchecked(path, historyMove: false);
    }

    public async void GoBack()
    {
        var h = State.History;
        if (!h.CanGoBack) return;
        var target = h.Entries[h.Index - 1];
        if (!await EnsureReachable(target)) return;
        h.GoBack();
        NavigateUnchecked(target, historyMove: true);
    }

    public async void GoForward()
    {
        var h = State.History;
        if (!h.CanGoForward) return;
        var target = h.Entries[h.Index + 1];
        if (!await EnsureReachable(target)) return;
        h.GoForward();
        NavigateUnchecked(target, historyMove: true);
    }

    public void GoUp()
    {
        var parent = PathSegments.Parent(Path);
        if (parent != null) Navigate(parent);
    }

    public void Refresh() => Host?.Refresh();

    private static async Task<bool> EnsureReachable(string path)
    {
        var status = await PathProbe.ProbeAsync(path, expectDirectory: true);
        switch (status)
        {
            case PathStatus.Missing:
                AppServices.Status($"Folder not available: {path}", StatusKind.Warning);
                return false;
            case PathStatus.TimedOut:
                AppServices.Status($"Location is not responding (disconnected or sleeping drive?): {path}", StatusKind.Warning);
                return false;
            default:
                return true;
        }
    }

    private void NavigateUnchecked(string path, bool historyMove)
    {
        if (Host != null)
        {
            _historyNavigation = historyMove;
            Host.Navigate(path);
        }
        else
        {
            // No live view (e.g. background workspace): update the model; the view navigates when it is created.
            if (!historyMove) State.History.Navigate(path);
            _displayName = null;
            NotifyNavigation();
        }
    }

    /// <summary>Called by the pane view when the native view completed a navigation (ours or the user's).</summary>
    internal void OnHostNavigated(string path, string displayName)
    {
        if (_historyNavigation)
        {
            _historyNavigation = false;
            var h = State.History;
            if (h.Current != null && !PathComparer.Equal(h.Current, path) && h.Index >= 0)
                h.Entries[h.Index] = path; // the Shell may canonicalise the path
        }
        else
        {
            State.History.Navigate(path);
        }
        _displayName = displayName;
        NotifyNavigation();
    }

    internal void OnHostNavigationFailed(string path, string reason)
    {
        _historyNavigation = false;
        AppServices.Status($"Could not open {path}: {reason}", StatusKind.Warning);
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        Pane.Owner.OnTabNavigated(this);
    }
}
