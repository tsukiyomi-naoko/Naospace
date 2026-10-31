using System.Windows;
using System.Windows.Controls;
using Nova.App.Services;

namespace Nova.App.Dialogs;

/// <summary>Shared look and behaviour for NOVA's small modal dialogs.</summary>
public class DialogBase : Window
{
    public DialogBase()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 12;
        SetResourceReference(BackgroundProperty, "Nova.Background");
        SetResourceReference(ForegroundProperty, "Nova.Foreground");
        SourceInitialized += (_, _) => ThemeService.ApplyTo(this);
    }

    protected static StackPanel OkCancel(Action ok, Action cancel, string okText = "OK")
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var okButton = new Button { Content = okText, IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        okButton.Click += (_, _) => ok();
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        cancelButton.Click += (_, _) => cancel();
        panel.Children.Add(okButton);
        panel.Children.Add(cancelButton);
        return panel;
    }

    protected static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 6, 0, 2) };
}

public sealed class TextPromptWindow : DialogBase
{
    private readonly TextBox _box;

    private TextPromptWindow(string title, string prompt, string initial)
    {
        Title = title;
        var root = new StackPanel { Margin = new Thickness(14), MinWidth = 360 };
        root.Children.Add(Label(prompt));
        _box = new TextBox { Text = initial, MinHeight = 26, Padding = new Thickness(4, 2, 4, 2) };
        System.Windows.Automation.AutomationProperties.SetName(_box, prompt);
        root.Children.Add(_box);
        root.Children.Add(OkCancel(() => DialogResult = true, () => DialogResult = false));
        Content = root;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    /// <summary>Returns the entered text, or null if cancelled.</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial)
    {
        var w = new TextPromptWindow(title, prompt, initial) { Owner = owner };
        return w.ShowDialog() == true ? w._box.Text : null;
    }
}

public sealed class WorkspaceEditorWindow : DialogBase
{
    private readonly TextBox _name;
    private readonly TextBox _icon;
    private readonly CheckBox _downloads;

    public WorkspaceEditorWindow(string title, string name, string icon, bool isNew)
    {
        Title = title;
        var root = new StackPanel { Margin = new Thickness(14), MinWidth = 360 };
        root.Children.Add(Label("Name:"));
        _name = new TextBox { Text = name, MinHeight = 26, Padding = new Thickness(4, 2, 4, 2) };
        System.Windows.Automation.AutomationProperties.SetName(_name, "Workspace name");
        root.Children.Add(_name);
        root.Children.Add(Label("Icon (an emoji or one or two characters):"));
        _icon = new TextBox { Text = icon, MinHeight = 26, Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(4, 2, 4, 2), MaxLength = 4 };
        System.Windows.Automation.AutomationProperties.SetName(_icon, "Workspace icon");
        root.Children.Add(_icon);
        _downloads = new CheckBox { Content = "Add a Downloads pin", IsChecked = true, Margin = new Thickness(0, 10, 0, 0), Visibility = isNew ? Visibility.Visible : Visibility.Collapsed };
        root.Children.Add(_downloads);
        root.Children.Add(OkCancel(() =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) { _name.Focus(); return; }
            DialogResult = true;
        }, () => DialogResult = false));
        Content = root;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    public string WorkspaceName => _name.Text.Trim();
    public string WorkspaceIcon => _icon.Text.Trim();
    public bool IncludeDownloads => _downloads.IsChecked == true;
}
