# NOVA Workspace — Architecture & Technical Decisions

Status: living document · last updated 2026-10-31 (v0.1 foundation)

## 1. The problem, restated

File Explorer is organised around **locations**. Project work is organised around **contexts**: a
ComfyUI session touches the install folder, models, LoRAs, custom nodes, workflows, outputs,
downloads and a few tools, all at once. Quick Access is a single flat list shared by all contexts.

NOVA's core idea is a **context switch**: activating a workspace restores *everything that
context needs*. That covers its pinned places and tools, the folders you were in, and the tabs
you had open, so navigation becomes a choice from a short list instead of a walk through the
directory tree.

The daily loop we are optimising:

1. Start working in an app (Mozaik, VS Code, ComfyUI in a browser).
2. NOVA is already showing that project's context, or switches with one click or automatically.
3. Jump to any folder in one or two actions (sidebar click, `Ctrl+K`, drag target).
4. Manage files right there (copy, move, rename, delete, drag in from Downloads).
5. Leave, come back tomorrow, everything is where it was.

## 2. Technology choice

**Decision: C# / .NET 10 (LTS) / WPF with the built-in Fluent (Windows 11) theme, plus targeted
Win32/COM interop.**

| Option | For | Against | Verdict |
|---|---|---|---|
| **WPF (.NET 10)** | Mature, fast to iterate; full Win32 interop (AppBar, hooks, hotkeys, COM shell); multiple top-level windows; simple deployment (single exe, no MSIX needed); .NET 9+ ships a real Fluent theme with Mica, light/dark and accent | Older rendering stack; not "the newest" UI framework | **Chosen** |
| WinUI 3 / Windows App SDK | Newest Microsoft UI, native Fluent | Weaker for shell-utility behaviours (AppBars, tool windows, custom window chrome edge cases); heavier packaging and runtime dependency; slower inner loop; airspace and multi-window quirks. The Files app (WinUI) shows it can be done, but at a high engineering cost | Revisit only if WPF limits us |
| Avalonia | Modern XAML, cross-platform | Cross-platform is not a goal; shell integration is all custom anyway | No |
| Tauri / Electron | Web UI velocity | Every shell feature becomes native bridge code; memory footprint; poorer "native Windows" feel | No |
| C++ / Win32 | Ultimate control & performance | Far slower product iteration for a UI-heavy app | No (C++ only if a hot spot ever demands it) |

.NET 8 support ends in November 2026, so NOVA targets **.NET 10 LTS** (supported to Nov 2028).

## 3. The integrated browser — `IExplorerBrowser` vs. our own view

The spec asks us to evaluate hosting Explorer's own view (`IExplorerBrowser`). Findings:

* **Hosting IExplorerBrowser** gives pixel-perfect Explorer behaviour (native context menus,
  thumbnails, column handlers) for free. However, it is a native child HWND inside WPF. That brings
  *airspace* problems: no WPF content can overlay it (no quick-jump palette above it, no drag
  adorners, and no smooth animations or theming). We would control very little of its keyboard
  handling, styling, density or state. Tabs, dual panes, session restore, filters and
  project-aware drop targets would all have to be built *around* a black box. It is also known to
  be fragile with re-parenting and DPI changes.
* **Own view (chosen)**: a virtualised WPF `ListView` fed by `System.IO` enumeration on a
  background thread, with shell integration only where it matters:
  * **File operations** go through the Windows copy engine (`IFileOperation`). That gives the
    native progress and conflict dialogs, Recycle Bin, undo records, long paths and elevation
    prompts. It is the same engine Explorer uses, so behaviour matches Explorer exactly.
  * **Icons and type names** use `SHGetFileInfo`. They are cached per extension; per-file icons
    (exe, lnk, ico) load on a background STA thread.
  * **Clipboard** uses the Explorer formats (`FileDrop` + `Preferred DropEffect`), so cut/copy/paste
    interoperates with Explorer both ways.
  * **Drag and drop** uses OLE `FileDrop` with Explorer's semantics (same drive moves, cross drive
    copies, `Ctrl` copies, `Shift` moves).
  * **Properties, Open with, terminal, reveal in Explorer** use `ShellExecuteEx` verbs.

  The one thing we don't have yet is the **full native context menu** (third-party shell
  extensions such as 7-Zip or Git). That is planned as a *"Show more options"* entry that builds
  `IContextMenu` on demand. It's an additive change.

`IExplorerBrowser` remains a possible future "native view" mode for a tab, but it is not the
foundation.

## 4. Solution layout

```
NovaWorkspace.slnx
├─ src/Nova.Core        net10.0         Platform-neutral domain logic (no WPF, no Win32)
│   ├─ Models/          Workspace, PinNode/PinGroup/PinItem, Session, TabState, WorkingLocation,
│   │                   DetectionRule, NovaState, AppSettings (observable + serializable)
│   ├─ Persistence/     StateStore (atomic, backups, recovery, migration), WorkspacePorter
│   ├─ Services/        DetectionEngine, AssociationLearner, VariableExpander, FuzzyMatcher,
│   │                   QuickJump, WorkspaceOps
│   └─ Util/            PathUtil, NaturalStringComparer, WildcardPattern, FileNameHelper
├─ src/Nova.App         net10.0-windows WPF application
│   ├─ Platform/        All native interop: Native.cs (declarations), HotkeyManager,
│   │                   ForegroundWatcher, AppBar, ShellFileOperations, ShellIcons,
│   │                   ShellLauncher, ExplorerWindows, SingleInstance, TrayIcon
│   ├─ ViewModels/      MainViewModel, BrowserPaneViewModel, BrowserTabViewModel,
│   │                   FileItemViewModel, QuickJumpViewModel
│   ├─ Views/           MainWindow, BrowserPaneView, dialogs, clipboard/drag helpers
│   ├─ Themes/          Density.xaml (compact overrides of the Fluent styles)
│   └─ SmokeTest.cs     --smoke-test: drives the real UI, logs results, exits 0/1
└─ tests/Nova.Core.Tests  xunit
```

Rules of the road:

* **Core is testable anywhere.** Every product rule (ranking, detection, persistence, path
  variables, history) lives in Core and is unit-tested. The app layer is wiring and UI.
* **All native code lives in `Platform/`.** That keeps it auditable, and it can later move to
  CsWin32-generated bindings or Vanara without touching the UI.
* **Models are observable *and* the serialization shape.** The UI binds to the same objects that
  are saved. That removes a whole class of "forgot to sync model and view-model" bugs, at the
  cost of a little purity.

## 5. Data model

```
NovaState
├─ Settings (hotkeys, detection mode, theme, dock, window bounds per mode, …)
├─ Workspaces[]
│   ├─ Name, Color, Description
│   ├─ Variables[]        COMFY = D:\ComfyUI   → pins stored as ${COMFY}\models\loras
│   ├─ Pins[]             tree of PinGroup / PinItem (Folder, File, Application, Script, Url, Command)
│   ├─ Sessions[]         named working environments
│   │   ├─ Left/Right PaneState → Tabs[] (path, back/forward history, sort)
│   │   ├─ IsDualPane, ActivePane
│   │   ├─ WorkingSet[]   task-scoped folders (label, last used)
│   │   └─ Notes
│   ├─ Recents[]          per-workspace MRU of visited folders
│   └─ Rules[]            detection: process name (+ optional title) → this workspace (+ optional session)
├─ ActiveWorkspaceId, WorkspaceLocked
└─ Learned[]              counts of manual selections, used to suggest rules
```

### Permanent vs. temporary locations: three tiers

| Tier | Lifetime | How things get there | Purpose |
|---|---|---|---|
| **Pins** | Permanent, per workspace | Deliberate: drag, `Ctrl+D`, menus | The project's map |
| **Working set** | Per *session* (task) | Explicit: `Ctrl+Shift+D`, drag, quick capture hotkey | "What I'm using for this task right now" |
| **Recent** | Automatic, capped, per workspace | Every navigation | Safety net / "where was that?" |

A working-set location can be **promoted** to a pin with one click. Clearing the working set is
cheap and safe. This keeps temporary folders out of the pin list while keeping them one click
away.

### Path variables (portability)

When you pin `D:\ComfyUI\models\loras` and the workspace defines `COMFY = D:\ComfyUI`, the
pin is stored as `${COMFY}\models\loras`. Moving ComfyUI, or importing the workspace on another
PC, then means changing **one** value. Environment variables (`%USERPROFILE%`) also work.

## 6. Persistence & safety

* State is `%LOCALAPPDATA%\NovaWorkspace\state.json`. **Portable mode:** put a file named
  `nova.portable` next to the exe and data lives in `.\data`.
* **Autosave** runs every 3 s. It serializes and compares to the last write, and writes only on
  change, so it is cheap and never misses a change. There is also a save on exit, on crash
  (dispatcher handler) and on workspace or session switches.
* **Atomic writes** use a temp file, a flush to disk, then a rename over the old file. A power
  cut can't leave a half-written file.
* **Rolling backups**: one on each successful start plus at most hourly, keeping 30.
* **Recovery**: an unreadable `state.json` is *quarantined* (kept, never overwritten) and the
  newest readable backup is used. The user is told what happened.
* **Forward safety**: a state file from a *newer* NOVA is refused, never silently downgraded.
  `SchemaVersion` plus a `Migrate` hook handle upgrades.
* Destructive file actions are always explicit. Delete goes to the Recycle Bin by default;
  `Shift+Delete` is permanent and confirmed by the shell. Dragging a *pin* out of the sidebar
  never offers "move", so an overshoot can't relocate a project folder.

## 7. Desktop integration

| Capability | Implementation |
|---|---|
| Global hotkeys | `RegisterHotKey` on the main HWND. Toggle (default `Ctrl+Alt+Space`) shows NOVA *with the jump palette open*, so it doubles as a launcher. Capture (`Ctrl+Alt+P`) grabs the active Explorer window's folder into the working set |
| Tray / background | Closing hides to the tray; hotkeys and detection keep working |
| Compact ↔ expanded | One window, two layouts; each mode remembers its own bounds; browsing state is never torn down |
| Always on top | `Topmost` toggle |
| Docking | Real **AppBar** (`SHAppBarMessage`): Windows reserves the screen edge, so maximised apps don't cover NOVA. The edge is released while NOVA is hidden |
| Multi-monitor / DPI | Per-Monitor V2 manifest; AppBar math in physical pixels per monitor; saved positions are validated against the current virtual screen (an unplugged monitor resets to centre) |
| Single instance | Named mutex + event; a second launch activates the first |
| Window reuse | "Open in File Explorer" focuses an existing Explorer window already showing that folder (Shell.Application `ShellWindows`) |

## 8. Smart detection (deliberately simple)

* `ForegroundWatcher` uses `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`, so there's no polling. A
  title-change hook is scoped *only to the current foreground process*, so VS Code switching
  repositories is noticed without system-wide event noise.
* Rules: **executable name** (wildcards, `.exe` optional, never the install path) plus an
  optional title filter (substring, wildcard, or `re:` regex). The most specific rule wins.
* Modes: **Off**, **Suggest** (default: a one-click "Switch to …" chip) and **Automatic**
  (switches unless the workspace is 🔒 locked).
* **Learning, minimal and transparent.** When you manually pick workspace W while app A is in
  front, NOVA counts it. After 3 times it *asks* "Always use W when A is active?". For apps that
  host many projects (browsers, editors, terminals), the rule includes a title token extracted
  from the window title ("main.py - my-repo - Visual Studio Code" → `my-repo`). Nothing is
  applied silently, and "Not now" is remembered.

## 9. Testing & verification strategy

* **Unit tests** (44, xunit) cover all Core logic. They run anywhere: `dotnet test`.
* **UI smoke test**: `NovaWorkspace.exe --smoke-test` starts the real app and drives the main
  flows (navigate, sort, filter, icons, pin, working set, dual pane, compact toggle, folder
  creation and watcher refresh, workspace and session switching, save). It logs to `nova.log`,
  writes screenshots and exits 0 or 1. CI runs it on a Windows runner.
* During development the smoke test also runs **under Wine** on Linux. That is how this
  foundation was verified end to end, and it already caught a real bug: theme styles were being
  resolved before the Fluent theme was loaded.

## 10. Known technical uncertainties / risks

| Area | Risk | Mitigation / plan |
|---|---|---|
| IFileOperation & clipboard | Only smoke-tested under Wine; real Windows behaviour (dialogs, UAC, network shares) needs hands-on verification | First thing to test on Windows (see README checklist) |
| AppBar docking | Untested on real multi-monitor/mixed-DPI setups; WPF + borderless + Fluent backdrop interplay | Marked experimental; floating mode is the default |
| Windows 11 Explorer tabs | `ShellWindows` exposes tabs as separate entries sharing one HWND; capture/reuse picks the first match | Use `IShellBrowser` per tab to resolve the active one (planned) |
| Fluent theme in WPF | Still flagged experimental (WPF0001) | Isolated: density styles are `BasedOn` the Fluent ones; falling back to our own templates is local work |
| Huge folders (100k+ items) | Enumeration and sort are in memory | Virtualised list already; add incremental population if needed |
| Native context menu | Third-party shell extensions not yet shown | `IContextMenu` "Show more options" (planned) |

## 11. Open-source landscape (reuse candidates)

| Project | License | What it offers NOVA |
|---|---|---|
| **Vanara** (dahall/Vanara) | MIT | Complete, tested P/Invoke + shell COM wrappers (`IContextMenu`, `IPreviewHandler`, `IShellItemImageFactory`, thumbnails). Strong candidate when the native context menu and previews arrive |
| **Microsoft.Windows.CsWin32** | MIT | Source-generated P/Invoke. Could replace `Native.cs` declarations for compile-time correctness |
| **Files** (files-community/Files) | MIT | Reference for shell interop edge cases (context menus, thumbnails, tabs); WinUI-based so code isn't drop-in |
| **PowerToys** | MIT | Peek (preview handlers), Always On Top, FancyZones; good reference implementations for previews and window management |
| **Everything SDK** (voidtools) | Permissive SDK | Instant whole-disk filename search when Everything is installed; fall back to Windows Search (OLE DB) |
| **H.NotifyIcon.Wpf** | MIT | Richer WPF tray icon if the WinForms `NotifyIcon` becomes limiting |
| QuickLook, Seer | GPL / proprietary | Preview ideas only. **Do not copy code** (GPL is incompatible with a closed or permissive distribution) |
