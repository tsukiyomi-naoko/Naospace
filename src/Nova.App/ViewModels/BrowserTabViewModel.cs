using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Core.Models;
using Nova.Core.Util;

namespace Nova.App.ViewModels;

public sealed record Crumb(string Label, string Path);

/// <summary>
/// One browser tab. Enumerates folders off the UI thread, sorts/filters in memory,
/// watches the folder for changes, and keeps its <see cref="TabState"/> (which is what
/// gets persisted with the session) up to date.
/// </summary>
public sealed partial class BrowserTabViewModel : ObservableObject, IDisposable
{
    private readonly Func<bool> _showHidden;
    private List<FileItemViewModel> _all = new();
    private CancellationTokenSource? _loadCts;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _refreshDebounce;

    public BrowserTabViewModel(TabState state, Func<bool> showHidden)
    {
        State = state;
        _showHidden = showHidden;
        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshDebounce.Tick += async (_, _) =>
        {
            _refreshDebounce.Stop();
            await ReloadAsync(preserveSelection: true, currentSelection: CurrentSelectionProvider?.Invoke());
        };
    }

    public TabState State { get; }

    public event Action<string>? Navigated;
    /// <summary>Raised after items are (re)loaded; carries names the view should select.</summary>
    public event Action<IReadOnlyCollection<string>>? SelectionRequested;

    [ObservableProperty] private ObservableCollection<FileItemViewModel> _items = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _isEditingAddress;
    [ObservableProperty] private string _addressText = "";
    [ObservableProperty] private IReadOnlyList<Crumb> _breadcrumbs = Array.Empty<Crumb>();
    [ObservableProperty] private string? _selectionText;

    public bool HasLoaded { get; private set; }
    public string Path => State.Path;
    public string Title => string.IsNullOrEmpty(State.Path) ? "New tab" : PathUtil.DisplayName(State.Path);
    public bool CanGoBack => State.CanGoBack;
    public bool CanGoForward => State.CanGoForward;
    public bool CanGoUp => PathUtil.Parent(State.Path) is not null;
    public SortField SortBy => State.SortBy;
    public bool SortDescending => State.SortDescending;
    public string StatusText => Error ?? (Items.Count == _all.Count
        ? $"{_all.Count:N0} items"
        : $"{Items.Count:N0} of {_all.Count:N0} items");

    partial void OnFilterTextChanged(string value) => ApplyView();
    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(StatusText));

    public Task NavigateAsync(string path)
    {
        path = PathUtil.Normalize(Environment.ExpandEnvironmentVariables(path));
        State.Navigate(path);
        return AfterLocationChange(selectAfter: null);
    }

    public Task GoBackAsync() => State.GoBack() is null ? Task.CompletedTask : AfterLocationChange(null);
    public Task GoForwardAsync() => State.GoForward() is null ? Task.CompletedTask : AfterLocationChange(null);

    public Task GoUpAsync()
    {
        var parent = PathUtil.Parent(State.Path);
        if (parent is null) return Task.CompletedTask;
        var from = PathUtil.DisplayName(State.Path);
        State.Navigate(parent);
        return AfterLocationChange(selectAfter: from); // Explorer-style: highlight where we came from
    }

    /// <summary>First load after restoring a session (no history push).</summary>
    public Task InitializeAsync() => AfterLocationChange(null);

    private async Task AfterLocationChange(string? selectAfter)
    {
        FilterText = "";
        IsEditingAddress = false;
        AddressText = State.Path;
        Breadcrumbs = PathUtil.Breadcrumbs(State.Path).Select(c => new Crumb(c.Label, c.Path)).ToList();
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        Watch(State.Path);
        await ReloadAsync(preserveSelection: false, selectAfter is null ? null : new[] { selectAfter });
        if (Error is null) Navigated?.Invoke(State.Path);
    }

    public async Task ReloadAsync(bool preserveSelection = false, IReadOnlyCollection<string>? select = null, IReadOnlyCollection<string>? currentSelection = null)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var path = State.Path;
        var showHidden = _showHidden();
        IsLoading = true;
        try
        {
            var (items, error) = await Task.Run(() => Enumerate(path, showHidden, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            Error = error;
            _all = items;
            HasLoaded = true;
            ApplyView();
            var wanted = select ?? (preserveSelection ? currentSelection : null);
            if (wanted is { Count: > 0 }) SelectionRequested?.Invoke(wanted);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_loadCts, cts)) IsLoading = false;
        }
    }

    private static (List<FileItemViewModel>, string?) Enumerate(string path, bool showHidden, CancellationToken ct)
    {
        var list = new List<FileItemViewModel>();
        if (string.IsNullOrWhiteSpace(path)) return (list, "No location");
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return (list, $"\"{path}\" does not exist or is not reachable.");
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
                ReturnSpecialDirectories = false,
            };
            foreach (var info in dir.EnumerateFileSystemInfos("*", options))
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new FileItemViewModel(info));
            }
            return (list, null);
        }
        catch (UnauthorizedAccessException) { return (list, "Access denied."); }
        catch (IOException ex) { return (list, ex.Message); }
    }

    public void SetSort(SortField field)
    {
        if (State.SortBy == field) State.SortDescending = !State.SortDescending;
        else { State.SortBy = field; State.SortDescending = false; }
        OnPropertyChanged(nameof(SortBy));
        OnPropertyChanged(nameof(SortDescending));
        ApplyView();
    }

    private void ApplyView()
    {
        IEnumerable<FileItemViewModel> q = _all;
        var filter = FilterText?.Trim();
        if (!string.IsNullOrEmpty(filter)) q = q.Where(i => WildcardPattern.MatchesFilter(i.Name, filter));

        var cmp = NaturalStringComparer.Instance;
        // Folders always first (like Explorer), regardless of direction.
        var ordered = q.OrderByDescending(i => i.IsDirectory);
        IOrderedEnumerable<FileItemViewModel> sorted = State.SortBy switch
        {
            SortField.Modified => State.SortDescending ? ordered.ThenByDescending(i => i.Modified) : ordered.ThenBy(i => i.Modified),
            SortField.Size => State.SortDescending ? ordered.ThenByDescending(i => i.Size ?? -1) : ordered.ThenBy(i => i.Size ?? -1),
            SortField.Type => State.SortDescending ? ordered.ThenByDescending(i => i.Extension, StringComparer.OrdinalIgnoreCase) : ordered.ThenBy(i => i.Extension, StringComparer.OrdinalIgnoreCase),
            _ => State.SortDescending ? ordered.ThenByDescending(i => i.Name, cmp) : ordered.ThenBy(i => i.Name, cmp),
        };
        if (State.SortBy != SortField.Name) sorted = sorted.ThenBy(i => i.Name, cmp);
        Items = new ObservableCollection<FileItemViewModel>(sorted);
        OnPropertyChanged(nameof(StatusText));
    }

    private void Watch(string path)
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            if (!Directory.Exists(path)) return;
            var w = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
            FileSystemEventHandler h = (_, _) => ScheduleRefresh();
            w.Created += h; w.Deleted += h; w.Changed += h;
            w.Renamed += (_, _) => ScheduleRefresh();
            w.Error += (_, _) => ScheduleRefresh();
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Some network/virtual locations can't be watched; manual refresh (F5) still works.
        }
    }

    private void ScheduleRefresh() =>
        Application.Current?.Dispatcher.BeginInvoke(() => { _refreshDebounce.Stop(); _refreshDebounce.Start(); });

    /// <summary>Lets the view supply the current selection when a watcher-driven refresh happens.</summary>
    public Func<IReadOnlyCollection<string>>? CurrentSelectionProvider { get; set; }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _watcher?.Dispose();
        _refreshDebounce.Stop();
    }
}
