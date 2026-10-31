using System.Collections.ObjectModel;
using Nova.App.Infrastructure;
using Nova.Core.Model;

namespace Nova.App.ViewModels;

/// <summary>One of the two browser panes of a session. Keeps <see cref="PaneState.Tabs"/> and <see cref="Tabs"/> in sync.</summary>
public sealed class PaneViewModel : ObservableObject
{
    private TabViewModel? _activeTab;

    public PaneViewModel(PaneState state, WorkspaceViewModel owner, int index)
    {
        State = state;
        Owner = owner;
        Index = index;
        Tabs = new ObservableCollection<TabViewModel>(state.Tabs.Select(t => new TabViewModel(t, this)));
        _activeTab = state.ActiveTabIndex >= 0 && state.ActiveTabIndex < Tabs.Count ? Tabs[state.ActiveTabIndex] : Tabs.FirstOrDefault();
    }

    public PaneState State { get; }
    public WorkspaceViewModel Owner { get; }
    public int Index { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }

    public bool IsActive => Owner.ActivePaneIndex == Index;
    internal void RaiseIsActive() => OnPropertyChanged(nameof(IsActive));

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (!SetProperty(ref _activeTab, value)) return;
            State.ActiveTabIndex = value == null ? -1 : Tabs.IndexOf(value);
            Owner.OnActiveTabChanged(this);
        }
    }

    public TabViewModel AddTab(string? path, bool activate = true, int? index = null)
    {
        var state = new TabState();
        if (!string.IsNullOrWhiteSpace(path)) state.History.Navigate(path);
        return Insert(new TabViewModel(state, this), activate, index);
    }

    /// <summary>Inserts an existing tab (e.g. moved from the other pane), preserving its history.</summary>
    public TabViewModel Insert(TabViewModel tab, bool activate = true, int? index = null)
    {
        tab.Pane = this;
        var i = Math.Clamp(index ?? (ActiveTab != null ? Tabs.IndexOf(ActiveTab) + 1 : Tabs.Count), 0, Tabs.Count);
        Tabs.Insert(i, tab);
        State.Tabs.Insert(i, tab.State);
        if (activate || ActiveTab == null) ActiveTab = tab;
        else State.ActiveTabIndex = ActiveTab == null ? -1 : Tabs.IndexOf(ActiveTab);
        Owner.SaveState();
        return tab;
    }

    /// <summary>Removes a tab. The pane view disposes the tab's native view in response.</summary>
    public void Remove(TabViewModel tab)
    {
        var i = Tabs.IndexOf(tab);
        if (i < 0) return;
        var wasActive = ReferenceEquals(tab, ActiveTab);
        Tabs.RemoveAt(i);
        State.Tabs.RemoveAt(i);
        if (wasActive)
            ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(i, Tabs.Count - 1)];
        else
            State.ActiveTabIndex = ActiveTab == null ? -1 : Tabs.IndexOf(ActiveTab);
        Owner.SaveState();
    }

    public void SelectRelative(int delta)
    {
        if (Tabs.Count == 0) return;
        var i = ActiveTab == null ? 0 : Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[((i + delta) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }
}
