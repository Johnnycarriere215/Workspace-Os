# WorkspaceOS Configuration

All settings are stored in `%APPDATA%\WorkspaceOS\config.json`. The Settings UI (gear icon on the top bar, or `Alt+Space` → "Open Settings") edits the same file. The file is created with defaults on first run; delete it to reset.

Use **Settings → Advanced → Export/Import** to back up or share configurations.

## Sections

### General
```json
"General": {
  "StartWithWindows": true,
  "ActiveWorkspace": 1
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
"Hotkeys": { "Bindings": { "Workspace1": "Win+1", "Launcher": "Alt+Space", "...": "..." } }
```
Format: modifiers `Win`, `Ctrl`, `Alt`, `Shift` joined with `+`, ending in a key (`A–Z`, `0–9`, `F1–F24`, `Left/Right/Up/Down`, `Space`, `Enter`, …). Empty string disables a binding. Combos Windows reserves are automatically taken over via a keyboard hook; **Win+number combos are intercepted by the bundled AutoHotkey runtime** (see `Tiling.EnableAutoHotkey`), which also owns any other binding you configure.

### Tiling

All tiling behavior lives in the `Tiling` object. **The engine is on by default since v4** (Hyprland-style auto-tiling of new windows) — turn it off with `"EnableTiling": false` (or `Win+Shift+T`); it stays off.

```json
"Tiling": {
  "EnableTiling": true,
  "ManageNewWindows": true,

  "PreserveSplit": false,
  "SmartSplit": false,
  "DefaultSplit": "Auto",
  "DefaultSplitRatio": 0.5,
  "SplitBias": "New",
  "MinSplitRatio": 0.1,
  "MaxSplitRatio": 0.9,
  "PersistentPreselect": "None",

  "Pseudotile": false,

  "InnerGap": 6,
  "OuterGap": 6,
  "SmartGaps": true,
  "ActiveIndicator": true,
  "IndicatorColor": "#FFFFD75F",

  "FollowMovedWindow": true,
  "FocusWrap": false,
  "ResizeStep": 5,
  "MinWindowSize": 120,
  "CenterSingleWindow": false,
  "SingleWindowMaxWidthPct": 0,

  "EnableScratchpad": false,
  "TilingDebug": false,

  "EnableAutoHotkey": true,
  "AutoHotkeyPath": "",

  "TilingRules": [
    { "Match": "Calculator.exe", "MatchType": "Executable", "Action": "Float",  "Enabled": true },
    { "Match": "mspaint",       "MatchType": "Process",    "Action": "Float",  "Enabled": true },
    { "Match": "Steam",          "MatchType": "Title",      "Action": "Ignore", "Enabled": true }
  ]
}
```

Field reference:

| Field | Meaning |
|---|---|
| `EnableTiling` | master switch (also `Win+Shift+T`) |
| `ManageNewWindows` | tile windows that open while tiling is on |
| `PreserveSplit` | Hyprland `preserve_split`: inserted splits keep the orientation they were created with |
| `SmartSplit` | split where the cursor enters the focused window (implies preserve-like behavior for that split) |
| `DefaultSplit` | `Auto` (decide from the focused window's aspect, Hyprland dwindle), `Horizontal`, or `Vertical` |
| `DefaultSplitRatio` | share given to the new window (0.1–0.9) |
| `SplitBias` | `New`: new window gets the ratio; `Active`: existing window keeps it |
| `PersistentPreselect` | `None` (one-shot `Win+Shift+Arrow`) or a direction that always applies |
| `Pseudotile` | default pseudotile state for newly tiled windows |
| `InnerGap` / `OuterGap` | px gaps between windows / around the layout |
| `SmartGaps` | no gaps when exactly one window is tiled |
| `ActiveIndicator` | Windows 11 DWM accent border on the focused window (no-op on Win10) |
| `FollowMovedWindow` | switch to a workspace after sending a window to it |
| `FocusWrap` | directional focus wraps at layout edges (default off) |
| `ResizeStep` | percent of split changed per resize keypress |
| `CenterSingleWindow` / `SingleWindowMaxWidthPct` | center the only window and cap its width (0 = off) |
| `EnableScratchpad` | `Win+S` overlay toggle |
| `TilingDebug` | verbose adoption/layout logging |
| `EnableAutoHotkey` | run the bundled AHK v2 runtime for global hotkeys (Win+1..9 etc.) |
| `AutoHotkeyPath` | custom `AutoHotkey64.exe` (empty = bundled/next-to-exe/PATH/Program Files) |
| `TilingRules` | per-window tiling decisions; `Action`: `Float`, `Tile`, or `Ignore`; first match wins; matched like workspace rules (`MatchType`: Executable/Process/Title/Class/Regex) |

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
