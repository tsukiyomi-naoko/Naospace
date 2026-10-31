# NOVA Workspace — Architecture

## 1. Requirement analysis: contradictions and technical risks

| # | Topic | Issue | Decision |
|---|---|---|---|
| R1 | **Embedded browsing** (core requirement) | Re-implementing a file manager (icons, thumbnails, context menus, shell extensions, drag-drop, copy engine) is enormous and never quite "Windows". | Host the real Shell view through the documented `IExplorerBrowser` COM API, one instance per tab. All file semantics (rename, Recycle Bin, conflict dialog, context menu, thumbnails, sorting, keyboard) are then native by construction. Validated by the `--selftest` harness before building further on it. |
| R2 | WPF ↔ Win32 "airspace" | WPF cannot draw over a hosted HWND. | No overlays over the file area; menus/popups are separate windows (WPF popups are HWNDs, so this works). Empty-pane text is shown only when no view is visible. |
| R3 | Keyboard inside the hosted view | Shell accelerators (F2, Del, Ctrl+C/V/X, Ctrl+A…) only work if the host forwards messages to `IShellView::TranslateAccelerator`. | `ShellBrowserHost.TranslateAcceleratorCore` forwards them; NOVA's own shortcuts get the first chance; plain keys are never stolen while an in-place rename edit box has focus. |
| R4 | History persistence vs. native travel log | The Shell's travel log is in-memory and dies with the view. | `EBO_NOTRAVELLOG`; NOVA owns per-tab history in the model (persisted, restored, survives view disposal). |
| R5 | Many tabs × many workspaces | Each Shell view costs memory and threads. | Lazy creation (only the active tab of a visible pane), LRU caps: 6 live views per pane, 4 workspaces with live views. Evicted views are rebuilt from saved state transparently. |
| R6 | **Docked + expanded** | An AppBar that reserves the width of a full file browser would permanently shrink every other application. | Only the sidebar strip is reserved; when the browser is expanded the docked window extends as a flyout over other windows. |
| R7 | **Desktop-facing mode** vs. "no unsafe Explorer hacks" | True desktop embedding needs undocumented `Progman`/`WorkerW` reparenting, which breaks with Explorer restarts and updates. | Safe equivalent: a normal window kept at the bottom of the z-order (`HWND_BOTTOM` on deactivation), never above other apps. Documented in KNOWN_LIMITATIONS. |
| R8 | Auto-detection must not steal focus | Creating a Shell view can take Win32 focus. Auto-switching happens while the user works elsewhere. | Detection only changes NOVA's own state; creating new native views is deferred until NOVA is activated. NOVA never activates/moves other windows on a workspace change. |
| R9 | Detection identity | Absolute exe paths change with updates; HWND/PID are not persistent. | Rules use executable *names* + window title (contains/regex). HWND/PID used only transiently. Elevated processes that cannot be identified are ignored. Titles are never logged. |
| R10 | Shared executables (VS Code, browsers) | One exe, many projects. | Title conditions are more specific than exe-only rules and win; priority breaks ties; "never switch" rules; lock; manual override until another window is focused. |
| R11 | Drives disappearing | `Directory.Exists` on a dead network share can block for 20+ s. | All reachability probes run off the UI thread with a 3 s timeout; pins are marked "unavailable", never deleted; navigation to an unreachable folder shows a status message instead of hanging. |
| R12 | Config safety | Autosave of frequently changing tab state must never endanger workspace definitions. | Separate documents (definitions vs. state vs. snapshots vs. rules vs. settings), atomic replace with `.bak`, quarantine of corrupt files, refusal to overwrite files from a newer schema, daily zip backups. |
| R13 | Dragging pins | A pin dragged *into* a file view with a FileDrop payload would let the Shell move the real folder. | Pin drags carry only an internal id. External drops onto the sidebar report `Link`/`Copy`, never `Move`, so the source never deletes anything. |
| R14 | Windows 11 tabbed Explorer | "Active Explorer location" is ambiguous when one window has several tabs. | Capture lists all open Explorer folders, most recently focused window first, and lets the user choose. |

## 2. Technology stack

| Choice | Why |
|---|---|
| **C# / .NET 10 (LTS, supported to Nov 2028)** | Current LTS; .NET 8 support ends Nov 2026. |
| **WPF** | Mature, keyboard/accessibility support (UI Automation), `HwndHost` for native Shell views, Per-Monitor-V2 DPI. Fluent theme (`Application.ThemeMode`, .NET 9+) gives Windows 11 light/dark styling. |
| **Windows Shell COM** (`IExplorerBrowser`, `IShellView`, `IShellItem(Array)`, `IContextMenu`, `IFileOperation`) | Documented, stable since Vista, used by Explorer itself. Classic `[ComImport]` interop, no third-party packages. |
| **Win32**: `RegisterHotKey`, `SetWinEventHook`, `SHAppBarMessage`, `MonitorFromWindow`/`GetMonitorInfo`, DWM dark title bar | Supported APIs for hotkey, foreground events, docking, multi-monitor. |
| **System.Text.Json** | Human-readable, versioned, portable workspace files. SQLite was considered for history but is not justified yet (bounded lists of ≤100 entries per tab/workspace). |
| **xUnit** | Tests for everything platform-neutral. |
| WinForms (in-box) | Only `NotifyIcon` for the tray. |

No external NuGet runtime dependencies. No code from PowerToys or other projects is included (see THIRD_PARTY_NOTICES.md).

## 3. Module map

```
Nova.Core (net10.0, no Windows dependency)                     ── unit-tested on any OS
  Model/        PinNode + PinTree ops, WorkspaceDefinition/Catalog, WorkspaceState, WorkspaceSession,
                PaneState, TabState, NavigationHistory, RecentList, Snapshots, AppSettings, DetectionRule
  Persistence/  AtomicFile, JsonDocumentStore (migrate/validate/recover), Schemas, ConfigStore (layout),
                SaveScheduler (debounced, ordered), BackupService (zip backup/restore, export/import)
  Detection/    DetectionEngine (rule matching), AutoSwitchController (lock/override/suggestions), TitleHeuristics
  Input/        HotkeyGesture parsing
  Layout/       ScreenGeometry (multi-monitor EnsureVisible)
  IO/           PathProbe (non-blocking reachability), PathSegments (breadcrumbs/parent)
  Diagnostics/  Log (background file logger, retention)

Nova.Windows (net10.0-windows)                                 ── Win32 / Shell integration
  Shell/        ShellBrowserHost (HwndHost + IExplorerBrowser), ShellInterop (COM definitions),
                FileOperations (IFileOperation on STA worker), ExplorerWindows, ShellIcons, KnownFolders
  Windowing/    GlobalHotkey, ForegroundMonitor, AppBar, WindowTools (monitors, z-order, dark title bar)
  Launching/    Launcher (apps/scripts/files/URLs, window reuse, Explorer/terminal)

Nova.App (WPF exe)
  App.xaml.cs           bootstrap: single instance, config load + recovery, crash handlers, exit/flush
  Services/             AppServices (composition root, document ownership, saving), ThemeService, TrayIcon
  ViewModels/           MainViewModel, WorkspaceViewModel, PaneViewModel, TabViewModel, PinNodeViewModel
  Views/                MainWindow (sidebar + toolbar + address bar), WorkspaceBrowserView, PaneView,
                        WindowModeController (floating/docked/desktop, compact, topmost, placement)
  Dialogs/              settings, detection rules, pin editor, prompts
  Diagnostics/SelfTest  Milestone 0 integration harness (`--selftest`)
```

## 4. State ownership

* **Documents** (`AppServices`): `AppSettings`, `WorkspaceCatalog`, `RuleSet` are owned by `AppServices`; each
  `WorkspaceState` and `SnapshotCollection` is owned by its `WorkspaceViewModel`. Only the owner mutates a
  document, always on the UI thread, then calls `AppServices.Save…()`.
* **Saving**: `SaveScheduler` debounces (600 ms), serializes on the UI thread (no concurrent reads of mutable
  state), writes on a background thread with per-file ordering, and `FlushAll()` runs on exit / before
  destructive operations.
* **Tabs**: `TabState` (+ `NavigationHistory`) is the truth. A `ShellBrowserHost` is a disposable *view* of it.
  `TabViewModel.Host` is set only while that view exists.

## 5. Native browser lifecycle

```
PaneView.Sync()
  active tab has no host and pane is visible
    ├─ main window inactive → defer until Activated   (no focus stealing)
    └─ create ShellBrowserHost → BuildWindowCore:
          container "static" child HWND  ← WPF positions/hides/destroys this
          CoCreate ExplorerBrowser → Initialize(container) → SetOptions(NoTravelLog|NoBorder) → Advise(events)
          navigate to tab.CurrentPath (deferred one dispatcher tick for correct initial size)
  inactive hosts → Visibility.Collapsed (kept alive, scroll/selection preserved)
  > 6 live hosts in pane → dispose least recently used (state remains in TabState)
Workspace switch → whole WorkspaceBrowserView hidden; > 4 workspaces → oldest view disposed
Session switch / snapshot restore → panes rebuilt from the session model
Dispose: Unadvise → IExplorerBrowser.Destroy → FinalRelease → DestroyWindow(container)
```

Events: `OnNavigationComplete` → `TabViewModel.OnHostNavigated` (history push unless the navigation was our own
back/forward), recents, autosave. Focus entering a view is detected with a process-local `EVENT_OBJECT_FOCUS`
WinEvent hook to track the active pane.

Threading: everything Shell-view related runs on the WPF STA UI thread. `IFileOperation` runs on its own STA
thread per operation (owner window = NOVA) so the UI never blocks; paths (not COM objects) cross threads.

## 6. Persistence layout

```
%LOCALAPPDATA%\NOVA Workspace\
  settings.json          AppSettings (+ window placement, physical px + monitor device name)
  workspaces.json        WorkspaceCatalog: definitions, pins, groups, tools
  rules.json             RuleSet: detection rules
  state\<id>.json        WorkspaceState: sessions → panes → tabs → history; working locations; recents
  snapshots\<id>.json    SnapshotCollection
  backups\               nova-backup-*.zip (auto daily + manual + before-restore), deleted-workspaces\
  logs\                  nova-YYYYMMDD.log (14 days)
```

Every document has `schemaVersion`. Load = parse → migrate (step functions per version) → deserialize → validate
/repair (ids, indices, bounds, invalid regex/hotkey) → on failure try `.bak` → else defaults. A corrupt primary is
copied to `*.corrupt-<timestamp>` before it can be overwritten. A document with a *newer* schema is loaded
read-only and never saved.

## 7. Diagnostics & privacy

The log records lifecycle events, warnings and exceptions. It does not record window titles, file contents or
browsing history; paths appear only in failure messages where needed to diagnose the problem.
