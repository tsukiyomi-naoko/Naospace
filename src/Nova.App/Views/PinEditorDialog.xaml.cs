using System.Windows;
using Microsoft.Win32;
using Nova.Core.Models;

namespace Nova.App.Views;

/// <summary>Edits a copy of the pin's fields; the pin is only changed on Save.</summary>
public partial class PinEditorDialog : Window
{
    private readonly PinItem _pin;

    public PinEditorDialog(PinItem pin)
    {
        InitializeComponent();
        _pin = pin;
        KindBox.ItemsSource = Enum.GetValues<PinKind>();
        NameBox.Text = pin.Name;
        KindBox.SelectedItem = pin.Kind;
        TargetBox.Text = pin.Target;
        ArgsBox.Text = pin.Arguments ?? "";
        WorkDirBox.Text = pin.WorkingDirectory ?? "";
        NoteBox.Text = pin.Note ?? "";
        AdminBox.IsChecked = pin.RunAsAdministrator;
        TargetBox.TextChanged += (_, _) => UpdateResolved();
        UpdateResolved();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void UpdateResolved()
    {
        var expanded = Environment.ExpandEnvironmentVariables(TargetBox.Text);
        Resolved.Text = TargetBox.Text.Contains("${") ? "Workspace variables are resolved when the pin is opened." :
            expanded != TargetBox.Text ? "→ " + expanded : "";
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (KindBox.SelectedItem is PinKind.Folder)
        {
            var d = new OpenFolderDialog();
            if (d.ShowDialog(this) == true) TargetBox.Text = d.FolderName;
        }
        else
        {
            var d = new OpenFileDialog { DereferenceLinks = false };
            if (d.ShowDialog(this) == true) TargetBox.Text = d.FileName;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TargetBox.Text))
        {
            MessageBox.Show(this, "A target is required.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _pin.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? TargetBox.Text.Trim() : NameBox.Text.Trim();
        _pin.Kind = KindBox.SelectedItem is PinKind k ? k : PinKind.Folder;
        _pin.Target = TargetBox.Text.Trim();
        _pin.Arguments = NullIfEmpty(ArgsBox.Text);
        _pin.WorkingDirectory = NullIfEmpty(WorkDirBox.Text);
        _pin.Note = NullIfEmpty(NoteBox.Text);
        _pin.RunAsAdministrator = AdminBox.IsChecked == true;
        DialogResult = true;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
