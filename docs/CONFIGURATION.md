# WorkspaceOS Configuration

All settings are stored in `%APPDATA%\WorkspaceOS\config.json`. The Settings UI (gear icon on the top bar, or `Alt+Space` → "Open Settings") edits the same file. The file is created with defaults on first run; delete it to reset.

Use **Settings → Advanced → Export/Import** to back up or share configurations.

## Sections

### General
```json
"General": {
  "StartWithWindows": true,
  "MaximizeNewWindows": true,
  "ActiveWorkspace": 1
}
```
`MaximizeNewWindows` maximizes each new normal application window once; the window then remains an ordinary movable/resizable window. Fullscreen games, dialogs and tool windows are skipped.

### Appearance
```json
"Appearance": {
  "BarBackground": "#FF05080A",
  "BarForeground": "#FFE8F8F5",
  "AccentColor": "#FF7FA7A3",
  "ActiveWorkspaceBackground": "#FF00E0CF",
  "ActiveWorkspaceForeground": "#FF04100F",
  "FontFamily": "JetBrainsMono Nerd Font, Cascadia Mono, Consolas",
  "FontSize": 12.5,
  "BarHeight": 32
}
```
Colors are `#AARRGGBB` hex. `AccentColor` is the muted teal used for inactive workspace numbers.

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

### Windows

WorkspaceOS does not tile windows on Windows. It never resizes a window or changes its borders automatically, so games and other apps keep their own window mode and resolution. Window rules only assign applications to workspaces.

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
