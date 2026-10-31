# Changelog

## 0.1.0 — 2026-10-31 (first vertical slice)

### Added
- Solution structure: `Nova.Core` (platform-neutral), `Nova.Windows` (Win32/Shell), `Nova.App` (WPF), `Nova.Core.Tests`.
- Workspace model: unlimited workspaces, nested pin groups, folder/file/application/script/URL pins, working
  locations, per-workspace recent folders, sessions, snapshots.
- Crash-safe persistence: versioned JSON documents, atomic replace with backup, corrupt-file quarantine and recovery,
  newer-schema read-only protection, migrations (v0→v1 example), validation/repair, debounced ordered autosave,
  zip backup/restore, workspace export/import, archived deletion.
- Integrated browser built on `IExplorerBrowser`: tabs, dual pane, breadcrumbs, address entry, per-tab persisted
  history, native selection/context menu/drag-drop/rename/delete, toolbar verbs, Copy/Move to other pane through
  `IFileOperation`, open in Explorer/terminal.
- Window modes: floating with per-monitor placement memory, AppBar docking (left/right), desktop mode, always on top,
  compact sidebar mode, tray icon, configurable global hotkey, single instance.
- Detection: foreground executable + title rules with priorities, never-switch rules, lock, manual override,
  "remember this?" suggestions, rules editor with live test.
- Light/dark theme (follows Windows), accessibility names, Per-Monitor-V2 DPI manifest.
- `--selftest` Windows integration harness; GitHub Actions workflow (Windows build, tests, publish, self-test).
- Docs: architecture, roadmap/status, testing checklists, known limitations.
