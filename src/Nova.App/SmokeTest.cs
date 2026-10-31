using System.IO;
using System.Windows;
using System.Windows.Threading;
using Nova.App.ViewModels;
using Nova.App.Views;
using Nova.Core.Models;

namespace Nova.App;

/// <summary>
/// "--smoke-test": starts the real UI, drives the main flows through the view-models,
/// then exits with code 0 (clean) or 1 (exceptions / binding errors), writing details to nova.log.
/// Used in CI on Windows, where interactive testing isn't possible.
/// </summary>
internal static class SmokeTest
{
    public static async Task RunAsync(MainViewModel vm, MainWindow window, Diagnostics.BindingErrorListener bindings, App app)
    {
        int failures = 0;
        void Check(bool ok, string what) { if (!ok) { failures++; Diagnostics.Log("SMOKE FAIL " + what); } else Diagnostics.Log("SMOKE ok   " + what); }
        async Task Settle(int ms = 600) { await Task.Delay(ms); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        void Snap(string name)
        {
            try
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                var content = (FrameworkElement)window.Content;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
                var bg = new System.Windows.Media.DrawingVisual();
                using (var dc = bg.RenderOpen())
                    dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                bmp.Render(bg);
                bmp.Render(content);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(AppPaths.DataDirectory, $"smoke-{name}.png"));
                enc.Save(fs);
            }
            catch (Exception ex) { Diagnostics.Log("SMOKE snapshot failed: " + ex.Message); }
        }

        try
        {
            await Settle(1500);
            Check(window.IsVisible, "main window visible");
            var tab = vm.LeftPane.SelectedTab;
            Check(tab is not null, "left pane has a tab");

            var root = Path.Combine(Path.GetTempPath(), "nova-smoke-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "img10.png"), "");
            File.WriteAllText(Path.Combine(root, "img2.png"), "");

            await vm.NavigateAsync(root);
            await Settle();
            tab = vm.LeftPane.SelectedTab!;
            Check(tab.Items.Count == 4, $"enumerated folder ({tab.Items.Count} items)");
            Check(tab.Items.Count > 0 && tab.Items[0].IsDirectory, "folders sort first");
            var names = tab.Items.Select(i => i.Name).ToList();
            Check(names.IndexOf("img2.png") < names.IndexOf("img10.png"), "natural sort");
            Check(tab.Items.All(i => i.Icon is not null), "shell icons resolved");

            tab.FilterText = "*.png";
            await Settle(200);
            Check(tab.Items.Count == 2, "wildcard filter");
            tab.FilterText = "";

            await tab.NavigateAsync(Path.Combine(root, "sub"));
            await Settle();
            await tab.GoBackAsync();
            await Settle();
            Check(string.Equals(tab.Path, root, StringComparison.OrdinalIgnoreCase), "back navigation");

            vm.PinPaths(new[] { root });
            Check(vm.ActiveWorkspace.AllPinItems().Any(p => p.Target.Contains("nova-smoke")), "pin current folder");
            vm.AddToWorkingSet(Path.Combine(root, "sub"));
            Check(vm.ActiveSession.WorkingSet.Count > 0, "working set");
            await Settle();
            Snap("single");

            vm.IsDualPane = true;
            await Settle();
            Check(vm.RightPane.Tabs.Count == 1, "dual pane opens");
            Snap("dual");
            vm.IsCompact = true;
            await Settle();
            Snap("compact");
            vm.IsCompact = false;
            await Settle();
            Check(vm.LeftPane.SelectedTab?.Path == root, "compact toggle preserves browsing state");

            var created = vm.CreateFolder(root, "New folder");
            await Settle(900);
            Check(created is not null && Directory.Exists(Path.Combine(root, created)), "create folder");
            Check(vm.LeftPane.SelectedTab!.Items.Any(i => i.Name == created), "watcher refreshed listing");

            var ws = vm.CreateWorkspace("Smoke");
            await Settle();
            Check(vm.ActiveWorkspace == ws, "create + switch workspace");
            var s2 = vm.CreateSession("Second", fromCurrent: true);
            await Settle();
            Check(vm.ActiveSession == s2, "create session");
            vm.SwitchWorkspace(vm.Workspaces[0], null, manual: true);
            await Settle();
            Check(vm.LeftPane.SelectedTab?.Path == root, "session restored on workspace switch");

            vm.OnForeground(new Nova.Core.Services.ForegroundInfo("notepad.exe", "x - Notepad"));
            vm.Save();
            Check(File.Exists(Path.Combine(AppPaths.DataDirectory, "state.json")), "state saved");

            vm.DeleteWorkspace(ws);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
        catch (Exception ex)
        {
            failures++;
            Diagnostics.Error("smoke", ex);
        }

        Diagnostics.Log($"SMOKE done: failures={failures} exceptions={Diagnostics.ErrorCount} bindingErrors={bindings.Count}");
        var code = failures + Diagnostics.ErrorCount > 0 ? 1 : 0;
        app.ExitApplication();
        Environment.Exit(code);
    }
}
