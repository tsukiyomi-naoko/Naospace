using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.Core.Models;

/// <summary>
/// Maps a foreground application (executable name, optionally window title) to a workspace.
/// Executable names, not install paths, so rules survive application updates.
/// </summary>
public partial class DetectionRule : ObservableObject
{
    public string Id { get; set; } = Ids.New();

    /// <summary>Executable name, wildcards allowed, ".exe" optional. E.g. "Mozaik*", "code".</summary>
    [ObservableProperty] private string _processName = "";

    /// <summary>Optional window-title filter. Wildcards (*, ?); a "re:" prefix means regex; otherwise substring.</summary>
    [ObservableProperty] private string? _titlePattern;

    /// <summary>Optional session to activate on match.</summary>
    [ObservableProperty] private string? _sessionId;

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private int _priority;
}
