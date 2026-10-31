# Naospace: Vision & Feature System

> **A context engine for your PC.** Naospace knows which project you're working on, keeps everything
> that belongs to it within reach, remembers where you left off, and keeps files flowing to the
> right place, so the PC never turns into something you have to dig through.

This doc is the *what & why*. [DESIGN.md](DESIGN.md) covers the detailed UI of the three display modes and the Shelf.

---

## 0. Why "PowerToys Workspaces" isn't enough

Launchers and layout restorers solve **one moment**: opening things. Real friction is spread across the whole day:

| Moment | Friction today | Naospace answer |
|---|---|---|
| **Start** | Re-open 10 folders, 3 tools, the server, the browser tab | **Resume** a workspace: everything comes back, as you left it |
| **Work** | Hunt for a folder, alt-tab through 20 windows, lose the reference image | **Reach**: every project thing ≤ 1 keystroke + 1 click, from any app |
| **Move files** | Downloads → models/loras by hand, five Explorer windows deep | **Flow**: drop targets, routing rules, a stash |
| **Switch project** | Close things or let them pile up | **Park** the current workspace, resume another |
| **Come back after a week** | "What was I doing? Where did I put that?" | **Memory**: resume card, journal, project notes |
| **Friday** | Hours of browsing and sorting | **Hygiene**: 5-minute guided review |

The product is the **whole loop**, not any single panel.

---

## 1. Design principles

1. **Reach over search.** The things you use daily should be *visible or one gesture away*. Search is the fallback, not the main tool.
2. **Spatial stability.** Pinned items **never move by themselves**. Muscle memory is the fastest kind of access.
   Anything dynamic (recents, suggestions, live Downloads) lives in clearly separate zones.
3. **Never spawn, always reuse.** No action creates a new Explorer window if one for that workspace exists.
4. **Everything reachable three ways.** Each item can be reached by **mouse**, **keyboard** and **hotkey/hardware** (Stream Deck, mouse button). No feature is mouse-only or keyboard-only.
5. **Suggest, don't surprise.** Automation proposes (`→ loras?`), and you confirm. Auto-actions are opt-in, per rule, and always undoable.
6. **Works *with* Windows.** It doesn't replace Explorer, the taskbar or VS Code. It makes them answer to the current project.
7. **Zero-friction capture.** Adding something to a workspace takes one drag, one paste or one right-click. If pinning is slow, people stop doing it.
8. **Local and private.** Activity history stays on the machine, can be turned off per workspace, and can be cleared with one click.
9. **Calm by default.** No notifications unless asked; no animation you can't turn off; no timers that punish being slow.

---

## 2. Mental model

```
             ┌──────────────────── Workspace ─────────────────────┐
             │  Roots        folders the project "owns" (anything │
             │               under them belongs to it)            │
             │  Pins         curated items: folders, files, apps, │
             │               links, commands, groups              │
             │  Shelf        references: images, notes, snippets  │
             │  Session      open Explorer tabs, running tools,   │
             │               window layout                        │
             │  Memory       journal, recent files, resume note   │
             │  Rules        routing, triggers, auto-activation   │
             └────────────────────────────────────────────────────┘
```

**Context stack:** what's shown at any moment is **Global** (always: Downloads, Desktop, Screenshots, everyday tools)
+ **Active workspace** + optionally **Pinned peers** (a second workspace you keep at hand, e.g. *GPTs Live* next to *ComfyUI*).

**Roots vs. pins** is the key idea: pins are what *you* curate, while roots let Naospace *understand* what belongs
to the project (for recent files, search scope, routing, cleanup, auto-detection) without you pinning every subfolder.

---

## 3. Surfaces: three distances

Every pin, command and Shelf item is available on every surface. You choose the distance that fits the moment.

| Distance | Surface | Gesture | For |
|---|---|---|---|
| **Glance** (0 actions) | **Sidebar rail** on a chosen monitor edge (reserved space) | none, always visible | "where am I, what's running" |
| | **Desktop board** | `Win+D` or hotkey | overview, references, project wall |
| **Reach** (1 action) | **Sidebar expanded** | hover / click edge | daily navigation |
| | **Floating overlay** | `Win+Alt+Space` | while in a full-screen-ish app |
| | **Radial menu** at cursor | hold a mouse button (e.g. middle / side button) or pen button | mouse/pen-first, no travel to the edge |
| **Command** (type) | **Palette** | `Win+Alt+Space` then type, or `Win+Alt+K` | anything by name, actions, all workspaces |
| **In place** | **Explorer** right-click: *Pin to…*, *Send to workspace folder ▸*, *Open workspace* | right-click | capture & move without leaving Explorer |
| | **File dialogs** (Open/Save/Upload) | sidebar/overlay/radial click | jump the dialog to a workspace folder |
| | **Tray** | click | switch workspace, park/resume |
| **External** | **CLI / URI / Stream Deck / Command Palette ext.** | `naospace://open/comfy/loras` | hardware buttons, scripts, AutoHotkey |

The **radial menu** matters for accessibility: it opens *where your hand already is*, has 8 big targets (Fitts's law),
and can be learned by direction ("up-right = loras").

---

## 4. Feature pillars

Priority tags: **P0** = needed in the first daily-usable build · **P1** = makes it great · **P2** = power features.

### A. Context engine: *knowing which project you're in*
- **P0** Manual switch: sidebar rail icons, `Win+Alt+1..9`, palette, tray.
- **P1** **Auto-activate** from the foreground window: process name, window title, **path** (an Explorer tab or VS Code folder under a workspace root), browser URL host (via title). Shown as a suggestion ("Switch to ComfyUI?") or automatic, per workspace.
- **P1** **Virtual desktop binding** (Windows 11): workspace ↔ virtual desktop. Switching one switches the other, and the rail shows the desktop name.
- **P2** Per-monitor context: left monitor shows ComfyUI, right shows VS Code.
- Active workspace shown everywhere: rail color strip, overlay header, tray icon tint, optional thin colored border on the active Explorer window.

### B. Reach: *pins that are always there*
- **P0** Pin types: folder, file, app/tool, URL, command, group, live folder (DESIGN §2.2).
- **P0** Sections (collapsible), manual order, icons, short aliases for typing (`lo` → loras).
- **P0** Capture by drag, paste (`Ctrl+V` path/URL/image) and Explorer right-click *Pin to Naospace ▸ workspace*.
- **P1** **Smart pins**:
  - *Folder with badge*: shows file count / newest-file age ("loras · 3 new").
  - *Tool with status*: running ● / stopped ○, and **port check** (ComfyUI `:8188` up). Click = start or focus; right-click = stop/restart.
  - *Latest file in folder*: "last render" opens or drags the newest file in `output`.
  - *Git repo*: branch + dirty state, open in VS Code / terminal.
- **P1** **Global workspace** always shown in a fixed zone (Downloads, Desktop, Screenshots, everyday tools).
- **P2** Item **variants**: one pin with alternatives (open in Explorer / VS Code / terminal / copy path) shown on hover or via modifier keys.

### C. Sessions: *park and resume instead of close and re-open*
- **P0** **One Explorer window per workspace**, folders open as tabs (DESIGN §5).
- **P1** **Snapshot**: capture open Explorer tabs + running tools + (optionally) window positions per monitor.
- **P1** **Park workspace**: save the session, then close its Explorer tabs and (optionally) its tools. Your desk is clean, nothing is lost.
- **P1** **Resume workspace**: restore the parked session (tabs, tools, layout), then show the **resume card** (see F).
- **P1** **Focus**: minimize every window that doesn't belong to the active workspace; restore with the same key.
- **P2** Layout presets per workspace: "ComfyUI left 2/3, Explorer right 1/3, Photoshop on monitor 2".
- **P2** Close *duplicates* and *stale* tabs (not touched in N hours) with **Tidy**.

### D. File flow: *files go where they belong*
- **P0** **Drop targets**: drop on a folder pin to move or copy, using Explorer's own file operations (progress, conflicts, **undo**).
- **P1** **Stash** (a temporary area in the Shelf): drag files in from anywhere, switch workspace or app, drag them out. Like a second clipboard you can see.
- **P1** **Live Downloads** with **routing suggestions** (`→ loras?`), one click to move.
- **P1** **Send to ▸** in Explorer right-click: lists the active workspace's folder pins.
- **P1** Copy path variants: `C:\a\b`, `C:/a/b`, `"C:\a\b"`, relative to workspace root, `file:///`.
- **P2** Rules engine: *when* a file appears in X and matches Y *then* suggest / move / rename / tag. Every auto-move is logged and undoable.
- **P2** Quick actions on files: open with workspace tool, convert/resize image, extract archive here, compute hash.

### E. Find: *when it's not pinned*
- **P0** Palette: fuzzy search across pins, sections, commands, workspaces.
- **P1** **Workspace-scoped file search** under the roots (instant via **Everything** if installed, otherwise our own index).
- **P1** **Recent in this workspace**: files opened, created or downloaded under its roots, newest first.
- **P2** **Tags** stored in Naospace (not in the files), e.g. `#favorite`, `#client-A`, `#wip`.
- **P2** **Smart collections** (saved searches shown as pins): "safetensors > 1 GB", "renders this week", "files tagged #wip".

### F. Memory: *remember what you were doing*
- **P1** **Resume card**: when you activate a workspace after a while, show *last active date, last files touched,
  your parked note, open TODOs*. One glance and you're back in.
- **P1** **Park note**: when parking, an optional one-line prompt: "where did I stop?"
- **P1** **Shelf**: reference images (pin on screen), snippets (prompts, args), notes/TODOs, swatches (DESIGN §4).
- **P2** **Journal / timeline** per workspace: an automatic log of files created/opened/downloaded/routed, tools started, notes. Searchable ("what did I do on the Mozaik project last Tuesday").
- **P2** Per-workspace **clipboard history**.

### G. Automation: *repetitive steps become one click*
- **P1** **Command pins** with parameters (`git checkout {branch}` asks for a branch).
- **P1** **Launch set**: "Start ComfyUI + open UI tab + open models group".
- **P2** **Hooks**: on *activate* (start server), on *park* (stop server, commit WIP), on *file arrives* (route).
- **P2** **Scripts** (PowerShell/Python) with output in a mini console or toast.

### H. Hygiene: *the Friday problem*
- **P1** **Weekly review** (`Win+Alt+I`), a 5-minute guided pass:
  1. Inbox: Downloads/Desktop older than N days → Move / Archive / Delete
  2. Stale: large files untouched for 30 days in output folders
  3. Duplicates: same file (hash) in two places, e.g. the same LoRA in two model folders
  4. Notes & TODOs: tick off, carry over
- **P1** **Workspace health**: broken paths, missing tools, roots on a disconnected drive (shown as a dimmed pin, not an error popup).
- **P2** Disk usage per root (treemap on the board), "ComfyUI models = 312 GB".

### I. Setup: *a new workspace in under a minute*
- **P0** Create a workspace from a folder (drag a folder onto the rail's ➕).
- **P1** **Detectors / templates** that recognise project types and fill in sections automatically:
  - *ComfyUI root* → models/* folders, custom_nodes, input/output, workflows, run_*.bat, `127.0.0.1:8188`
  - *Git / VS Code project* → repo root, `.code-workspace`, terminal, remote URL, README
  - *Node / Python* → venv activate, `npm run` scripts as commands
  - *Generic* → top-level folders as suggestions
- **P1** **Snapshot → workspace**: turn what's open right now into a new workspace.
- **P2** Import from Quick Access pins and Quick Access Popup; export/share a workspace as a file (paths turned into variables).

---

## 5. Accessibility (in the strict sense)

Naospace is about reachability, but it must also be accessible in the formal sense:

| Area | Requirement |
|---|---|
| **Keyboard** | Every feature works without a mouse. Visible focus ring. Shortcuts rebindable. Tab order follows visual order. |
| **Screen readers** | Full UI Automation support (names, roles, states) for Narrator/NVDA. Item announcements include section and position ("loras, folder, 2 of 6, Models"). |
| **Pointer** | Big targets (min 32 px, adjustable). Screen-edge rail = infinite-depth target. Radial menu. Configurable hover delay; option for **click-only** (no hover-open). No drag required: every drag action also exists as a menu action. |
| **Vision** | Follows Windows text scaling and **High Contrast** themes. Workspace identity is **color + icon + letter**, never color alone. Light/dark/Mica. |
| **Motion** | Respects Windows "animation effects off". No flashing. |
| **Cognitive load** | Stable layout, consistent placement across modes, undo for every destructive action, no timeouts. Plain-language labels. |
| **Input devices** | Mouse extra buttons, pen buttons, Stream Deck, and anything that can send a hotkey or open a `naospace://` URI. |

---

## 6. Key flows (how a day should feel)

**Monday 9:00, resume ComfyUI**
1. Rail shows *ComfyUI* parked since Friday. Click it.
2. Explorer opens one window with 4 tabs (checkpoints, loras, input, output), ComfyUI server starts, the browser tab opens.
3. Resume card: "Fri 18:40 · last files: upscale_v3.json, render_0142.png · note: *try VAE fix on upscale* · 2 TODOs".

**11:00, new LoRA downloaded**
1. The rail's Downloads badge pulses once (or doesn't, if calm mode is on). Expand: `flux_style_v2.safetensors → loras?`
2. Click the chip. Moved, undoable. The loras pin shows "1 new".

**14:00, client call needs Mozaik**
1. `Win+Alt+2`, then *Park ComfyUI?* → yes, note "halfway through batch 3".
2. ComfyUI's Explorer tabs close (server keeps running if you set it to). Mozaik's session restores on the same screen.

**15:00, need a reference image while in Mozaik**
1. Hold the mouse side button, radial menu, *Shelf*, click image, and it's pinned on screen at 60 % opacity, click-through.

**Friday 17:30, review**
1. `Win+Alt+I`: 14 items in Downloads → 9 routed, 3 archived, 2 deleted; 2 duplicate checkpoints found (6.4 GB); TODOs carried over. **Done in 5 minutes.**

---

## 7. Architecture implications

```
┌──────────────────────── Naospace.Service (background, tray) ────────────────────────┐
│ Context engine │ Session manager │ File-flow & rules │ Index/Search │ Journal │ Health │
│                       ▲ event bus (window focus, file events, process events)        │
│ Shell layer: hotkeys · AppBar · desktop layer · ShellWindows/tabs · file dialogs ·   │
│              IFileOperation · FileSystemWatcher · WinEvent hooks · virtual desktops   │
│ Storage: workspaces.json (config, human-editable) · naospace.db (SQLite: journal,     │
│          recents, tags, hashes, sessions)                                             │
└───────────────▲───────────────────────────────▲───────────────────────────▲──────────┘
                │ in-process                    │ named pipe IPC            │
     Surfaces: Sidebar · Overlay · Board ·      naospace.exe CLI ·          Command Palette ext ·
     Palette · Radial · Shelf · Image pins      naospace:// URI             Stream Deck plugin
```

- **Providers** (plugin-style interfaces) so features stay independent: `IItemProvider` (pin types), `IDetector`
  (templates), `IAction` (verbs on items), `ISearchProvider` (Everything / own index), `ITrigger` (rules).
- **One command model**: every action (open, park, route, pin…) is a named command with an ID. Menus, palette, hotkeys,
  radial, CLI and URI all call the same commands, so "reachable three ways" comes for free and is consistent.
- **Undo journal**: file moves/renames and park/close actions record inverse operations.
- **Performance**: model folders can be huge. Watchers are shallow by default; hashing for duplicates is lazy, done in the
  background and cached by (path, size, mtime).

---

## 8. Milestones

| Milestone | Theme | Contents | You can… |
|---|---|---|---|
| **M1 Reach** | stop the hunting | Service + tray, config, workspaces, pins (P0 types), **sidebar** (rail + expand, reserved space), floating overlay + hotkey, palette, Explorer tab reuse, drop targets, create-from-folder | open any project folder/tool in 1 action without new windows |
| **M2 Sessions** | stop the closing/re-opening | Snapshot, **park/resume**, park note, resume card, focus mode, global workspace, smart pins (status, badges) | switch projects in seconds and come back to exactly where you were |
| **M3 Flow** | stop the sorting | Shelf + stash + image pins, live Downloads, routing suggestions, Send to ▸, Explorer *Pin to*, file-dialog jump, copy-path variants | move files to the right place with one drag/click |
| **M4 Memory & find** | stop forgetting | scoped search (Everything), recents, auto-activate, virtual desktop binding, desktop board, radial menu, detectors/templates | find anything in a project and let Naospace follow you |
| **M5 Power** | stop repeating | weekly review, health, duplicates, journal, rules engine + hooks, commands with params, CLI/URI, Command Palette ext, Stream Deck | the Friday cleanup becomes a 5-minute routine |

M1 alone should already replace Quick Access for project work; each later milestone removes another chunk of friction.

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| No official API to open Explorer **tabs** | Best-effort (`Ctrl+T` + navigate) with fallback to navigating the window; isolate in one class, test on each Windows build |
| **Virtual desktop** switching APIs are partly undocumented and change between builds | Make it optional (M4), feature-flag it, degrade gracefully |
| AppBar quirks with mixed DPI / monitor hot-plug | Re-register on `WM_DISPLAYCHANGE` / DPI change; store monitor by device ID |
| Feature creep | Strict milestones; each must be daily-usable on its own |
| Activity tracking feels creepy | Local-only, per-workspace opt-in, visible "pause history", one-click clear |
| Huge folders (models, outputs) | Shallow watchers, lazy hashing, Everything when available |

---

## 10. Open questions
1. Which mouse button / pen button should open the **radial menu**? (Middle-hold, a side button, none?)
2. **Park** default: close only Explorer tabs, or also stop tools (ComfyUI server)? (Per workspace setting, but which default?)
3. Do you use **Windows virtual desktops** today? If yes, binding workspaces to them could be very powerful.
4. Do you have a **Stream Deck** or macro keys? (Changes how early CLI/URI matters.)
5. Is **Everything** (voidtools) installed? It makes scoped search instant.
6. Sidebar expand default: overlay (windows never move) or push (windows resize)?
