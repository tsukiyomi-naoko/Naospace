using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.Core.Models;

/// <summary>Root of everything NOVA persists.</summary>
public partial class NovaState : ObservableObject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<Workspace> Workspaces { get; set; } = new();
    [ObservableProperty] private string? _activeWorkspaceId;

    /// <summary>When set, automatic detection never switches away from the active workspace.</summary>
    [ObservableProperty] private bool _workspaceLocked;

    public List<LearnedAssociation> Learned { get; set; } = new();

    public Workspace? ActiveWorkspace =>
        Workspaces.FirstOrDefault(w => w.Id == ActiveWorkspaceId) ?? Workspaces.FirstOrDefault();
}

public enum DetectionMode { Off, Suggest, Automatic }
public enum ThemePreference { System, Dark, Light }
public enum DockMode { Floating, Left, Right }

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private string _toggleHotkey = "Ctrl+Alt+Space";
    [ObservableProperty] private string _captureHotkey = "Ctrl+Alt+P";
    [ObservableProperty] private DetectionMode _detectionMode = DetectionMode.Suggest;
    [ObservableProperty] private ThemePreference _theme = ThemePreference.System;
    [ObservableProperty] private DockMode _dock = DockMode.Floating;
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _isCompact;
    [ObservableProperty] private bool _showHiddenFiles;
    [ObservableProperty] private bool _hideOnFocusLoss;
    [ObservableProperty] private int _maxRecents = 40;

    public WindowBounds Expanded { get; set; } = new() { Width = 1180, Height = 720 };
    public WindowBounds Compact { get; set; } = new() { Width = 300, Height = 720 };
    public double SidebarWidth { get; set; } = 280;
}

public class WindowBounds
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>Manual selections observed while a given app was in the foreground.</summary>
public class LearnedAssociation
{
    public string ProcessName { get; set; } = "";
    public string? TitleKey { get; set; }
    public string WorkspaceId { get; set; } = "";
    public int Count { get; set; }
    public bool Dismissed { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.Now;
}
