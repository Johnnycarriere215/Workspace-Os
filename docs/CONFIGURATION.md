# WorkspaceOS Configuration

All settings are stored in `%APPDATA%\WorkspaceOS\config.json`. The Settings UI (gear icon on the top bar, or `Alt+Space` → "Open Settings") edits the same file. The file is created with defaults on first run; delete it to reset.

Use **Settings → Advanced → Export/Import** to back up or share configurations.

## Sections

### General
```json
"General": {
  "StartWithWindows": true,
  "ActiveWorkspace": 1,
  "RestoreWorkspacesOnStart": true
}
```

### Appearance
```json
"Appearance": {
  "BarBackground": "#FF000000",
  "BarForeground": "#FFE0E0E0",
  "AccentColor": "#FFFFD75F",
  "ActiveWorkspaceBackground": "#FFFFFFFF",
  "ActiveWorkspaceForeground": "#FF000000",
  "FontFamily": "Consolas",
  "FontSize": 13,
  "BarHeight": 28
}
```
Colors are `#AARRGGBB` hex. `AccentColor` is the inactive-workspace yellow.

### Workspaces
```json
"Workspaces": [
  { "Index": 1, "Name": "code" },
  { "Index": 2, "Name": "web" }
]
```
1–9 workspaces. `Name` is what the bar displays (empty = the number).

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
- `Workspace: 0` pins the app to **all** workspaces
- First matching rule wins.

### Hotkeys
```json
"Hotkeys": { "Bindings": { "Workspace1": "Win+1", "Launcher": "Alt+Space", "...": "..." } }
```
Format: modifiers `Win`, `Ctrl`, `Alt`, `Shift` joined with `+`, ending in a key (`A–Z`, `0–9`, `F1–F24`, `Left/Right/Up/Down`, `Space`, `Enter`, …). Empty string disables a binding. Combos Windows reserves are automatically taken over via a keyboard hook.

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
| `workspaceos.log` | log (Settings → Advanced → Open log) |
| `focus-history.json` | focus session history |
| `clipboard.json` | clipboard history |
| `session.json` | workspace assignments for restart restore |
| `hidden-windows.json` | crash-recovery journal (deleted on clean exit) |
