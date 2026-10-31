# NOVA Workspace — Roadmap

The original seven-stage plan was reordered. Persistence, desktop integration and basic detection
landed in the foundation because they shape the architecture; postponing them would have meant
rework.

## ✅ v0.1 — Foundation (this checkpoint)

**Workspaces**
- Unlimited workspaces with color, description and **path variables** (`${COMFY}`).
- Pin tree: nested groups; pins for folders, files, apps, scripts (`.ps1`/`.py`/`.bat`), URLs
  and commands; arguments, start-in folder, run as admin.
- Pin by drag (from NOVA or Explorer), `Ctrl+D`, or the menus. Reorder by drag or `Ctrl+↑/↓`,
  move between groups.
- Per-session **working set** (temporary locations) with promote-to-pin.
- Per-workspace **recent folders**.
- Export/import single workspaces (`.novaws`), with fresh IDs and session remapping.

**Integrated file browser**
- Tabs (`Ctrl+T/W/Tab`, middle-click), **dual pane** (`Ctrl+\`, `F6`), breadcrumbs plus an
  editable address bar (`Ctrl+L`, which accepts `${VAR}` and `%ENV%`).
- Back, forward and up, including the mouse X-buttons; going Up selects the folder you came from.
- Natural sort, folders first, column sort; live filter (`Ctrl+F`: text, `*.png`, `re:`).
- Live refresh via FileSystemWatcher; shell icons and type names.
- Copy, cut, paste, rename, new folder, delete (Recycle Bin), permanent delete, all through the
  Windows copy engine.
- Drag and drop between panes, onto folders, breadcrumbs, tabs (hover to switch tab) and
  **pinned folders**.
- **Project-aware "Move to / Copy to"**: every folder pin, working-set entry and the other pane,
  one menu away. This is the Downloads → project workflow.
- Open with, Properties, terminal here, reveal in Explorer, open in Explorer (focuses an existing
  window).

**Desktop integration**
- Compact sidebar ↔ expanded browser (`Ctrl+B`), each with its own remembered geometry.
- Always on top, tray icon, hide on close, optional hide-on-focus-loss overlay mode.
- Global hotkeys: show/hide plus jump palette (`Ctrl+Alt+Space`), capture the active Explorer
  folder (`Ctrl+Alt+P`).
- AppBar docking left/right (experimental). Per-Monitor-V2 DPI. Single instance.

**Sessions & persistence**
- Named sessions per workspace: tabs, panes, history, working set and notes are restored on
  switch and on restart.
- New, duplicate, rename and delete sessions.
- Atomic autosave, rolling backups, corrupt-file quarantine and recovery, schema versioning,
  portable mode.

**Intelligence**
- `Ctrl+K` **quick jump**: fuzzy search over every pin, working location, recent folder, session
  and workspace across *all* workspaces, or paste a path.
- Foreground app detection (Off / Suggest / Automatic), workspace lock, rules editor, "add rule
  from last app", and learned rule suggestions.

## Next — v0.2 "Daily driver" (proposed priority order)

1. **Hands-on Windows validation pass.** Run the checklist in the README, then fix what real use
   reveals. *Highest priority: nothing else matters if the basics feel off.*
2. **Native "Show more options" context menu** (`IContextMenu`): 7-Zip, Git and other shell
   extensions. Additive; likely via Vanara.
3. **Inline rename** in the list (F2 edits in place) instead of a dialog.
4. **Preview pane** (`Alt+P`): image thumbnails via `IShellItemImageFactory`, text and code
   preview, shell preview handlers (PDF, Office) via `IPreviewHandler`. *ComfyUI outputs are
   mostly images, so this has a high payoff.*
5. **Thumbnail / grid view** for image-heavy folders (outputs, LoRA preview images).
6. **Search**: recursive search in the current workspace's roots, using the Everything SDK when
   available, otherwise Windows Search or a background walker.
7. **Windows 11 Explorer tabs**: resolve the *active* tab for capture and window reuse.

## Later — ideas worth discussing

Each idea lists the problem it solves, the architectural impact and where it fits.

| Idea | Problem it solves | Architecture impact | Fit |
|---|---|---|---|
| **Inbox / Downloads triage** | New downloads pile up; moving them into the right project folder is the most repeated chore | Small: a watched-folder section in the sidebar plus the existing Move-to targets; later, per-workspace suggestions ("`.safetensors` → `${COMFY}\models\loras`") | v0.3 |
| **Project activity timeline** | "What did I change yesterday in this project?" | Medium: a per-workspace journal of navigation and file operations (Core) | v0.3 |
| **Snapshots / checkpoints** | Freeze a session before a risky reorganisation, roll back the *layout* | Small: sessions are already cloneable; add named, timestamped clones | v0.2–0.3 |
| **Session templates** | New project, same shape (e.g. "Mozaik job") | Small: a workspace or session with variables as a template; create from it with prompts for variable values | v0.3 |
| **Workspace "launch"** | Start the whole toolset at once (app + folders + URL) | Small: "launch group" action on a pin group | v0.2 |
| **Command palette verbs** | `Ctrl+K` currently *goes* places; add *actions* ("new session", "toggle dual", "move selection to…") | Small: add a command source to QuickJump | v0.2 |
| **Explorer context-menu "Add to NOVA"** | Capture from Explorer without a hotkey | Medium: a registered shell verb that calls NOVA over the single-instance channel (switch from an event to a named pipe) | v0.3 |
| **Folder size / disk usage view** | Model folders get huge | Medium: background scanner with caching | Later |
| **Visual folder map** | See a project's structure at a glance | Medium–large | Later |
| **Auto-hide docked bar** | Reclaim the screen edge | Medium: AppBar auto-hide registration and reveal animation | Later |
| **Sync via a user-chosen folder** | Same workspaces on two PCs, no accounts | Small: portable mode already enables it; add merge-on-conflict | Later |

## Explicit non-goals (for now)

- Cloud accounts, subscriptions, telemetry.
- Replacing Explorer system-wide (no default folder handler hijacking).
- Elaborate ML for detection. Simple, predictable rules plus suggestions are enough.
