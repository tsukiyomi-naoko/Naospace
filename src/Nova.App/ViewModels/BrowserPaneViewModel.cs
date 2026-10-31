using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Core.Models;

namespace Nova.App.ViewModels;

/// <summary>A browser pane: a strip of tabs. NOVA shows one or two panes.</summary>
public sealed partial class BrowserPaneViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public BrowserPaneViewModel(MainViewModel owner, int index)
    {
        _owner = owner;
        Index = index;
    }

    public int Index { get; }
    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = new();

    [ObservableProperty] private BrowserTabViewModel? _selectedTab;
    [ObservableProperty] private bool _isActive;

    public void Load(PaneState state, string fallbackPath)
    {
        foreach (var t in Tabs) Detach(t);
        Tabs.Clear();
        var tabs = state.Tabs.Count > 0 ? state.Tabs : new List<TabState> { new() { Path = fallbackPath } };
        foreach (var ts in tabs) Tabs.Add(Attach(new BrowserTabViewModel(ts, () => _owner.Settings.ShowHiddenFiles)));
        SelectedTab = Tabs[Math.Clamp(state.SelectedIndex, 0, Tabs.Count - 1)]; // triggers the first load
    }

    public void Capture(PaneState target)
    {
        target.Tabs = Tabs.Select(t => t.State).ToList();
        target.SelectedIndex = SelectedTab is null ? 0 : Math.Max(0, Tabs.IndexOf(SelectedTab));
    }

    partial void OnSelectedTabChanged(BrowserTabViewModel? value)
    {
        // Tabs restored from a session are loaded lazily, on first activation.
        if (value is not null && !value.HasLoaded && !value.IsLoading)
            _ = value.InitializeAsync();
    }

    public async Task<BrowserTabViewModel> NewTabAsync(string path, bool select = true)
    {
        var tab = Attach(new BrowserTabViewModel(new TabState { Path = "" }, () => _owner.Settings.ShowHiddenFiles));
        var at = SelectedTab is null ? Tabs.Count : Tabs.IndexOf(SelectedTab) + 1;
        Tabs.Insert(at, tab);
        if (select) SelectedTab = tab;
        await tab.NavigateAsync(path);
        return tab;
    }

    [RelayCommand]
    private void CloseTab(BrowserTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null) return;
        if (Tabs.Count == 1)
        {
            // Never leave a pane empty; closing the last tab goes "home" instead.
            _ = tab.NavigateAsync(_owner.DefaultLocation());
            return;
        }
        var idx = Tabs.IndexOf(tab);
        Detach(tab);
        Tabs.Remove(tab);
        if (SelectedTab is null || SelectedTab == tab) SelectedTab = Tabs[Math.Clamp(idx, 0, Tabs.Count - 1)];
    }

    [RelayCommand]
    private Task NewTab() => NewTabAsync(SelectedTab?.Path is { Length: > 0 } p ? p : _owner.DefaultLocation());

    [RelayCommand]
    private Task DuplicateTab(BrowserTabViewModel? tab) =>
        NewTabAsync((tab ?? SelectedTab)?.Path ?? _owner.DefaultLocation());

    public void SelectRelative(int delta)
    {
        if (Tabs.Count == 0 || SelectedTab is null) return;
        var i = (Tabs.IndexOf(SelectedTab) + delta + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[i];
    }

    private BrowserTabViewModel Attach(BrowserTabViewModel tab)
    {
        tab.Navigated += _owner.OnTabNavigated;
        return tab;
    }

    private void Detach(BrowserTabViewModel tab)
    {
        tab.Navigated -= _owner.OnTabNavigated;
        tab.Dispose();
    }
}
