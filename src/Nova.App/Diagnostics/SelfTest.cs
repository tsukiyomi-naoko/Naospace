using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Nova.Core.Input;
using Nova.Core.Persistence;
using Nova.Core.Model;
using Nova.Windows.Shell;
using Nova.Windows.Windowing;

namespace Nova.App.Diagnostics;

/// <summary>
/// Milestone 0 feasibility harness. Run <c>NovaWorkspace.exe --selftest [report-path]</c> on a real Windows desktop.
/// It verifies the architectural assumptions that cannot be checked on a non-Windows build machine: hosting real
/// IExplorerBrowser instances in WPF (several at once), navigation events, selection access, the IFileOperation copy
/// engine (with integrity check), verbs, hotkey registration, monitor enumeration, foreground hooks and Explorer
/// window enumeration. Everything happens in a temporary folder; nothing outside it is modified.
/// Exit code = number of failed checks.
/// </summary>
public static class SelfTest
{
    private sealed record Result(string Name, bool? Passed, string Detail);

    public static async Task<int> RunAsync(string reportPath)
    {
        var results = new List<Result>();
        var sw = Stopwatch.StartNew();
        var root = Path.Combine(Path.GetTempPath(), "nova-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        var dirA = Directory.CreateDirectory(Path.Combine(root, "A")).FullName;
        var dirB = Directory.CreateDirectory(Path.Combine(root, "B")).FullName;
        Window? window = null;

        void Add(string name, bool? ok, string detail = "") => results.Add(new Result(name, ok, detail));

        async Task Check(string name, Func<Task<string>> body)
        {
            try { Add(name, true, await body()); }
            catch (Exception ex) { Add(name, false, ex.GetType().Name + ": " + ex.Message); }
        }

        try
        {
            Add("UI thread is STA (required by Shell views)", Thread.CurrentThread.GetApartmentState() == ApartmentState.STA);

            await Check("Atomic config write + recovery", () =>
            {
                var store = new ConfigStore(Path.Combine(root, "config"));
                store.Save(store.CatalogPath, new WorkspaceCatalog { Workspaces = { new WorkspaceDefinition { Name = "T" } } });
                store.Save(store.CatalogPath, new WorkspaceCatalog { Workspaces = { new WorkspaceDefinition { Name = "T" } } });
                File.WriteAllText(store.CatalogPath, "{ broken");
                var r = store.LoadCatalog();
                if (r.Source != LoadSource.Backup) throw new InvalidOperationException("Expected recovery from backup, got " + r.Source);
                return Task.FromResult("recovered from .bak");
            });

            // Test data
            var payload = new byte[3 * 1024 * 1024];
            RandomNumberGenerator.Fill(payload);
            File.WriteAllBytes(Path.Combine(dirA, "model.safetensors"), payload);
            File.WriteAllText(Path.Combine(dirA, "move-me.txt"), "move");

            window = new Window
            {
                Title = "NOVA self-test",
                Width = 900, Height = 500,
                Left = 40, Top = 40,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            window.Content = grid;
            window.Show();
            var hwnd = new WindowInteropHelper(window).Handle;

            var left = new ShellBrowserHost();
            var right = new ShellBrowserHost();
            Grid.SetColumn(right, 1);

            await Check("Host IExplorerBrowser #1 and navigate", async () =>
            {
                grid.Children.Add(left);
                var path = await NavigateAndWait(left, dirA);
                return "navigated to " + path;
            });

            await Check("Host IExplorerBrowser #2 simultaneously (dual pane) and navigate", async () =>
            {
                grid.Children.Add(right);
                var path = await NavigateAndWait(right, dirB);
                return "navigated to " + path;
            });

            await Check("Independent navigation of pane #1 (to parent and back)", async () =>
            {
                await NavigateAndWait(left, root);
                await NavigateAndWait(left, dirA);
                return "ok";
            });

            await Check("Read selection from native view", () =>
            {
                var sel = left.GetSelection();
                return Task.FromResult($"{sel.Count} item(s) selected (0 expected without user input)");
            });

            await Check("Hide/show a hosted view (tab switching)", async () =>
            {
                right.Visibility = Visibility.Collapsed;
                await Pump(200);
                right.Visibility = Visibility.Visible;
                await Pump(200);
                return "ok";
            });

            await Check("IFileOperation copy with integrity check", async () =>
            {
                var r = await FileOperations.RunAsync(FileOperationKind.Copy, new[] { Path.Combine(dirA, "model.safetensors") }, dirB, hwnd);
                if (!r.Success) throw new InvalidOperationException(r.Error ?? "operation aborted");
                var copied = Path.Combine(dirB, "model.safetensors");
                if (!File.Exists(copied)) throw new FileNotFoundException("copy missing");
                if (!SHA256.HashData(File.ReadAllBytes(copied)).SequenceEqual(SHA256.HashData(payload))) throw new InvalidDataException("hash mismatch");
                return "SHA-256 identical";
            });

            await Check("IFileOperation move", async () =>
            {
                var r = await FileOperations.RunAsync(FileOperationKind.Move, new[] { Path.Combine(dirA, "move-me.txt") }, dirB, hwnd);
                if (!r.Success) throw new InvalidOperationException(r.Error ?? "operation aborted");
                if (File.Exists(Path.Combine(dirA, "move-me.txt")) || !File.Exists(Path.Combine(dirB, "move-me.txt")))
                    throw new InvalidOperationException("file was not moved");
                return "ok";
            });

            await Check("Background verb 'NewFolder' (informational)", async () =>
            {
                var before = Directory.GetDirectories(dirB).Length;
                var invoked = right.InvokeVerb("NewFolder", background: true);
                await Pump(1500);
                var after = Directory.GetDirectories(dirB).Length;
                return invoked && after > before ? "supported" : "not supported → NOVA uses its fallback";
            });

            await Check("Refresh native view", async () => { right.Refresh(); await Pump(200); return "ok"; });

            await Check("Dispose hosted views", async () =>
            {
                grid.Children.Clear();
                left.Dispose();
                right.Dispose();
                await Pump(200);
                return "ok";
            });

            await Check("Register/unregister a global hotkey", () =>
            {
                using var hk = new GlobalHotkey(HwndSource.FromHwnd(hwnd)!, 0x4E50);
                HotkeyGesture.TryParse("Ctrl+Alt+Shift+F12", out var g);
                var ok = hk.Register(g!);
                hk.Unregister();
                return Task.FromResult(ok ? "registered" : "already in use by another app (not a NOVA failure)");
            });

            await Check("Enumerate monitors", () =>
            {
                var m = WindowTools.GetMonitors();
                if (m.Count == 0) throw new InvalidOperationException("no monitors");
                return Task.FromResult(string.Join("; ", m.Select(x => $"{x.DeviceName} {x.WorkArea.Width}x{x.WorkArea.Height}{(x.IsPrimary ? " primary" : "")}")));
            });

            await Check("Foreground hook start/stop", () =>
            {
                using var f = new ForegroundMonitor(Dispatcher.CurrentDispatcher);
                f.Start();
                f.Stop();
                return Task.FromResult("ok");
            });

            await Check("Enumerate open Explorer windows", () =>
                Task.FromResult($"{ExplorerWindows.GetOpenLocations().Count} location(s)"));

            await Check("Known folder: Downloads", () => Task.FromResult(ShellNative.DownloadsFolder));
        }
        catch (Exception ex)
        {
            Add("Self-test harness", false, ex.ToString());
        }
        finally
        {
            window?.Close();
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        var failed = results.Count(r => r.Passed == false);
        var sb = new StringBuilder();
        sb.AppendLine("# NOVA Workspace self-test");
        sb.AppendLine($"Date: {DateTimeOffset.Now:u}  ·  OS: {Environment.OSVersion}  ·  .NET {Environment.Version}  ·  {sw.Elapsed.TotalSeconds:F1}s");
        sb.AppendLine();
        foreach (var r in results)
            sb.AppendLine($"- [{(r.Passed == true ? "PASS" : r.Passed == false ? "FAIL" : "INFO")}] {r.Name}{(string.IsNullOrEmpty(r.Detail) ? "" : " — " + r.Detail)}");
        sb.AppendLine();
        sb.AppendLine(failed == 0 ? "All checks passed." : $"{failed} check(s) failed.");
        File.WriteAllText(reportPath, sb.ToString());
        return failed;
    }

    private static async Task<string> NavigateAndWait(ShellBrowserHost host, string path)
    {
        var tcs = new TaskCompletionSource<string>();
        void OnNav(object? s, ShellNavigatedEventArgs e) => tcs.TrySetResult(e.Path);
        void OnFail(object? s, ShellNavigationFailedEventArgs e) => tcs.TrySetException(new InvalidOperationException("navigation failed: " + e.Reason));
        host.Navigated += OnNav;
        host.NavigationFailed += OnFail;
        try
        {
            host.Navigate(path);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            if (done != tcs.Task) throw new TimeoutException("no NavigationComplete within 15 s");
            var result = await tcs.Task;
            if (!PathComparer.Equal(result, path)) throw new InvalidOperationException($"expected {path}, got {result}");
            return result;
        }
        finally
        {
            host.Navigated -= OnNav;
            host.NavigationFailed -= OnFail;
        }
    }

    private static Task Pump(int ms) => Task.Delay(ms);
}
