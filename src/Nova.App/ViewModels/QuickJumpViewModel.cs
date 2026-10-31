using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Core.Models;
using Nova.Core.Services;

namespace Nova.App.ViewModels;

/// <summary>
/// Ctrl+K palette: fuzzy jump to any pin, working location, recent folder, session or
/// workspace — across all workspaces — or type/paste a path and press Enter.
/// </summary>
public sealed partial class QuickJumpViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public QuickJumpViewModel(MainViewModel main) => _main = main;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private JumpTarget? _selected;
    public ObservableCollection<JumpTarget> Results { get; } = new();

    partial void OnQueryChanged(string value) => Refresh();

    public void Open()
    {
        Query = "";
        Refresh();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    private void Refresh()
    {
        Results.Clear();
        foreach (var r in QuickJump.Search(_main.State, Query)) Results.Add(r);
        Selected = Results.FirstOrDefault();
    }

    public void Move(int delta)
    {
        if (Results.Count == 0) return;
        var i = Selected is null ? 0 : Results.IndexOf(Selected);
        Selected = Results[Math.Clamp(i + delta, 0, Results.Count - 1)];
    }

    /// <param name="alternate">Ctrl+Enter: open folders in a new tab.</param>
    public async Task ExecuteAsync(bool alternate)
    {
        var q = Query.Trim().Trim('"');
        // A typed path wins over fuzzy results.
        if (q.Length > 2 && (q.Contains(":\\") || q.StartsWith(@"\\") || q.StartsWith('%')) && System.IO.Directory.Exists(Environment.ExpandEnvironmentVariables(q)))
        {
            Close();
            await _main.NavigateAsync(Environment.ExpandEnvironmentVariables(q), alternate ? OpenTarget.NewTab : OpenTarget.Current);
            return;
        }
        var target = Selected;
        if (target is null) return;
        Close();
        var mode = alternate ? OpenTarget.NewTab : OpenTarget.Current;
        switch (target.Payload)
        {
            case Workspace ws: _main.SwitchWorkspace(ws, null, manual: true); break;
            case Session s: _main.SwitchWorkspace(target.Workspace, s, manual: true); break;
            case PinItem pin: await _main.OpenPinAsync(pin, mode); break;
            case WorkingLocation wl: await _main.NavigateAsync(wl.Path, mode); break;
            case RecentEntry r: await _main.NavigateAsync(r.Path, mode); break;
        }
    }
}
