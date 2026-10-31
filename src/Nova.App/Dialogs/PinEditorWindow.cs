using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nova.Core.Model;

namespace Nova.App.Dialogs;

/// <summary>Edits a pin's label, target and launch options.</summary>
public sealed class PinEditorWindow : DialogBase
{
    private readonly ComboBox _kind;
    private readonly TextBox _label, _target, _args, _workDir;
    private readonly CheckBox _reuse;
    private readonly StackPanel _launchOptions;

    public PinEditorWindow(PinNode pin)
    {
        Result = pin;
        Title = "Edit pin";
        var root = new StackPanel { Margin = new Thickness(14), Width = 480 };

        root.Children.Add(Label("Type:"));
        _kind = new ComboBox { ItemsSource = Enum.GetValues<PinKind>().Where(k => k != PinKind.Group).ToList(), SelectedItem = pin.Kind, MinHeight = 26 };
        _kind.SelectionChanged += (_, _) => UpdateVisibility();
        root.Children.Add(_kind);

        root.Children.Add(Label("Label (leave empty to use the folder or file name):"));
        _label = Box(pin.Label, "Label");
        root.Children.Add(_label);

        root.Children.Add(Label("Target (folder, file, program, script or URL):"));
        var targetRow = new DockPanel();
        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0), MinWidth = 80 };
        browse.Click += (_, _) => Browse();
        DockPanel.SetDock(browse, Dock.Right);
        targetRow.Children.Add(browse);
        _target = Box(pin.Target, "Target");
        targetRow.Children.Add(_target);
        root.Children.Add(targetRow);

        _launchOptions = new StackPanel();
        _launchOptions.Children.Add(Label("Arguments:"));
        _args = Box(pin.Arguments, "Arguments");
        _launchOptions.Children.Add(_args);
        _launchOptions.Children.Add(Label("Start in (working directory, optional):"));
        _workDir = Box(pin.WorkingDirectory, "Working directory");
        _launchOptions.Children.Add(_workDir);
        _reuse = new CheckBox
        {
            Content = "Switch to an already running window of this program instead of starting another instance",
            IsChecked = pin.ReuseExistingWindow,
            Margin = new Thickness(0, 8, 0, 0),
        };
        _launchOptions.Children.Add(_reuse);
        root.Children.Add(_launchOptions);

        root.Children.Add(OkCancel(Accept, () => DialogResult = false));
        Content = root;
        UpdateVisibility();
        Loaded += (_, _) => _target.Focus();
    }

    public PinNode Result { get; private set; }

    private static TextBox Box(string text, string name)
    {
        var b = new TextBox { Text = text, MinHeight = 26, Padding = new Thickness(4, 2, 4, 2) };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    private PinKind Kind => _kind.SelectedItem is PinKind k ? k : PinKind.Folder;

    private void UpdateVisibility()
    {
        _launchOptions.Visibility = Kind is PinKind.Application or PinKind.Script ? Visibility.Visible : Visibility.Collapsed;
        _reuse.Visibility = Kind == PinKind.Application ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Browse()
    {
        if (Kind == PinKind.Folder)
        {
            var d = new OpenFolderDialog();
            if (Directory.Exists(_target.Text)) d.InitialDirectory = _target.Text;
            if (d.ShowDialog(this) == true) _target.Text = d.FolderName;
        }
        else if (Kind != PinKind.Url)
        {
            var d = new OpenFileDialog();
            if (d.ShowDialog(this) == true) _target.Text = d.FileName;
        }
    }

    private void Accept()
    {
        var target = _target.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(target)) { _target.Focus(); return; }
        if (Kind == PinKind.Url && !Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            MessageBox.Show(this, "Please enter a complete URL, e.g. https://github.com/owner/repo", "Invalid URL");
            return;
        }
        // Missing paths are allowed on purpose (e.g. a drive that is currently disconnected).
        Result = new PinNode
        {
            Id = Result.Id,
            Kind = Kind,
            Label = _label.Text.Trim(),
            Target = target,
            Arguments = _args.Text.Trim(),
            WorkingDirectory = _workDir.Text.Trim(),
            ReuseExistingWindow = _reuse.IsChecked == true,
            IsExpanded = Result.IsExpanded,
            Children = Result.Children,
        };
        DialogResult = true;
    }
}
