# Known limitations (v0.1.0)

**Verification**
* The Windows-specific code (Shell view hosting, copy engine, AppBar, hotkey, foreground hook, tray, WPF UI) was
  compiled but **not executed** by the developer. Run `--selftest` and docs/TESTING.md before relying on it.

**File browser**
* Middle-clicking (or Ctrl+clicking) a folder *inside* the native view does not yet open a new tab; use the sidebar,
  the tab context menu, or *More → Open selected folder(s) in new tab*. Needs `ICommDlgBrowser` integration.
* Per-workspace "recent files" only records files opened from NOVA pins, not files opened by double-clicking inside
  the view (same reason).
* The view mode (details/icons) and sort order follow Windows' own per-folder memory, not a per-tab NOVA setting.
* The embedded Shell view may stay light in dark mode on some Windows builds: Windows exposes no supported API to force
  dark mode on a hosted Shell view.
* WPF content cannot overlap the native view (airspace); this is by design.
* Typing `shell:` locations in the address bar depends on Windows' parsing support.

**Window modes**
* *Desktop mode* is a safe equivalent of desktop embedding: a normal window kept behind other windows. True embedding
  into the desktop layer would require undocumented Explorer internals and is intentionally not used.
* Docking uses the monitor the window is currently on (move the floating window first). AppBar auto-hide is not yet
  implemented. While docked and expanded, the browser part overlaps other windows instead of reserving space (by design).
* Clicking the tray icon always shows NOVA (it does not toggle to hidden).

**Detection**
* Elevated (administrator) applications cannot be identified by a non-elevated NOVA and are ignored.
* Title-change tracking listens to global name-change events; this is cheap but not zero cost.
* "Remember this project/window" uses a title substring suggested by a heuristic; edit it in the bar or in the rules editor.

**General**
* `Application.ThemeMode` (Fluent theme) is marked experimental by .NET 9/10 (diagnostic WPF0001 is suppressed).
* No installer yet; run from the publish folder. Not code-signed (SmartScreen may warn).
* English UI only.
