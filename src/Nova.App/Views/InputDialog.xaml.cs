using System.Windows;

namespace Nova.App.Views;

public partial class InputDialog : Window
{
    private InputDialog() => InitializeComponent();

    /// <summary>Returns the entered text, or null if cancelled.</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial, bool selectStem = false)
    {
        var d = new InputDialog { Owner = owner, Title = title };
        d.Prompt.Text = prompt;
        d.Input.Text = initial;
        d.Loaded += (_, _) =>
        {
            d.Input.Focus();
            var dot = initial.LastIndexOf('.');
            if (selectStem && dot > 0) d.Input.Select(0, dot); else d.Input.SelectAll();
        };
        return d.ShowDialog() == true ? d.Input.Text.Trim() : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
