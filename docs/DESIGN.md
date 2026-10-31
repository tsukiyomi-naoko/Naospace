# Naospace: Design Doc (v0, pre-development)

> **Workspaces for your file system.** Each project gets its own folders, tools and links,
> all in one small panel. You can pin it, keep it on the desktop or call it up with a hotkey,
> so you don't end up with 40 Explorer windows open.

---

## 1. The problem (what we're actually fixing)

| Pain | Root cause |
|---|---|
| Lots of open Explorer windows | Each "go to folder" opens a **new** window, and nothing ever closes them |
| Closing and re-opening the same folders every day | Explorer doesn't remember a *set* of folders per project |
| Quick Access is fine for daily use but bad for projects | It's **one flat global list**. It can't hold a "ComfyUI context" separate from a "Mozaik context" |
| Hours spent browsing/cleaning at the end of the week | Files land in Downloads with no route to where they belong (e.g. `.safetensors` → `models/loras`) |
| Switching projects is slow | Folders, tools, scripts and URLs for one project live in five different places |

**Goal:** opening any project location takes **one keystroke and one click**, never adds a new window, and moving files to their right place takes **one drag**.

---

## 2. Core concepts

### 2.1 Workspace
A named, saved set of **items** for one project/context. Examples: `ComfyUI`, `Mozaik`, `VS Code`, `GPTs Live`.

A workspace has:
- **Items**, grouped into optional **sections** (e.g. "Models", "I/O", "Tools")
- **Variables** (`${COMFY}` = `D:\AI\ComfyUI`) so paths stay short and portable
- A **color + icon** so you can tell at a glance which one is active
- Optional **launch set**: what "Open everything" starts (server .bat, browser tab, VS Code folder…)
- Optional **auto-activate rule**: e.g. switch to `VS Code` when `Code.exe` is in the foreground

### 2.2 Item types
| Type | Click | Extra |
|---|---|---|
| **Folder** | Open in the *reused* Explorer window (see §4) | Peek (browse inside the panel), **drop target** for move/copy |
| **File** | Open with default app | "Reveal in folder" |
| **App / Tool** | Launch exe/.bat/.lnk with args + working dir | "Run as admin", "single instance" (focus if already running) |
| **URL** | Open in browser | e.g. `http://127.0.0.1:8188` for ComfyUI |
| **Command** | Run a shell command (git pull, venv activate, pip update…) | Show output in a toast or a mini console |
| **Live folder** | Shows the **N most recent files** of a folder (e.g. Downloads) | Drag them straight onto other folder tiles |

### 2.3 The Dock (the overlay panel)
One compact, keyboard-first window with **four display modes**, switchable at any time:

| Mode | Behavior | When to use |
|---|---|---|
| **Pinned** | Always on top, small, semi-transparent when not hovered | Heavy work sessions (ComfyUI model shuffling) |
| **Desktop** | Lives *on* the desktop, behind all windows, like a widget/Fences | Ambient access, "my desk" |
| **Summoned** (default) | Hidden; a **global hotkey** drops it in front at the mouse or screen edge; `Esc` or losing focus hides it | Everyday use |
| **Edge-docked** | Collapses to a thin strip on a screen edge, expands on hover | Multi-monitor setups |

---

## 3. Interaction design

### Global hotkeys (configurable)
| Hotkey | Action |
|---|---|
| `Win+Alt+Space` (suggested) | Toggle the Dock (summon/hide) |
| `Win+Alt+P` | Toggle Pinned mode |
| `Win+Alt+1..9` | Jump straight to workspace N (summons Dock) |

### Inside the Dock
- **Type to filter**: start typing to fuzzy-filter items in the current workspace, `Enter` opens. `Tab` searches *all* workspaces.
- `Ctrl+1..9` switch workspace, `Ctrl+Tab` cycle.
- `Space` on a folder = **Peek** (inline file list, navigate with arrows, drag out, `Enter` opens in Explorer).
- Right-click item: Open / Open in new tab / Copy path / Open terminal here / Open in VS Code / Edit / Remove.
- **Drag & drop**:
  - Drop **files** onto a folder tile: **move** (same drive) or **copy** (other drive). Hold `Ctrl` = copy, `Shift` = move (same as Explorer).
  - Drop a **folder/exe/shortcut** onto empty space: **add it as an item** to the current workspace.
  - Drag a tile out (to an app, a browser upload field…): drags the real path.

### Sketch
```
┌─ Naospace ───────────────── ComfyUI ▾ ── 📌 ─ ✕ ┐
│ 🔍 filter…                                       │
│ ── Server ─────────────────────────────────────  │
│ ▶ Start ComfyUI     🌐 127.0.0.1:8188   ⟳ Update │
│ ── Models ─────────────────────────────────────  │
│ 📁 checkpoints  📁 loras  📁 vae  📁 controlnet  │
│ 📁 upscale      📁 embeddings                    │
│ ── I/O ────────────────────────────────────────  │
│ 📁 input   📁 output   📁 custom_nodes  📁 root  │
│ ── Downloads (live) ───────────────────────────  │
│ 📄 flux_style_v2.safetensors     2 min  → loras? │
│ 📄 workflow_upscale.json        10 min           │
│ ── Tools ──────────────────────────────────────  │
│ 🛠 ComfyUI-Manager  🛠 Civitai Helper  🛠 VS Code │
└──────────────────────────────────────────────────┘
```

---

## 4. The key feature: stop the window sprawl

This is what changes things, more than any shortcut does.

1. **Explorer window reuse.** Naospace keeps track of **one Explorer window per workspace** (via the
   `Shell.Application` → `ShellWindows` COM API). Clicking a folder **navigates that window** instead
   of opening a new one. On Windows 11 we can optionally open it as a **new tab** in that window
   instead (best effort, since Explorer has no official "new tab" API).
2. **"Close workspace" button.** Closes every Explorer window/tab whose path sits under the workspace's
   folders. Leaving a project for the day closes its windows in one click.
3. **Snapshot / Restore.** "Capture" grabs every currently open Explorer window (and optionally
   running tools) and saves them as a workspace or a launch set. "Restore" reopens exactly that.
   That turns "I have 12 windows open for this project" into a reusable workspace in one click.
4. **Global "Tidy" command.** Close all Explorer windows that point to the same folder (duplicates)
   or that haven't been focused in X hours.

---

## 5. Fixing the end-of-week cleanup

The real fix for "hours browsing my PC" is that files should **never pile up in the wrong place**.

- **Live Downloads view** in every workspace (or a dedicated `Inbox` workspace).
- **Routing rules** (per workspace), suggestions only at first, never automatic moves unless you opt in:
  ```
  *.safetensors  where name contains "lora"  →  ${COMFY}\models\loras
  *.safetensors / *.ckpt                      →  ${COMFY}\models\checkpoints
  *.json         where it's a ComfyUI workflow →  ${COMFY}\user\default\workflows
  ```
  A matching file shows a `→ loras?` chip; click it to move it.
- **Inbox review** (`Win+Alt+I`): a list of everything in Downloads/Desktop older than N days with
  one-click actions *Move to… / Archive / Delete*. Five minutes instead of an afternoon.
- Later: optional per-workspace **"stale" report**, i.e. big files in output folders you haven't touched in 30 days.

---

## 6. Configuration

Plain JSON in `%APPDATA%\Naospace\workspaces.json` (editable by hand, synced via OneDrive/git if
you want), plus an in-app editor so you rarely need to touch the file.

```jsonc
{
  "hotkeys": { "toggle": "Win+Alt+Space", "pin": "Win+Alt+P", "inbox": "Win+Alt+I" },
  "dock":    { "mode": "summoned", "opacity": 0.92, "position": "mouse" },
  "explorer": { "reuseWindow": true, "preferTabs": true },

  "workspaces": [
    {
      "id": "comfy",
      "name": "ComfyUI",
      "color": "#7C5CFF",
      "vars": { "COMFY": "D:\\AI\\ComfyUI_windows_portable\\ComfyUI" },
      "autoActivate": { "processes": ["python.exe"], "windowTitleContains": ["ComfyUI"] },
      "sections": [
        { "name": "Server", "items": [
          { "type": "app",  "name": "Start ComfyUI", "path": "${COMFY}\\..\\run_nvidia_gpu.bat", "singleInstance": true },
          { "type": "url",  "name": "UI", "url": "http://127.0.0.1:8188" },
          { "type": "command", "name": "Update", "cmd": "git pull", "cwd": "${COMFY}" }
        ]},
        { "name": "Models", "items": [
          { "type": "folder", "path": "${COMFY}\\models\\checkpoints" },
          { "type": "folder", "path": "${COMFY}\\models\\loras" },
          { "type": "folder", "path": "${COMFY}\\models\\vae" }
        ]},
        { "name": "I/O", "items": [
          { "type": "folder", "path": "${COMFY}\\input" },
          { "type": "folder", "path": "${COMFY}\\output" },
          { "type": "folder", "path": "${COMFY}\\custom_nodes" }
        ]},
        { "name": "Downloads", "items": [
          { "type": "live", "path": "%USERPROFILE%\\Downloads", "count": 6 }
        ]}
      ],
      "routing": [
        { "match": "*.safetensors", "nameContains": "lora", "to": "${COMFY}\\models\\loras" },
        { "match": ["*.safetensors", "*.ckpt"], "to": "${COMFY}\\models\\checkpoints" }
      ],
      "launchSet": ["Start ComfyUI", "UI"]
    },
    { "id": "mozaik",   "name": "Mozaik",    "color": "#FF8A3D", "sections": [] },
    { "id": "vscode",   "name": "VS Code",   "color": "#2F80ED", "sections": [] },
    { "id": "gpts",     "name": "GPTs Live", "color": "#22C55E", "sections": [] }
  ]
}
```

---

## 7. Tech stack recommendation

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **C# / .NET 8 + WPF** | Native Win32/COM access (ShellWindows, `RegisterHotKey`, `SetWindowPos`, shell drag-drop, `IFileOperation` for real Explorer-style copy with progress/undo), single-exe publish, mature | UI looks dated unless styled (fixed with WPF-UI / Fluent theme) | ✅ **Recommended** |
| WinUI 3 | Modern Fluent look | Painful packaging, weaker drag-drop/topmost/transparency control | Possible later |
| Tauri (Rust + web UI) | Pretty UI, small binary | Every shell feature needs Rust↔Win32 plumbing | Not worth it here |
| Electron | Easy UI | 150 MB+ RAM for a launcher, weak shell integration | ❌ |
| AutoHotkey v2 | Prototype in an evening | Hard to grow into drag-drop, peek, routing, editor | Good for a throwaway spike only |

**Proposed libraries:** `WPF-UI` (Fluent styling), `H.NotifyIcon.Wpf` (tray), `System.Text.Json`,
`Microsoft.WindowsAPICodePack`/`Vanara` (shell icons, `IFileOperation`, COM shell windows).
Optional later: **Everything SDK** for instant search across all workspace folders.

**Architecture sketch**
```
Naospace.App        WPF UI: Dock window, editor, tray, themes
Naospace.Core       Workspace model, config load/save, variables, routing rules (no UI, unit-testable)
Naospace.Shell      Win32/COM: hotkeys, window modes, Explorer reuse/tabs, file ops, icons, folder watching
```

---

## 8. Roadmap

### v0.1: MVP (usable daily)
- [ ] Tray app, auto-start with Windows
- [ ] JSON config with variables, multiple workspaces, workspace switcher
- [ ] Item types: folder, file, app, URL
- [ ] Global hotkey toggle (Summoned mode) and Pinned (topmost) mode
- [ ] Type-to-filter and keyboard navigation
- [ ] **Explorer window reuse** (navigate an existing window instead of opening new ones)
- [ ] Drop files onto folder tiles (move/copy)
- [ ] Drop a folder onto the Dock to add it to the workspace

### v0.2: Kill the sprawl
- [ ] Close workspace (close its Explorer windows), Tidy duplicates
- [ ] Snapshot / Restore open Explorer windows
- [ ] Peek (browse folder contents inside the Dock)
- [ ] Live folder item (recent Downloads)
- [ ] Desktop and Edge-docked modes

### v0.3: Flow
- [ ] Routing rules and the Inbox review screen
- [ ] In-app workspace editor (drag to reorder, sections, icons, colors)
- [ ] Command items and launch sets ("Open everything")
- [ ] Auto-activate workspace from the foreground app
- [ ] Explorer right-click → "Add to Naospace workspace…"

### Later / ideas
- Per-workspace notes/TODO scratchpad
- Everything-powered search scoped to the workspace
- Per-workspace "recent files" (files you opened via Naospace)
- Multi-monitor: a different default workspace per monitor
- Import from Quick Access / Explorer pinned folders

---

## 9. Existing tools (for reference, so we build what's missing)
- **PowerToys Workspaces**: restores app *window layouts*, not folder sets; no overlay.
- **Stardock Fences** "folder portals": desktop-only, no hotkey summon, no workspaces per project.
- **Directory Opus / XYplorer / Files**: replace Explorer with tabs/layouts, but they're full file managers, not a lightweight project switcher.
- **Listary / Everything**: great search, but no curated per-project sets.

Naospace sits between them: **a small, keyboard-first project switcher that works *with* Explorer instead of replacing it.**

---

## 10. Open questions
1. Windows 10 or 11? (Affects Explorer tabs and the Mica/Fluent look.)
2. Preferred toggle hotkey? (`Win+Alt+Space`, `` Ctrl+` ``, a mouse button…)
3. One Dock showing one workspace at a time, or a separate Dock per workspace pinned side by side?
4. Should routing rules ever move files automatically, or always ask?
5. Is it OK to store the config in `%APPDATA%`, or keep it in a synced folder (OneDrive/git)?
