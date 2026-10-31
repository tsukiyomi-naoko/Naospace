using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Nova.Windows.Windowing;
using NovaTheme = Nova.Core.Model.ThemeMode;

namespace Nova.App.Services;

/// <summary>
/// Light/dark theming. Standard WPF controls use the built-in Fluent theme (Application.ThemeMode, .NET 9+);
/// NOVA's own surfaces use the palette dictionaries in Themes/. "System" follows the Windows app-mode setting live.
/// </summary>
public static class ThemeService
{
    private static NovaTheme _mode = NovaTheme.System;
    private static bool _hooked;

    public static bool IsDark { get; private set; }

    public static void Apply(NovaTheme mode)
    {
        _mode = mode;
        if (!_hooked)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _hooked = true;
        }
        IsDark = mode == NovaTheme.Dark || (mode == NovaTheme.System && SystemUsesDarkMode());

        var app = Application.Current;
#pragma warning disable WPF0001 // Fluent ThemeMode is marked experimental in .NET 9/10 but is the supported path for Windows 11 styling.
        app.ThemeMode = mode switch
        {
            NovaTheme.Light => ThemeMode.Light,
            NovaTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001

        var merged = app.Resources.MergedDictionaries;
        // Match NOVA's own palette exactly (WPF's Fluent dictionaries have similar names).
        var palette = merged.FirstOrDefault(d => d.Source != null &&
            (d.Source.OriginalString == LightPalette || d.Source.OriginalString == DarkPalette));
        var replacement = new ResourceDictionary { Source = new Uri(IsDark ? DarkPalette : LightPalette, UriKind.Relative) };
        if (palette != null)
        {
            var index = merged.IndexOf(palette);
            merged[index] = replacement;
        }
        else
        {
            merged.Add(replacement);
        }

        BuildDerivedStyles(app);
        foreach (Window w in app.Windows) WindowTools.UseDarkTitleBar(w, IsDark);
    }

    private const string LightPalette = "Themes/Light.xaml";
    private const string DarkPalette = "Themes/Dark.xaml";

    /// <summary>
    /// Compact item styles must be based on the *active* theme's implicit styles (Fluent), which only exist after
    /// ThemeMode is applied, so they are built here rather than in XAML. XAML refers to them with DynamicResource.
    /// </summary>
    private static void BuildDerivedStyles(Application app)
    {
        var treeBase = app.TryFindResource(typeof(TreeViewItem)) as Style;
        if (treeBase == null) Nova.Core.Diagnostics.Log.Warn("Fluent TreeViewItem style not found; using the classic theme style.");
        var tree = new Style(typeof(TreeViewItem), treeBase);
        tree.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay }));
        tree.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 2, 1)));
        tree.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 24.0));
        tree.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding("Label")));
        app.Resources["Nova.CompactTreeItem"] = tree;

        var listBase = app.TryFindResource(typeof(ListBoxItem)) as Style;
        var list = new Style(typeof(ListBoxItem), listBase);
        list.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
        list.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 24.0));
        app.Resources["Nova.CompactListItem"] = list;
    }

    public static void ApplyTo(Window window) => WindowTools.UseDarkTitleBar(window, IsDark);

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != NovaTheme.System || e.Category != UserPreferenceCategory.General) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(_mode));
    }

    public static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}
