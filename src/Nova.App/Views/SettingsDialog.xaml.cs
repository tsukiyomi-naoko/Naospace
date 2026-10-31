using System.Windows;
using Nova.App.Platform;
using Nova.Core.Models;

namespace Nova.App.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        DetectionBox.ItemsSource = Enum.GetValues<DetectionMode>();
        ThemeBox.ItemsSource = Enum.GetValues<ThemePreference>();
        DockBox.ItemsSource = Enum.GetValues<DockMode>();
        DataContext = settings;
    }

    private void OnOpenData(object sender, RoutedEventArgs e) => ShellLauncher.OpenInExplorer(AppPaths.DataDirectory, reuse: true);
    private void OnDone(object sender, RoutedEventArgs e) => Close();
}
