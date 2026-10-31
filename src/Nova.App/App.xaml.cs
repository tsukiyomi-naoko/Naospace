using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Nova.App.Services;
using Nova.App.ViewModels;
using Nova.App.Views;
using Nova.Core.Diagnostics;
using Nova.Core.Model;
using Nova.Core.Persistence;
using Nova.Windows.Shell;

namespace Nova.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\NovaWorkspace.SingleInstance";
    private const string ShowEventName = @"Local\NovaWorkspace.Show";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            var report = e.Args.Length > 1 ? e.Args[1] : Path.Combine(Path.GetTempPath(), "nova-selftest.md");
            var failures = await Diagnostics.SelfTest.RunAsync(report);
            Shutdown(failures);
            return;
        }

        // Single instance: a second launch just summons the running one.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            // This new process is in the foreground (the user just launched it), so it may hand foreground rights
            // to the running instance; otherwise Windows would only flash its taskbar button.
            Nova.Windows.Interop.Native.AllowSetForegroundWindow(Nova.Windows.Interop.Native.ASFW_ANY);
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(() => _window?.ShowAndActivate()), null, -1, false);

        var root = Environment.GetEnvironmentVariable("NOVA_WORKSPACE_CONFIG_DIR") is { Length: > 0 } custom ? custom : ConfigStore.DefaultRoot;
        var store = new ConfigStore(root);
        Log.Initialize(store.LogDirectory);
        Log.Info($"NOVA Workspace {typeof(App).Assembly.GetName().Version} starting.");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Error("Unobserved task exception", ev.Exception); ev.SetObserved(); };

        AppServices.Initialize(store, SynchronizationContext.Current ?? new DispatcherSynchronizationContext(Dispatcher));

        // Back up the last known-good configuration before anything is loaded or saved in this session.
        try { AppServices.Backups.CreateAutomaticBackupIfDue(TimeSpan.FromHours(20)); }
        catch (Exception ex) { Log.Warn("Automatic backup failed", ex); }

        var notes = LoadConfiguration();
        ThemeService.Apply(AppServices.Settings.Theme);

        _vm = new MainViewModel();
        _window = new MainWindow(_vm);
        MainWindow = _window;
        _tray = new TrayIcon(() => _window.ToggleVisibility(), () => _window.ShowAndActivate(), ExitApplication);

        if (!AppServices.Settings.StartHidden) _window.ShowAndActivate();
        else
        {
            // Create the handle so the global hotkey works while hidden.
            new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        }
        if (notes.Count > 0) AppServices.Status(string.Join(" ", notes), StatusKind.Warning);
    }

    /// <summary>Loads all documents with recovery. Returns notes about anything that was repaired.</summary>
    private static List<string> LoadConfiguration()
    {
        var store = AppServices.Store;
        var notes = new List<string>();

        var settings = store.LoadSettings();
        var catalog = store.LoadCatalog();
        var rules = store.LoadRules();
        notes.AddRange(settings.Notes);
        notes.AddRange(catalog.Notes);
        notes.AddRange(rules.Notes);

        AppServices.Settings = settings.Value;
        AppServices.Catalog = catalog.Value;
        AppServices.Rules = rules.Value;
        AppServices.ReloadRules();

        if (AppServices.Catalog.Workspaces.Count == 0)
        {
            if (catalog.ReadOnly)
            {
                notes.Add("Workspaces come from a newer NOVA version and are read-only.");
            }
            else
            {
                AppServices.Catalog.Workspaces.Add(CreateStarterWorkspace());
                AppServices.SaveCatalog();
                if (catalog.Source != LoadSource.Default)
                    notes.Add("No workspaces were found, so a starter workspace was created.");
            }
        }
        return notes;
    }

    private static WorkspaceDefinition CreateStarterWorkspace()
    {
        string Known(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var ws = new WorkspaceDefinition { Name = "General", Icon = "★" };
        ws.Pins.Add(PinNode.Folder(ShellNative.DownloadsFolder, "Downloads"));
        ws.Pins.Add(PinNode.Folder(Known(Environment.SpecialFolder.MyDocuments), "Documents"));
        ws.Pins.Add(PinNode.Folder(Known(Environment.SpecialFolder.DesktopDirectory), "Desktop"));
        ws.Pins.Add(PinNode.Group("Tools", new PinNode { Kind = PinKind.Application, Target = Path.Combine(Environment.SystemDirectory, "cmd.exe"), Label = "Command Prompt" }));
        return ws;
    }

    /// <summary>Restores a backup zip and reloads everything in place.</summary>
    public void RestoreBackup(string zip)
    {
        try
        {
            AppServices.Saver.FlushAll();
            AppServices.Backups.RestoreBackup(zip);
            var notes = LoadConfiguration();
            ThemeService.Apply(AppServices.Settings.Theme);
            _vm?.ReloadAllFromDisk();
            _window?.ResetAllViews();
            _window?.RegisterHotkey();
            AppServices.Status("Configuration restored from backup." + (notes.Count > 0 ? " " + string.Join(" ", notes) : string.Empty), StatusKind.Info);
        }
        catch (Exception ex)
        {
            Log.Error("Restore failed", ex);
            MessageBox.Show(_window!, "The backup could not be restored:\n" + ex.Message, "Restore backup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("Exiting.");
        try
        {
            if (_window != null)
            {
                _window.PrepareExit();
                _window.Close();
            }
        }
        catch (Exception ex) { Log.Error("Error while closing the main window", ex); }
        try { AppServices.Saver.FlushAll(); }
        catch (Exception ex) { Log.Error("Final save failed", ex); }
        _tray?.Dispose();
        _showWait?.Unregister(null);
        Log.Flush();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { AppServices.Saver?.FlushAll(); } catch { }
        _instanceMutex?.Dispose();
        _showEvent?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        try { AppServices.Saver.FlushAll(); } catch { }
        // Keep running: the configuration is safe and the user's session should not be lost because of one failing action.
        AppServices.Status("Unexpected error: " + e.Exception.Message + " (details in the log)", StatusKind.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Fatal unhandled exception", e.ExceptionObject as Exception);
        try { _window?.Modes.Dispose(); } catch { } // never leave a reserved AppBar strip behind
        Log.Flush();
    }
}
