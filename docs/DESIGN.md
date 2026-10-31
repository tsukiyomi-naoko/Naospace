# Naospace: Design Doc (v0.2, pre-development)

> **Everything a project needs, one keystroke away.** Each project gets a workspace with its folders,
> tools, links and reference images. You can keep it docked to the screen edge, floating on top
> or on the desktop, and stop closing, sorting and reopening Explorer windows.

> See [VISION.md](VISION.md) for principles, the full feature system and milestones. This doc details the UI.

**Target platform:** Windows 11 (uses Explorer tabs, Mica/Fluent styling, Win11 snap behavior).

---

## 1. The problem

| Pain | Root cause |
|---|---|
| Lots of open Explorer windows | Each "go to folder" opens a **new** window, and nothing ever closes them |
| Closing and re-opening the same folders every day | Explorer doesn't remember a *set* of folders per project |
| Quick Access is fine for daily use but bad for projects | It's **one flat global list**, with no "Mozaik context" separate from a "VS Code" or "ComfyUI context" |
| Hours spent browsing/cleaning at the end of the week | Files land in Downloads with no route to where they belong |
| Switching projects is slow | Folders, tools, scripts, URLs and reference images for one project live in five different places |

**Goal (accessibility first):** while you work on a project, *everything* related to it can be reached
in **one keystroke and one click**, without adding a new window and without leaving the current app.

---

## 2. Core concepts

### 2.1 Workspace
A named, saved context for one area of work: `Mozaik CNC`, `VS Code`, `ComfyUI`, `GPTs Live`, `Admin`… Workspaces are universal: nothing in the core is specific to any app (see [VISION §2.1](VISION.md#21-universal-by-design)).

- **Current project slot** (optional): one workspace, many jobs/repos/projects; `${JOB}` pins follow the selected one (see [VISION §2.2](VISION.md#22-workspaces-with-many-projects-inside-current-project))

- **Pinned items**, grouped into collapsible **sections** (Folders, Tools, Links, …)
- **Shelf**: the collapsible bottom drawer for reference pictures, notes and snippets (§4)
- **Variables** (`${MOZAIK}` = `D:\Mozaik`, `${JOB}` = current job folder) keep paths short and portable
- **Color + icon** so you can tell at a glance which one is active
- **Launch set**: what "Open everything" starts (server .bat, browser tab, VS Code folder…)
- **Auto-activate rule** (optional): switch to `VS Code` when `Code.exe` is in the foreground

### 2.2 What you can pin
Pin by **drag & drop** (from Explorer, the browser, the taskbar, the Start menu), by **paste**
(`Ctrl+V` of a path, URL or screenshot), from Explorer's **right-click → Pin to Naospace**, or by
promoting a suggestion from the workspace's "recently used" list.

| Type | Click | Extra |
|---|---|---|
| **Folder** | Opens in the workspace's *reused* Explorer window/tab (§5) | Peek (browse inside the panel), **drop target** for move/copy, open terminal / VS Code here |
| **File** | Opens with default app | Reveal in folder, open with… |
| **Software / Tool** | Launches exe/.lnk/.bat with args + working dir | **Single instance**: focuses it if already running; run as admin |
| **URL** | Opens in browser | e.g. supplier catalog, GitHub repo, `http://localhost:3000` |
| **Command** | Runs a shell command (git pull, venv, pip…) | Output in a toast or mini console |
| **Live folder** | Shows the **N newest files** of a folder (e.g. Downloads) | Drag them straight onto other folder tiles |
| **Group** | Opens several items at once (e.g. 4 folders as tabs in one Explorer window) | Same idea as Quick Access Popup groups |

---

## 3. Display modes

One workspace model, three ways to show it. You can switch at any time, and each workspace remembers
its preferred mode.

### 3.1 Dock mode (sidebar)
A **narrow, expandable sidebar that's always there** on the edge of a monitor you choose.

- **Monitor + edge**: choose which monitor (e.g. "Monitor 2") and which edge (left/right; top/bottom optional).
  The choice is stored by monitor ID, so it comes back to the same screen after a reboot or reconnecting a display.
- **Reserves desktop space**: it registers as a Windows **AppBar** (`SHAppBarMessage`), like the taskbar.
  Maximized and snapped windows stop at its edge instead of covering it.
- **Two widths:**
  - **Collapsed rail** (~48 px, the default): workspace color strip, workspace icons for switching, then the icons of pinned items. Tooltips show names.
  - **Expanded panel** (~280–360 px, adjustable): full labels, sections, filter box, Peek, live Downloads, and the Shelf at the bottom.
- **How it expands** (setting):
  - *Overlay expand* (default): only the rail's width is reserved, and the expanded panel slides **over** the window next to it. Your windows never resize or jump.
  - *Push expand*: the reserved space grows with the panel, so neighbouring windows resize. Useful on ultrawide screens where you keep it open all day.
- **Opens** on hover (with a short delay so you don't open it by accident), on click, or with the hotkey. **Closes** when the mouse leaves, on `Esc`, or when you click elsewhere. A 📌 keeps it expanded.
- **Full-screen apps** (games, video, presentations): the sidebar hides automatically and gives the space back, then returns afterwards.
- **Several monitors**: one sidebar per monitor, each showing a different workspace if you like (e.g. Mozaik CNC on the left screen, VS Code on the right).
- Best for: keeping the current project always one click away without it ever getting covered.

```
 collapsed rail           expanded (overlay expand)
┌──┐                     ┌──┬──────────────────────────┐
│● │ Mozaik CNC          │● │ Mozaik CNC ▾        📌    │
│◆ │ VS Code             │◆ │ Job: 2417 Smith kitchen ▾ │
│▲ │ ComfyUI             │▲ │ 🔍 filter…                │
│■ │ GPTs                │■ │ ▾ Current job             │
│──│                     │──│  📁 Job root  📁 Drawings  │
│📁│                     │📁│  📁 CNC out·3 📁 Cut lists │
│📁│                     │📁│ ▾ Tools & shared          │
│▶ │                     │▶ │  ▶ Mozaik  📁 Library      │
│🖧│                     │🖧│  🖧 \\CNC-PC\Programs ●   │
│  │                     │  │ ▾ Downloads (live)        │
│  │                     │  │  📄 hinge.dxf → Drawings? │
│  │                     │  ├──────── ▴ Shelf · 5 ─────┤
│▴ │                     │▴ │ [img][img] 📝 check depth  │
└──┘                     └──┴──────────────────────────┘
 ↑ reserved space         ↑ reserved   ↑ slides over windows
```

### 3.2 Floating overlay mode
- A free-floating panel you place anywhere, **always on top**.
- **Global hotkey** shows/hides it at the mouse or in its last position. It can auto-hide when it loses focus ("summon" behavior) or stay put ("pinned" behavior), using the 📌 toggle.
- Adjustable opacity when not hovered; optional **click-through** when faded.
- Best for: quick jumps while in Mozaik, VS Code, ComfyUI or any full-screen-ish app without changing your layout.

### 3.3 Desktop board mode
- A large **board on the desktop**, behind all windows (attached to the desktop layer, so it survives `Win+D`).
- Shows the workspace in full: big tiles, the Shelf expanded, reference images shown large.
- Can show **several workspaces side by side** as columns, like a project wall.
- The hotkey **brings the board to the front** temporarily; `Esc` sends it back behind your windows.
- Best for: an overview at the start/end of a day, mood boards, picking a project to work on.

### Global hotkeys (configurable, defaults)
| Hotkey | Action |
|---|---|
| `Win+Alt+Space` | Show/hide the overlay, or bring the board/dock to the front |
| `Win+Alt+P` | Toggle pinned (stays on top) vs. summon behavior |
| `Win+Alt+1..9` | Jump to workspace N |
| `Win+Alt+S` | Open/close the Shelf |
| `Win+Alt+I` | Inbox review (§6) |

### Inside the panel
- **Type to filter**: fuzzy-filters the current workspace, `Enter` opens. `Tab` searches all workspaces.
- `Ctrl+1..9` switch workspace, `Ctrl+Tab` cycle.
- `Space` on a folder = **Peek** (inline file list, arrow keys to navigate, drag files out).
- **Drag & drop**: drop files on a folder tile to **move** (same drive) or **copy** (other drive), with `Ctrl`/`Shift` overrides like Explorer.
  Drop anything on empty space to **pin** it. Drag a tile out to drop the real file/path into another app.

### Sketch (floating overlay, Shelf open)
```
┌─ Naospace ─────────────────── ● ComfyUI ▾ ── 📌 ─ ✕ ┐
│ 🔍 filter…                                         │
│ ▾ Folders                                          │
│   📁 checkpoints 📁 loras 📁 vae 📁 controlnet       │
│   📁 input  📁 output  📁 custom_nodes  📁 root     │
│ ▾ Software & tools                                 │
│   ▶ Start ComfyUI  🛠 VS Code  🛠 Photoshop  ⟳ Update │
│ ▾ Links                                            │
│   🌐 127.0.0.1:8188  🌐 Civitai  🌐 GitHub repo     │
│ ▾ Downloads (live)                                 │
│   📄 flux_style_v2.safetensors   2 min  → loras?    │
├──────────────────── ▴ Shelf ───────────────────────┤
│ [img][img][img][img]  📝 prompt: "cinematic…"       │
│ [img][img]  🎨 #2B1E3F #FF8A3D   📝 todo: fix vae   │
└────────────────────────────────────────────────────┘
```

---

## 4. The Shelf (collapsible bottom drawer)

A per-workspace drawer that slides out from the bottom of the overlay/dock (or sits expanded on the
desktop board). It's for **reference material you want to look at while working**, not for files you open.

| Shelf item | Behavior |
|---|---|
| **Reference image** | Thumbnail; hover to enlarge; click to **pin to screen** as a separate floating always-on-top image (resizable, adjustable opacity, click-through), similar to PureRef/Snipaste |
| **Screenshot** | `Ctrl+V` a clipboard screenshot straight into the Shelf |
| **Note / TODO** | Short text, checkboxes |
| **Snippet** | Text copied to the clipboard on click, e.g. prompts, negative prompts, seeds, CLI args |
| **Color swatch** | Click copies hex |
| **Link preview** | URL with thumbnail |

- Drag a Shelf image **out** and it drops as a real file (e.g. into an email to a client, onto the ComfyUI canvas, or into a browser upload).
- Images can be **linked** (stay where they are) or **copied** into the workspace's own storage
  (`%APPDATA%\Naospace\workspaces\<id>\shelf\`), so deleting a download doesn't break your board.
- Height is remembered; collapsed, it's a single strip that shows a count (`▴ Shelf · 12`).

---

## 5. Stopping the Explorer sprawl

1. **One Explorer window per workspace.** Naospace tracks a window per workspace through the
   `Shell.Application` → `ShellWindows` COM API. Clicking a folder **opens a new tab in that window**
   (Windows 11 tabs) or navigates the active tab, depending on your setting. It never spawns a new window.
2. **Folder groups.** "Open job" opens job root + Drawings + CNC output as 3 tabs in that one window; "Open models" does the same for ComfyUI.
3. **Close workspace.** One click closes all Explorer windows/tabs under the workspace's folders.
4. **Snapshot / Restore.** "Capture" saves the currently open Explorer windows and tabs (and optionally running tools) into a workspace. "Restore" reopens exactly that.
5. **Tidy.** Closes duplicate Explorer windows/tabs pointing to the same folder.
6. **File dialog jump** (borrowed from Quick Access Popup). When an Open/Save/Upload dialog is in front,
   clicking a workspace folder **changes the dialog's folder** instead of opening Explorer. This is
   important for uploading images and loading models.

> Windows 11 has no official API for opening an Explorer tab. We do it the best-effort way (focus window → `Ctrl+T` → navigate)
> and fall back to navigating the existing window if it fails.

---

## 6. Ending the weekend cleanup

- **Live Downloads** in every workspace (or a dedicated `Inbox` workspace).
- **Routing rules** per workspace. They **suggest** by default and only move automatically if you opt in:
  ```
  Mozaik CNC:  *.dxf                          →  ${JOB}\Drawings
               *quote*.pdf / *invoice*.pdf    →  ${JOB}\Client
  VS Code:     *.env / *.pem                   →  ask, never auto-move (secrets)
  ComfyUI:     *.safetensors name has "lora"   →  ${COMFY}\models\loras
  Admin:       *invoice*.pdf                   →  ${CLIENTS}\${CLIENT}\Invoices
  ```
  A matching file shows a `→ Drawings?` chip in the active workspace; one click moves it.
- **Inbox review** (`Win+Alt+I`): everything in Downloads/Desktop older than N days, with *Move to… / Archive / Delete*.

---

## 7. Configuration

Plain JSON in `%APPDATA%\Naospace\workspaces.json` (the location can point to OneDrive/git for syncing),
plus an in-app editor so you rarely need to touch it.

```jsonc
{
  "hotkeys": { "toggle": "Win+Alt+Space", "pin": "Win+Alt+P", "shelf": "Win+Alt+S", "inbox": "Win+Alt+I" },
  "explorer": { "reuseWindow": true, "openAs": "tab" },        // "tab" | "navigate"

  "workspaces": [
    {
      "id": "comfy",
      "name": "ComfyUI",
      "color": "#7C5CFF",
      "mode": { "type": "overlay", "pinned": false, "opacity": 0.92 },   // "dock" | "overlay" | "board"
      // dock example: { "type": "dock", "monitor": "DISPLAY2", "edge": "left", "railWidth": 48,
      //                 "panelWidth": 320, "expand": "overlay", "openOn": "hover", "hideOnFullscreen": true }
      "vars": { "COMFY": "D:\\AI\\ComfyUI_windows_portable\\ComfyUI" },
      "profile": "ai-image",
      "autoActivate": { "windowTitleContains": ["ComfyUI"] },
      "sections": [
        { "name": "Folders", "items": [
          { "type": "folder", "path": "${COMFY}\\models\\checkpoints" },
          { "type": "folder", "path": "${COMFY}\\models\\loras" },
          { "type": "folder", "path": "${COMFY}\\input" },
          { "type": "folder", "path": "${COMFY}\\output" },
          { "type": "group",  "name": "All models", "paths": ["${COMFY}\\models\\checkpoints", "${COMFY}\\models\\loras", "${COMFY}\\models\\vae"] }
        ]},
        { "name": "Software & tools", "items": [
          { "type": "app", "name": "Start ComfyUI", "path": "${COMFY}\\..\\run_nvidia_gpu.bat", "singleInstance": true },
          { "type": "app", "name": "VS Code", "path": "code", "args": "${COMFY}\\custom_nodes" },
          { "type": "command", "name": "Update", "cmd": "git pull", "cwd": "${COMFY}" }
        ]},
        { "name": "Links", "items": [
          { "type": "url", "name": "ComfyUI", "url": "http://127.0.0.1:8188" }
        ]},
        { "name": "Downloads", "items": [
          { "type": "live", "path": "%USERPROFILE%\\Downloads", "count": 6 }
        ]}
      ],
      "shelf": {
        "open": true,
        "items": [
          { "type": "image",   "path": "shelf/ref_lighting.png", "stored": "copy" },
          { "type": "snippet", "name": "Neg prompt", "text": "blurry, lowres, watermark" },
          { "type": "note",    "text": "Try VAE fix on upscale workflow" },
          { "type": "color",   "value": "#FF8A3D" }
        ]
      },
      "routing": [
        { "match": "*.safetensors", "nameContains": "lora", "to": "${COMFY}\\models\\loras" }
      ],
      "launchSet": ["Start ComfyUI", "ComfyUI"]
    },
    {
      "id": "mozaik",
      "name": "Mozaik CNC",
      "color": "#FF8A3D",
      "profile": "cnc-cad",
      "mode": { "type": "dock", "monitor": "DISPLAY1", "edge": "left", "expand": "overlay" },
      "vars": { "MOZAIK": "D:\\Mozaik", "MACHINE": "\\\\CNC-PC\\Programs" },
      "projectSlot": { "name": "Job", "var": "JOB", "from": "${MOZAIK}\\Jobs\\*", "sort": "recent" },
      "sections": [
        { "name": "Current job", "items": [
          { "type": "group",  "name": "Open job", "paths": ["${JOB}", "${JOB}\\Drawings", "${JOB}\\CNC"] },
          { "type": "folder", "path": "${JOB}\\Drawings" },
          { "type": "folder", "path": "${JOB}\\CNC", "badge": "new" },
          { "type": "folder", "path": "${JOB}\\Reports" },
          { "type": "latest", "name": "Latest CNC export", "path": "${JOB}\\CNC" }
        ]},
        { "name": "Tools & shared", "items": [
          { "type": "app",    "name": "Mozaik", "path": "C:\\Program Files\\Mozaik\\Mozaik.exe", "singleInstance": true },
          { "type": "folder", "name": "Library", "path": "${MOZAIK}\\Library" },
          { "type": "folder", "name": "Machine", "path": "${MACHINE}", "status": "reachable" }
        ]}
      ],
      "routing": [
        { "match": "*.dxf", "to": "${JOB}\\Drawings" },
        { "match": ["*quote*.pdf", "*invoice*.pdf"], "to": "${JOB}\\Client" }
      ],
      "launchSet": ["Mozaik", "Open job"]
    },
    {
      "id": "vscode",
      "name": "VS Code",
      "color": "#2F80ED",
      "profile": "code-project",
      "projectSlot": { "name": "Repo", "var": "REPO", "from": ["D:\\Dev\\*", "D:\\Naoko\\IoT Developement\\*"], "sort": "recent" },
      "sections": [
        { "name": "Repo", "items": [
          { "type": "app",     "name": "Open in VS Code", "path": "code", "args": "\"${REPO}\"" },
          { "type": "app",     "name": "Terminal here", "path": "wt", "args": "-d \"${REPO}\"" },
          { "type": "folder",  "path": "${REPO}" },
          { "type": "git",     "path": "${REPO}" },
          { "type": "command", "name": "Dev server", "cmd": "npm run dev", "cwd": "${REPO}", "status": { "port": 3000 } }
        ]}
      ]
    },
    { "id": "gpts",   "name": "GPTs Live", "color": "#22C55E", "mode": { "type": "board" }, "sections": [] }
  ]
}
```

---

## 8. Tech stack

**C# / .NET 8 + WPF**, styled with **WPF-UI** (Fluent + Mica on Windows 11).

Why: every key feature is a Windows shell feature. These are AppBar docking (`SHAppBarMessage`),
global hotkeys (`RegisterHotKey`), topmost/click-through windows, desktop-layer attachment,
`ShellWindows` COM for Explorer reuse, file-dialog navigation, shell drag-drop and `IFileOperation`
(real Explorer-style copy/move with progress and undo). WPF reaches all of them directly and publishes as one exe.

| Rejected | Why |
|---|---|
| WinUI 3 | Modern look, but weaker control over transparency, topmost, AppBar and drag-drop; packaging is painful |
| Tauri / Electron | Every shell feature becomes native plumbing; Electron costs 150 MB+ RAM for a launcher |
| AutoHotkey | Great for a one-evening spike, but can't grow into the Shelf, board and editor |
| PowerToys Command Palette extension | Could be a **companion** later (search workspaces from the palette), but it can't host the overlay, board or Shelf |

**Libraries:** WPF-UI, H.NotifyIcon.Wpf (tray), Vanara (Win32/shell/COM interop), System.Text.Json.
Later: Everything SDK for workspace-scoped search.

```
Naospace.Core    Workspace model, config, variables, routing rules (no UI, unit-tested)
Naospace.Shell   Hotkeys, AppBar, desktop layer, Explorer reuse/tabs, file dialogs, file ops, icons, watchers
Naospace.App     WPF: Dock / Overlay / Board views, Shelf, image pins, editor, tray
```

---

## 9. Roadmap

The roadmap now lives in [VISION.md §8 Milestones](VISION.md#8-milestones), alongside the full feature system.

---

## 10. Existing tools (what we borrow, what's missing)

| Tool | What's good (borrow) | What's missing |
|---|---|---|
| **PowerToys Command Palette (+ Dock)** | Keyboard-first launcher with a pinned bar of commands and an extension model | No per-project workspaces, no reference shelf, no Explorer window management |
| **Quick Access Popup** | Popup menu of favorites, **folder groups**, **changes folder inside file dialogs** and the current Explorer window | Menu only, nothing stays visible; no overlay/board, no images |
| **PowerToys Workspaces** | Restores app window layouts | Not folders/tabs, no overlay |
| **Stardock Fences** (folder portals) | Desktop-layer boards | Desktop-only, no hotkey summon, no project contexts |
| **PureRef / Snipaste** | Reference images pinned on top of the screen | Separate from your project files and tools |
| **Directory Opus / Files / XYplorer** | Tabs, layouts | Full Explorer replacements, not a light project switcher |

Naospace combines these: **project workspaces with folders, tools and references, shown as a dock, an overlay or
a desktop board, and working *with* Explorer instead of replacing it.**

---

## 11. Decisions

**Decided**
- Windows 11 only
- Three modes: Dock, Floating overlay, Desktop board
- Dock = narrow expandable sidebar on a chosen monitor edge that reserves desktop space (maximized windows don't cover it)
- Pinning of folders, software/tools, links, plus a collapsible bottom Shelf for reference images and notes

**Still open**
1. Default hotkey (`Win+Alt+Space`, `` Ctrl+` ``, a mouse button…)?
2. Shelf images: copy into workspace storage by default, or link only?
3. Routing rules: always ask, or allow auto-move per rule?
4. Config location: `%APPDATA%` or a synced folder?
5. Sidebar expand default: overlay (windows never move) or push (windows resize)?
