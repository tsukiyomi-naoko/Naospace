using System.Windows;
using System.Windows.Threading;
using Nova.App.Platform;
using Nova.App.ViewModels;
using Nova.App.Views;
using Nova.Core.Models;
using Nova.Core.Persistence;

namespace Nova.App;

public partial class App : Application
{
    private SingleInstance? _instance;
    private MainViewModel? _vm;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            _instance.SignalFirstInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => { if (a.ExceptionObject is Exception x) Diagnostics.Error("fatal", x); };
        TaskScheduler.UnobservedTaskException += (_, a) => { Diagnostics.Error("task", a.Exception); a.SetObserved(); };
        base.OnStartup(e);
        var smoke = e.Args.Contains("--smoke-test");
        Diagnostics.BindingErrorListener? bindings = null;
        if (smoke)
        {
            bindings = new Diagnostics.BindingErrorListener();
            System.Diagnostics.PresentationTraceSources.Refresh();
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(bindings);
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            Diagnostics.Log("SMOKE start");
        }

        var store = new StateStore(AppPaths.DataDirectory);
        var (state, report) = store.Load();
        ApplyTheme(state.Settings.Theme);
        LoadDensityStyles();

        _vm = new MainViewModel(state, store);
        _window = new MainWindow(_vm);
        _instance.ListenForActivation(() => Dispatcher.BeginInvoke(() => _window.ShowAndActivate()));

        _vm.Initialize();
        if (!e.Args.Contains("--background")) _window.Show();

        if (smoke)
        {
            _ = SmokeTest.RunAsync(_vm, _window, bindings!, this);
            return;
        }

        if (report.Recovered)
            MessageBox.Show(_window,
                $"NOVA could not read its saved state ({report.Error}).\n\n" +
                (report.RecoveredFrom is not null
                    ? $"It was restored from backup {report.RecoveredFrom}. The unreadable file was kept next to it."
                    : "No usable backup was found, so NOVA started fresh. The unreadable file was kept."),
                "NOVA Workspace — recovered", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void LoadDensityStyles()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/NovaWorkspace;component/Themes/Density.xaml", UriKind.Absolute),
        });
    }

    public static void ApplyTheme(ThemePreference pref)
    {
        Current.ThemeMode = pref switch
        {
            ThemePreference.Dark => ThemeMode.Dark,
            ThemePreference.Light => ThemeMode.Light,
            _ => ThemeMode.System,
        };
    }

    public void ExitApplication()
    {
        _window?.PrepareForExit();
        _window?.Close();   // saves window bounds into settings
        _vm?.Shutdown();    // then persists everything
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Persist first so a UI bug can never cost the user their workspaces.
        Diagnostics.Error("dispatcher", e.Exception);
        try { _vm?.Save(); } catch { /* already failing */ }
        if (Environment.GetCommandLineArgs().Contains("--smoke-test")) { e.Handled = true; return; }
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nNOVA saved your workspaces and will keep running.",
            "NOVA Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
