# Testing

## What was verified for v0.1.0 — and what was not

| Level | Where | Result |
|---|---|---|
| Compile all projects (`net10.0`, `net10.0-windows` with `EnableWindowsTargeting`) | Linux, .NET SDK 10.0.114 | ✅ 0 warnings, 0 errors |
| Unit tests `Nova.Core.Tests` (60) | Linux | ✅ all pass |
| `dotnet publish -r win-x64` produces `NovaWorkspace.exe` (PE32+ GUI) | Linux | ✅ |
| **Running the app**, Shell view hosting, file operations, AppBar, hotkey, foreground hook | Windows | ❌ **not yet run** — no Windows desktop was available to the developer |

Treat every 🟦 item in ROADMAP.md as unverified until the steps below pass on Windows.

## 1. Automated

```bash
dotnet test tests/Nova.Core.Tests -c Release
```

Covers: navigation history, pin tree operations (move/regroup/cycle prevention), sessions & snapshots, working
locations, recents, atomic writes, backup recovery, corrupt-file quarantine, newer-schema read-only, v0→v1 migration,
validation/repair, save scheduler debounce/flush, backup/restore/export/import, detection rule ranking, lock and
manual override, learned rules, title heuristics, hotkey parsing, multi-monitor placement, breadcrumbs, path probes.

## 2. Windows integration self-test (Milestone 0)

```bash
NovaWorkspace.exe --selftest %TEMP%\nova-selftest.md
```

Opens a small window with two embedded Shell views for ~10 s, works only inside a temp folder, then writes the report.
Exit code = number of failed checks. Please attach the report when reporting problems. CI runs it on
`windows-latest` as an informational step.

Use a separate configuration folder for manual testing so your real setup is untouched:

```bash
set NOVA_WORKSPACE_CONFIG_DIR=%TEMP%\nova-test-config
```

## 3. Manual acceptance checklists

### Test A — ComfyUI file management
1. Create workspace "ComfyUI" (keep "Add a Downloads pin"). Add group "Models" with pins Checkpoints, LoRAs.
2. Click **Downloads** → it opens *inside* NOVA (no Explorer window appears).
3. Right-click **LoRAs** → *Open in other pane* → dual pane shows both.
4. Select a file in Downloads → **Copy ›** (Shift+F5). Repeat → the native "Replace or Skip" dialog appears.
5. Verify the copy (e.g. `certutil -hashfile <file> SHA256` on both). Try **Move ›** (Shift+F6).
6. Expected: no Explorer windows opened, no silent overwrite, progress UI for large files.

### Test B — Workspace switching
1. In ComfyUI open 3 tabs in different folders, navigate a few levels, use Back.
2. Switch to "Mozaik" (combo or Ctrl+2), navigate its folders.
3. Switch back: same tabs, same folders, Back/Forward history intact, same active tab.

### Test C — Session restoration
1. Two sessions in ComfyUI ("Animation", "Models") with different tabs; dual pane with custom split in one.
2. Save a snapshot. Change workspace. Exit via tray → Exit.
3. Restart: active workspace, sessions, tabs, panes, split ratio, active tabs and history are restored.
4. Restore the snapshot → layout returns to the snapshot.

### Test D — Native desktop behaviour
1. Move NOVA to a secondary monitor, resize. Restart → same monitor/size.
2. Always on top on → other windows stay below; off → normal.
3. Hide (— button or hotkey). Summon with Ctrl+Alt+N from another app → same workspace and state.
4. Menu → Docked left: maximize another app → it stops at NOVA's edge. Ctrl+B expands the browser as a flyout.
   Hide → the reserved strip disappears; show → it returns. Change display scaling → bar repositions.
5. Menu → On the desktop: click another app → NOVA goes behind it and never covers it.
6. Disconnect the secondary monitor while NOVA is there (or change resolution) → NOVA reappears on a visible monitor.

### Test E — Simple automatic detection
1. Menu → Detection rules → add `Code.exe` + title contains `MyProject` → workspace "Dev". Save.
2. Focus a VS Code window with MyProject → NOVA switches to "Dev" (without taking focus).
3. Focus Notepad → nothing changes.
4. Manually select "ComfyUI" while VS Code was last active → the "remember?" bar appears; choose *Just this time*.
   Returning to the same VS Code window does not switch back; focusing another app and then VS Code does.
5. Lock (padlock) → no automatic switching at all.

### Test F — Reliability
1. Pin a folder on a USB drive; remove the drive → pin shows "unavailable", clicking it shows a status message, UI responsive.
   Reconnect → works again; the pin was never removed.
2. Pin a folder on an unreachable network share → status within ~3 s, no freeze.
3. Open a folder without access rights → status message, no crash.
4. Copy conflicts → native conflict dialog (Test A).
5. Kill the process (Task Manager) during use → restart → state from at most ~1 s earlier.
6. Corrupt `workspaces.json` by hand (truncate it) → restart → status says it was recovered from backup;
   a `workspaces.json.corrupt-*` copy exists.

### Accessibility / UX spot checks
* Tab through sidebar → browser; all toolbar buttons have tooltips and announce names in Narrator.
* Dark and light Windows mode both readable (Settings → Theme). 100 %, 150 %, 200 % scaling.
* A folder with 10,000+ files scrolls smoothly (native view).
