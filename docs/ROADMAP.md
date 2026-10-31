# Roadmap and status

Legend: ✅ implemented and verified by automated tests · 🟦 implemented, compiles, **needs Windows verification**
(run `--selftest` and the manual checklist in TESTING.md) · 🟨 partial · ⬜ planned

## Milestone 0 — Feasibility and UI foundation

| Item | Status |
|---|---|
| Host real Shell views (`IExplorerBrowser`) in WPF via `HwndHost` | 🟦 `ShellBrowserHost`; checked by self-test |
| Navigation events, selection access, keyboard accelerator forwarding | 🟦 |
| Multiple simultaneous instances (tabs, dual pane) incl. hide/show and disposal | 🟦 self-test hosts two views at once |
| File operations through the Windows copy engine | 🟦 `FileOperations` (self-test copies 3 MB and verifies SHA-256, then moves) |
| Floating / docked (AppBar) window behaviour | 🟦 |
| Feasibility harness | 🟦 `NovaWorkspace.exe --selftest` |

**Acceptance criteria M0** (measurable, on Windows 11):
1. `--selftest` exits with code 0 and every line of the report is PASS/INFO.
2. Two panes show two different folders; double-clicking a folder in either navigates only that pane.
3. In a view: F2 renames, Del sends to Recycle Bin (with the Windows prompt as configured), Ctrl+C / Ctrl+V copy
   within NOVA and to/from Explorer, right-click shows the full native context menu, drag between panes copies/moves.
4. Docked left: maximized windows of other apps stop at NOVA's edge; undocking returns the space.

## Milestone 1 — Workspace foundation

| Item | Status |
|---|---|
| Application shell, single instance, tray, crash handling, logging | 🟦 |
| Workspace create / rename / icon / reorder / delete (archived) | 🟦 (model ✅) |
| Sidebar: pins, nested groups, drag-drop add & reorder, edit, remove | 🟦 (tree operations ✅) |
| Shortcut launchers: apps, scripts, files, URLs, reuse existing window | 🟦 |
| Workspace switching without state loss | 🟦 (model ✅) |
| Resizable floating mode with remembered size/position/monitor | 🟦 (placement logic ✅) |
| Global hotkey (configurable, conflict reported) | 🟦 (parsing ✅) |
| Persistent, versioned, crash-safe workspace definitions | ✅ |

**Acceptance criteria M1**: create 3 workspaces with nested groups; restart → identical; hotkey summons NOVA from any
app; a pin to a disconnected drive shows "unavailable" within 3 s and the UI never freezes; deleting a pin never
deletes files.

## Milestone 2 — Integrated browsing (critical)

| Item | Status |
|---|---|
| Real folder browser, breadcrumbs, typed address, back/forward/up/refresh, per-tab history | 🟦 (history/segments ✅) |
| Tabs (new/close/duplicate/move/middle-click), dual panes, active-pane tracking | 🟦 |
| Rename, new folder, cut/copy/paste, delete (Recycle Bin), properties | 🟦 via native view + verbs, fallback for new folder |
| Copy/Move to other pane with native conflict dialog and progress | 🟦 |
| Native context menu, drag-drop, thumbnails, sorting, details | 🟦 provided by the Shell view |
| Keyboard shortcuts | 🟦 |
| Open in Explorer / terminal, pin selection, open selection in new tabs | 🟦 |
| Middle-click a folder *inside* the view → new tab | ⬜ needs `ICommDlgBrowser`/site integration |
| Per-tab view mode/sort persistence | ⬜ (the Shell's own per-folder view memory applies) |

## Milestone 3 — Session persistence

| Item | Status |
|---|---|
| Autosave (debounced, ordered, flushed on exit) | ✅ scheduler / 🟦 wiring |
| Multiple named sessions per workspace, switch/duplicate/rename/delete | 🟦 (model ✅) |
| Restore tabs, pane locations, active tabs, dual-pane split, history after restart | 🟦 (model ✅) |
| Snapshots: save / restore / delete | 🟦 (model ✅) |
| Backup (daily auto, manual), restore with safety backup, export/import workspace | ✅ service / 🟦 UI |
| Recovery from interrupted writes, corrupt files, newer schema | ✅ |

## Milestone 4 — Desktop integration

| Item | Status |
|---|---|
| AppBar docking left/right, reposition on taskbar/DPI/resolution changes, release while hidden | 🟦 |
| Always-on-top toggle; desktop mode (stays behind other windows) | 🟦 |
| Monitor preference, disconnection/resolution fallback | 🟦 (logic ✅) |
| Foreground detection (WinEvent, debounced, title changes) | 🟦 |
| Rules editor (exe + title contains/regex, priority, never-switch), test against last window | 🟦 (engine ✅) |
| Manual association prompt, workspace lock, manual override | 🟦 (controller ✅) |
| AppBar auto-hide | ⬜ |
| Choosing the dock monitor without moving the window first | ⬜ (docks on the monitor the window is on) |

## Milestone 5 — Refinement and release (planned)

⬜ Installer (MSIX or Inno Setup) and code signing · ⬜ search (Windows Search / optional Everything integration)
· ⬜ destination suggestions for downloads (never automatic moves) · ⬜ per-workspace recent *files* opened inside
the view · ⬜ optional Explorer context-menu "Add to NOVA workspace" · ⬜ UI density tuning after real-use feedback
· ⬜ UI automation tests on Windows · ⬜ localisation.

## Continuation plan (next iteration)

1. Run `--selftest` and TESTING.md checklists A–F on Windows 11 (incl. a 150 % / 200 % DPI monitor and a
   secondary monitor). Fix whatever fails — especially R3 (accelerators) and focus behaviour.
2. Implement `IServiceProvider`/`ICommDlgBrowser` on the host site: middle-click → new tab, file-open events for
   per-workspace recent files.
3. AppBar auto-hide; dock-monitor chooser.
4. Packaging + signing; first tagged release.
