# WorkspaceOS Configuration

All settings are stored in `%APPDATA%\WorkspaceOS\config.json`. The Settings UI (gear icon on the top bar, or `Alt+Space` → "Open Settings") edits the same file. The file is created with defaults on first run; delete it to reset.

Use **Settings → Advanced → Export/Import** to back up or share configurations.

## Sections

### General
```json
"General": {
  "StartWithWindows": true,
  "MaximizeNewWindows": true,
  "HideBarInFullscreen": true,
  "FocusWindowOnHover": true,
  "ActiveWorkspace": 1
}
```
`MaximizeNewWindows` maximizes each new normal application window once; the window then remains an ordinary movable/resizable window. Fullscreen games, dialogs and tool windows are skipped.

`HideBarInFullscreen` (default `true`) hides every top bar and releases its Win32 appbar reservation while a fullscreen game, fullscreen browser video, or fullscreen/borderless app is in front. The app gets the full monitor; normal maximized windows, dialogs and the desktop keep the bar. Turning the setting off restores the bar immediately.

`FocusWindowOnHover` (default `true`) focuses the ordinary top-level window under the mouse after it stays in roughly the same spot for 120 ms. After a workspace switch, the window under the pointer is focused immediately, so the next `Ctrl+T` or keystroke reaches the app on the newly visible desktop. This is focus-follows-mouse: it never injects clicks. It pauses while a fullscreen game is in front or a mouse button is held, and ignores the bar, taskbar, desktop, menus, tool windows, cloaked windows and WorkspaceOS's own windows.

### Appearance
```json
"Appearance": {
  "BarBackground": "#FF05080A",
  "BarForeground": "#FFE8F8F5",
  "AccentColor": "#FF7FA7A3",
  "ActiveWorkspaceBackground": "#FF00E0CF",
  "ActiveWorkspaceForeground": "#FF04100F",
  "ModuleLabelColor": "#FF6FD8CF",
  "ModuleBackground": "#FF081012",
  "ModuleBorder": "#FF123A37",
  "SeparatorColor": "#FF123A37",
  "BarIcon": "GD Fish",
  "BarIconPath": "",
  "BarIconWidth": 30,
  "ShowBarIcon": true,
  "FontFamily": "JetBrainsMono Nerd Font, Cascadia Mono, Consolas",
  "FontSize": 12.5,
  "BarHeight": 32
}
```
Colors are `#AARRGGBB` hex. `AccentColor` is the muted teal used for inactive workspace numbers; `ModuleBackground`/`ModuleBorder` style the right-hand sensor chips.

Every bar colour is editable from the gear button on the bar (Settings → Appearance). Each colour row has a live swatch: click it for the Windows colour picker, or type a hex value. The gear opens the same Settings window as the tray menu.

### Themes

Settings → Appearance starts with six built-in themes (`Neon Void`, `Midnight Synth`, `Toxic Bloom`, `Solar Flare`, `Frost Byte`, `Blood Moon`). Clicking a tile writes all nine colour keys below and stores the name in `Appearance.Theme`; press **Apply & Save** to use it.

Each theme has an original **3840 × 2160** wallpaper. **Download 4K wallpaper** renders the currently selected theme in the app and saves it as a PNG, defaulting to `Pictures\WorkspaceOS`. Nothing is downloaded from the internet and the artwork is generated from the theme palette, so it is always native 4K. **Set as desktop wallpaper** does the same render and applies it as the Windows background (fill mode), caching the PNG in `%LOCALAPPDATA%\WorkspaceOS\wallpapers`.

**Photo wallpapers** lists eight curated Unsplash photos, downloaded at 3840 px wide on demand. Photos are free to use under the Unsplash License; **Save** writes a copy to `Pictures\WorkspaceOS`, **Set** applies it to the desktop. An internet connection is needed the first time.

**Theme from your wallpaper** samples the image Windows is using (`Theme from current wallpaper`) or any picture you pick, then derives the nine bar colours: darkest hue for the surface, most vivid hue for the active slot. With **Match bar colours to the wallpaper** checked, the bar is re-themed automatically whenever you set a wallpaper from Settings. The derived palette is not a named preset, so the theme highlight clears; press **Apply & Save** to keep it.

`BarHeight` (20–80 px) is set with the bar height slider in Settings → Appearance. The bar reserves exactly that many pixels at the top of each monitor and maximized windows stop below it. `BarIconWidth` has its own 12–64 px slider; on a short bar the icon shrinks further so it never overflows the strip.

`BarIcon` selects the ship mark drawn at the left of the bar. Built-ins are original GD-style vector ships: `GD Fish`, `Comet`, `Viper`, `Nova`, `Prism`, `Warden`, `Specter`, `Ember`. `BarIconPath` points at a PNG/JPG/GIF/BMP you own (for example an icon bought from a community shop) and overrides `BarIcon` when the file exists; an empty or unreadable path falls back to the selected built-in ship. `BarIconWidth` is the icon's width in pixels (12–64). `ShowBarIcon` hides the mark entirely.

### Workspaces
```json
"Workspaces": [
  { "Index": 1, "Name": "code" },
  { "Index": 2, "Name": "web" }
]
```
1–9 workspaces. `Name` is what the bar displays (empty = the number). Workspaces are Windows' native virtual desktops: WorkspaceOS creates real desktops to match this list at startup, and the bar also shows any extra desktops you add natively (`Ctrl+Win+D`).

### Rules — automatic workspace assignment
```json
"Rules": [
  { "Match": "Code.exe",    "MatchType": "Executable", "Workspace": 1, "Enabled": true },
  { "Match": "chrome.exe",  "MatchType": "Executable", "Workspace": 2, "Enabled": true },
  { "Match": "discord.exe", "MatchType": "Executable", "Workspace": 4, "Enabled": true },
  { "Match": "spotify",     "MatchType": "Process",    "Workspace": 0, "Enabled": true }
]
```
- `MatchType`: `Executable` (file name), `Process` (name without .exe), `Title` (substring), `Class` (window class), `Regex` (matched against title and exe)
- `Workspace: 0` pins the app to **all** workspaces (native desktop pinning, same as Task View → "Show this window on all desktops")
- First matching rule wins.

### Hotkeys
```json
"Hotkeys": { "Bindings": { "Workspace1": "Win+1", "Launcher": "Alt+Space", "StartMenu": "Win+Space", "...": "..." } }
```
Format: modifiers `Win`, `Ctrl`, `Alt`, `Shift` joined with `+`, ending in a key (`A–Z`, `0–9`, `F1–F24`, `Left/Right/Up/Down`, `Space`, `Enter`, …). Empty string disables a binding. Combos Windows reserves are automatically taken over via a keyboard hook; **Win+number combos are intercepted by the bundled AutoHotkey runtime** (see `Automation.EnableAutoHotkey`), which also owns any other binding you configure.

The scratchpad defaults are `ScratchpadSend` = `Ctrl+Win+S` and `ScratchpadToggle` = `Win+S`. Sending stashes the focused window; toggling opens or closes it from any workspace. Pressing `Ctrl+Win+S` again while the scratchpad window itself is focused takes it back out. The stashed window is restored when WorkspaceOS exits. Override either binding like any other hotkey; set it to an empty string to disable it.

### Windows

WorkspaceOS does not tile windows on Windows. It never resizes a window or changes its borders automatically, so games and other apps keep their own window mode and resolution. Window rules only assign applications to workspaces.

While a fullscreen app is in front, the top bar hides and removes its reserved appbar strip; it returns when the app is closed or no longer fullscreen. This is controlled by `General.HideBarInFullscreen`.

`Win+Shift+Arrow` moves the focused window to the nearest monitor in that direction, preserving its relative position and size. This is a one-shot geometry move with no layout state.

Old `Tiling` sections and `Tile*` / `ToggleTiling` hotkeys in existing configs are discarded on Windows during the next launch.

### Automation

```json
"Automation": { "EnableAutoHotkey": true, "AutoHotkeyPath": "" }
```

| Field | Meaning |
|---|---|
| `EnableAutoHotkey` | run the bundled AHK v2 runtime for global hotkeys (Win+1..9 etc.) |
| `AutoHotkeyPath` | custom `AutoHotkey64.exe` (empty = bundled/next-to-exe/PATH/Program Files) |

### Top bar
```json
"Bar": { "Modules": ["CPU","RAM","GPU","Disk","NetUp","NetDown","Battery","Volume"], "RefreshMs": 2000, "ShowSeconds": false }
```
Modules render right-to-left in the listed order; remove entries to hide them.

### System monitor
```json
"Monitor": { "RefreshMs": 1500, "ProcessCount": 12 }
```

### Focus Mode
```json
"Focus": { "PresetMinutes": [15,25,45,60], "BlockedApps": ["discord.exe","steam.exe"], "LastDurationMinutes": 25 }
```
Opening (or focusing a new window of) any blocked app during a session fails it immediately.

## Data files

| File | Purpose |
|---|---|
| `config.json` | all settings |
| `workspaceos.ahk` | generated AutoHotkey v2 script (regenerated from your bindings; do not edit) |
| `workspaceos.log` | log (Settings → Advanced → Open log) |
| `focus-history.json` | focus session history |
| `clipboard.json` | clipboard history |
