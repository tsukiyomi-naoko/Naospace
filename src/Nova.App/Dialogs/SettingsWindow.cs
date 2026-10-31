using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.Core.Input;
using Nova.Core.Model;

namespace Nova.App.Dialogs;

public sealed class SettingsWindow : DialogBase
{
    private readonly AppSettings _settings;
    private readonly TextBox _hotkey;
    private readonly ComboBox _theme;
    private readonly CheckBox _tray, _startHidden, _detect, _offer, _expand, _confirmClose;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "NOVA Workspace settings";
        var root = new StackPanel { Margin = new Thickness(14), Width = 440 };

        root.Children.Add(Label("Global summon hotkey (click the box and press the key combination):"));
        _hotkey = new TextBox { Text = settings.SummonHotkey, MinHeight = 26, Padding = new Thickness(4, 2, 4, 2), IsReadOnlyCaretVisible = true };
        System.Windows.Automation.AutomationProperties.SetName(_hotkey, "Global summon hotkey");
        _hotkey.PreviewKeyDown += CaptureHotkey;
        root.Children.Add(_hotkey);

        root.Children.Add(Label("Theme:"));
        _theme = new ComboBox { ItemsSource = Enum.GetValues<Nova.Core.Model.ThemeMode>(), SelectedItem = settings.Theme, MinHeight = 26 };
        root.Children.Add(_theme);

        _tray = Check("Closing the window keeps NOVA running in the notification area", settings.CloseToTray);
        _startHidden = Check("Start hidden (summon with the hotkey or tray icon)", settings.StartHidden);
        _expand = Check("Show the file browser automatically when a folder is opened", settings.ExpandBrowserWhenOpeningFolder);
        _confirmClose = Check("Ask before closing a tab", settings.ConfirmTabClose);
        _detect = Check("Switch workspace automatically from the foreground application (detection rules)", settings.AutoDetectionEnabled);
        _offer = Check("After a manual switch, offer to remember the association", settings.OfferAssociationOnManualSwitch);
        foreach (var c in new[] { _tray, _startHidden, _expand, _confirmClose, _detect, _offer }) root.Children.Add(c);

        root.Children.Add(OkCancel(Accept, () => DialogResult = false, "Save"));
        Content = root;
    }

    private static CheckBox Check(string text, bool value) =>
        new() { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, 8, 0, 0) };

    private void CaptureHotkey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Enter or Key.Escape && Keyboard.Modifiers == ModifierKeys.None) return; // keep dialog navigation
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        var text = string.Join("+", parts);
        _hotkey.Text = HotkeyGesture.TryParse(text, out var g) ? g!.ToString() : text;
    }

    private void Accept()
    {
        if (!HotkeyGesture.TryParse(_hotkey.Text, out var g) || g == null)
        {
            MessageBox.Show(this, "The hotkey needs at least one modifier (Ctrl, Alt, Shift or Win) and a key, e.g. Ctrl+Alt+N.", "Invalid hotkey");
            return;
        }
        _settings.SummonHotkey = g.ToString();
        _settings.Theme = _theme.SelectedItem is Nova.Core.Model.ThemeMode t ? t : Nova.Core.Model.ThemeMode.System;
        _settings.CloseToTray = _tray.IsChecked == true;
        _settings.StartHidden = _startHidden.IsChecked == true;
        _settings.ExpandBrowserWhenOpeningFolder = _expand.IsChecked == true;
        _settings.ConfirmTabClose = _confirmClose.IsChecked == true;
        _settings.AutoDetectionEnabled = _detect.IsChecked == true;
        _settings.OfferAssociationOnManualSwitch = _offer.IsChecked == true;
        DialogResult = true;
    }
}
