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
`MaximizeNewWindows` maximizes each new normal application window once, then leaves it movable and resizable. Fullscreen games, dialogs and tool windows are skipped.

`HideBarInFullscreen` (default `true`) hides every top bar and releases its Win32 appbar reservation while a fullscreen game, fullscreen browser video, or fullscreen/borderless app is in front. The app gets the full monitor; normal maximized windows, dialogs and the desktop keep the bar. Turning the setting off restores the bar immediately.

`FocusWindowOnHover` (default `true`) focuses the ordinary top-level window under the mouse after it stays in roughly the same spot for 120 ms. After a workspace switch, the window under the pointer is focused immediately, so the next `Ctrl+T` or keystroke reaches the app on the newly visible desktop. This is focus-follows-mouse: it never injects clicks. It pauses while a fullscreen game is in front or a mouse button is held, and ignores the bar, taskbar, desktop, menus, tool windows, cloaked windows and WorkspaceOS's own windows.

### Appearance
```json
"Appearance": {
  "BarBackground": "#FF121212",
  "BarForeground": "#FFD9D9D9",
  "AccentColor": "#FF805B10",
  "ActiveWorkspaceBackground": "#FFC9A227",
  "ActiveWorkspaceForeground": "#FF121212",
  "ModuleLabelColor": "#FFC9A227",
  "ModuleBackground": "#FF1F1F1F",
  "ModuleBorder": "#FF333333",
  "SeparatorColor": "#FF333333",
  "BarTheme": "Gold Rush",
  "BarIcon": "Shibumi",
  "BarIconPath": "",
  "BarIconWidth": 84,
  "ShowBarIcon": true,
  "FontFamily": "JetBrainsMono Nerd Font, Cascadia Mono, Consolas",
  "FontSize": 12.5,
  "BarHeight": 32
}
```
Colors are `#AARRGGBB` hex. `AccentColor` is the muted colour used for inactive workspace numbers; `ModuleBackground`/`ModuleBorder` style the right-hand sensor chips. `BarTheme` names the Omarchy theme those nine keys came from — it is what themes the tiler borders and the taskbar preset, so set it (or pick a theme from the logo menu) rather than editing colours alone.

Every bar colour is editable from the gear button on the bar (Settings → Appearance). Each colour row has a live swatch: click it for the Windows colour picker, or type a hex value. The gear opens the same Settings window as the tray menu.

### Themes

Settings → Appearance lists the Omarchy themes this build ships — **Gold Rush** (default), **Harbor**, **Harbor Dark**, **Matte Black**, **Vantablack**, **Pokemon**, **Rose Pine**, **Tokyo Night**, **Catppuccin**, **Gruvbox**, **Nord**, **Everforest**, **Kanagawa** and **Miasma**. Clicking a tile writes all nine colour keys and stores both `Appearance.BarTheme` and `Appearance.Theme`; press **Apply & Save** to use it.

The same list is on the bar's logo menu, and `Ctrl+Win+Shift+Space` cycles forward without opening anything. A theme change repaints the bar, Settings, the launcher, the Shibumi panels, the cava widget and the generated Windhawk taskbar preset — and sets the Windows accent colour, app mode and the theme's wallpaper on every monitor and virtual desktop.

Each theme has an original **3840 × 2160** wallpaper. **Download 4K wallpaper** renders the currently selected theme in the app and saves it as a PNG, defaulting to `Pictures\WorkspaceOS`. It needs a selected preset — with a `Custom` palette it asks you to pick a theme instead of exporting artwork for a theme you are not using. Nothing is downloaded from the internet and the artwork is generated from the theme palette, so it is always native 4K. **Set as desktop wallpaper** does the same render and applies it as the Windows background (fill mode) to **every monitor and every virtual desktop**, caching the PNG in `%LOCALAPPDATA%\WorkspaceOS\wallpapers`. The same path is used by the theme switch: WorkspaceOS copies the theme's own artwork into that cache, calls the shell's `IDesktopWallpaper` service once with a null monitor id (all monitors and every virtual desktop), and verifies each monitor actually reports the new file. `SystemParametersInfo(SPI_SETDESKWALLPAPER)` is only the fallback for builds without the shell service — running both in sequence applied the image twice, which is why a theme change now paints the desktop exactly once.

**Photo wallpapers** lists eight curated Unsplash photos, downloaded at 3840 px wide on demand. Photos are free to use under the Unsplash License; **Save** writes a copy to `Pictures\WorkspaceOS`, **Set** applies it to the desktop. An internet connection is needed the first time.

**Theme from your wallpaper** samples the image Windows is using (`Theme from current wallpaper`) or any picture you pick, then derives the nine bar colours: darkest hue for the surface, most vivid hue for the active slot. With **Match bar colours to the wallpaper** checked, the bar is re-themed automatically whenever you set a wallpaper from Settings. A derived — or hand-edited — palette is not a preset: **Apply & Save** stores `Appearance.Theme` as `"Custom"`, and the gallery highlights its **Custom** tile instead of a stale theme. Selecting a preset again overwrites it as usual.

Each **Photo wallpapers** card shows its photographer credit, linking to that photographer's Unsplash profile (or to Unsplash when the photographer could not be verified).

`BarHeight` (20–80 px) is set with the bar height slider in Settings → Appearance. The Shibumi form reserves that many pixels plus a 5 px breathing gap, so maximized windows stop clear of the island instead of touching it. `BarIconWidth` has its own 12–140 px slider; on a short bar the mark shrinks further so it never overflows the strip.

`BarIcon` selects the mark drawn at the left edge of the bar — the same control opens the bar menu. Built-ins are the actual Shibumi-Shell launcher assets: `Shibumi` (default wordmark), `Omarchy`, `Hyprland`, `Arch`, `Omacom`, `Dragon` and `Mark`. `BarIconPath` points at a PNG/JPG/GIF/BMP you own and overrides `BarIcon` when the file exists; an empty or unreadable path falls back to the selected built-in mark. `BarIconWidth` is the mark's width in pixels (12–140). `ShowBarIcon` hides it — the menu still opens from the bar's left edge.

### Bar — form, logo and cava
```json
"Bar": {
  "WorkspaceSlots": 5,
  "Modules": ["CPU", "RAM", "GPU", "Disk", "NetUp", "NetDown", "Battery", "Volume"],
  "Profile": "Shibumi",
  "Logo": "Hyprland",
  "ShowCava": true,
  "CavaBars": 18,
  "CavaPath": ""
}
```
`Profile` is `Shibumi` (one full-width run, 5 px inset with a 5 px gap below; the default), `Full` (edge to edge), `Fit` (inset with rounded corners), `Dock` (deeper inset) or `Notch` (a centred pill). Every form reserves the same appbar strip, so maximized windows never cover the bar.

`ShowCava` puts the audio bars in the centre cluster. WorkspaceOS starts your own `cava.exe` with a generated config (`%APPDATA%\WorkspaceOS\cava-workspaceos.conf`) that only changes cava's output to raw ASCII; your cava config, shaders and themes are untouched. `CavaBars` is the number of bars (8–48, default 18), and `CavaPath` overrides autodetection (`%LOCALAPPDATA%\cava\cava.exe`, then `PATH`). The bars are painted from `AccentColor` to `ActiveWorkspaceBackground`, so they recolour with the theme.

### Shibumi shell

```json
"Bar": {
  "Profile": "Shibumi",
  "Shibumi": {
    "BarHeight": 35,
    "IslandRadius": 16,
    "PillHeight": 24,
    "PillRadius": 12,
    "PillPaddingX": 9,
    "Frost": true,
    "Borders": true,
    "Shadow": false,
    "WorkspaceMarker": "Numbers",
    "HiddenWidgets": [],
    "Sections": {}
  }
}
```

`Shibumi` is the Windows port of the Shibumi-Shell bar. The geometry is the suite's V1 presentation contract: a 35 px strip (38 px exclusive band), 32 px islands inset 5 px with a 16 px radius, and 24 px pills (12 px radius, 9 px padding per side, 5 px content gap). `Frost` makes the islands translucent through the wallpaper; `Borders` draws the 1 px pill and island edges; `Shadow` is the optional panel shadow, off by default as in V1.

The plugin catalogue is generated from the Shibumi-Shell fork's own manifests (`src/WorkspaceOS/Assets/shibumi/plugins.json`, MIT) — ids, roles G1–G18, default sections and descriptions. Each plugin is bound to a Windows backend:

| Role | Plugin | Windows backend |
| --- | --- | --- |
| G1 | Control Center | WorkspaceOS control centre |
| G2 | Workspaces | Native virtual desktops |
| G3 | Tray + notifications | Windows notification area |
| G4 / G5 | Memory / CPU | Global memory status, processor times |
| G6 | Audio | Core Audio (WASAPI) |
| G7 | AI usage | Not available on Windows |
| G8 | Weather, clock, date, status | Windows clock + system status |
| G9 | Media | Needs the WinRT media bridge (hidden) |
| G10 | Quick access | Themes, wallpaper, settings |
| G11 | Network | Win32 network interfaces + `netsh` |
| G12 | Battery | `GetSystemPowerStatus` |
| G13 | Brightness | WMI `WmiMonitorBrightness` |
| G14 | Power profile | `powercfg` scheme list/set |
| G15 | Bluetooth | `BluetoothFindFirstRadio` |
| G16 / G17 / G18 | Temperature / GPU / Storage | Metrics service |

`Sections` maps a plugin id (or bar key) to `left`, `center` or `right`, overriding the manifest's default; `HiddenWidgets` removes a widget from the bar. Both are edited on the **Shibumi shell** page, opened from the bar's logo menu. A widget whose sensor is missing on this machine is not drawn at all, which is how batteryless desktops and desktops without a brightness controller stay clean.

Every theme change also writes the theme's wallpaper to all monitors and virtual desktops, the Windows accent colour, the light/dark app mode and the Windhawk taskbar preset. The wallpaper is copied to `%LOCALAPPDATA%\WorkspaceOS\wallpapers` first, so Windows never holds a lock on the app's own files.

### Taskbar

```json
"Taskbar": { "EnableAppearance": true, "Mode": "Dynamic", "Opacity": 0.35 }
```

`Mode` is `Dynamic`, `Clear`, `Blur`, `Acrylic` or `Disabled`; `Opacity` is the
acrylic tint strength from 0 to 1.

On Windows 10, and on Windows 11 with a classic taskbar, WorkspaceOS sets the taskbar window's accent policy directly — the same `SetWindowCompositionAttribute` mechanism TranslucentTB uses. `Dynamic` is clear on the desktop, blurred over a normal window and acrylic over a maximized one; the tint is the active theme's paper colour, so the taskbar and the bar agree. There is no patch, no injected DLL and no restart — the surface is re-asserted after Explorer restarts and restored when WorkspaceOS exits.

Windows 11 22H2+ (including 24H2/25H2) draws the taskbar with XAML and ignores the accent policy. WorkspaceOS detects that by checking whether Explorer loaded `Taskbar.dll` and reports `taskbar=xaml … windhawk=enabled` in `diag` when the styler is enabled; it does not inject code into Explorer. The app writes the preset to `%APPDATA%\WorkspaceOS\taskbar\windows-11-taskbar-styler.registry.json` on every theme change. The preset is a `controlStyles` set, not one of the mod's bundled themes: it paints the taskbar's `RootGrid` with a **`WindhawkBlur`** brush tinted in the theme's paper colour, and collapses the stock fill and border underneath it. `WindhawkBlur` is used rather than `AcrylicBrush` because acrylic renders opaque inside the taskbar island on Windows 11 — an acrylic bar looks like a flat panel, while `WindhawkBlur` blurs the real window behind it. Windhawk has no command-line installer, so install `windows-11-taskbar-styler` (and, for Explorer, `windows-11-file-explorer-styler`) from Windhawk's own mod list first; the app's preset files are staged either way. Then run `tools\apply-windhawk-taskbar.ps1` once elevated — it ships inside the installer at `%LOCALAPPDATA%\Programs\WorkspaceOS\tools\` and in the portable zip. It finds the compiled library Windhawk registered on this machine, applies the current theme's glass to whichever mods are installed, and registers the task. It also registers the highest-privileges **WorkspaceOS Taskbar Preset** task, which WorkspaceOS starts by name on every theme change — running an existing task needs no UAC prompt, and Windhawk re-reads its settings when they change, so neither a theme change nor the task restarts the Windhawk engine. Pass `-Restart` to the script only to re-inject the mod into a running Explorer. Without the task the preset is only staged in `%APPDATA%\WorkspaceOS\taskbar\` and the log says so. **Copy taskbar preset** in the bar menu or Settings remains for manual paste. The preset is generated in the active theme's colours and is the same route the win rice uses.

**File Explorer.** Explorer draws its own opaque XAML background and ignores the accent policy, so its glass comes from a second Windhawk mod, `windows-11-file-explorer-styler`. Windows has no supported way to make Explorer translucent, and Windhawk has no command-line installer, so this mod has to be installed once by hand from Windhawk's mod list; everything after that is automatic. WorkspaceOS writes `%APPDATA%\WorkspaceOS\explorer\windows-11-file-explorer-styler.registry.json` on every theme change and applies it with the same **WorkspaceOS Taskbar Preset** task. The preset selects the mod's `WindowGlass` theme with `backgroundTranslucentEffect: acrylicblur` (an accent blur behind the whole window) and overrides the two constants that would otherwise paint Explorer solid — `MainContentBG` (the file list, Home and Gallery background, which the theme sets to an opaque `SystemChromeAltHighColor`) and `Background` (menus and command-bar flyouts) — with a `WindhawkBlur` in the bar's paper colour, tinted more solidly than the bar's because a whole window of text needs more separation. That makes the content itself translucent, not just the window frame, so Explorer reads as the same glass as the taskbar and re-tints on every theme change without restarting the Windhawk engine. One ordering constraint matters: Windhawk applies a settings change by re-initialising its XAML hooks inside every open Explorer window, so the preset is always written new-values-first and pruned afterwards, and the taskbar and Explorer presets are applied in a single elevated run. Until the mod is installed the preset is only staged, and the log says so once per session.
### Theme files (drop-in)

Any Omarchy theme works: create `%APPDATA%\WorkspaceOS\themes\<name>\` and put that theme's `colors.toml` in it. The folder name becomes the theme name (a `colors.toml` also used for a built-in name overrides it). The file is a flat list of `key = "value"` lines — `background`, `foreground`, `accent`, optional `muted`, and `color0`–`color15`. Comments and other sections are fine. Malformed files are skipped rather than breaking the cycle; reopen the menu (or restart) after adding one.

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
`StartMenu` defaults to `Win+Space` and clicks the native Start button, so the chord opens the Windows-key menu through the generated AutoHotkey script. `Launcher` is the command palette and defaults to `Alt+Space`; `LauncherAlt` is an optional second key for it and is empty by default.

Format: modifiers `Win`, `Ctrl`, `Alt`, `Shift` joined with `+`, ending in a key (`A–Z`, `0–9`, `F1–F24`, `Left/Right/Up/Down`, `Space`, `Enter`, …). Empty string disables a binding. Combos Windows reserves are automatically taken over via a keyboard hook; **Win+number combos are intercepted by the bundled AutoHotkey runtime** (see `Automation.EnableAutoHotkey`), which also owns any other binding you configure.

**The shipped defaults** — the table a fresh install gets, and the one `HotkeyDefaultsTests` checks:

| Action | Chord | What it does |
|---|---|---|
| `Workspace1` … `Workspace9` | `Win+1` … `Win+9` | switch to that workspace |
| `SendToWorkspace1` … `SendToWorkspace9` | `Win+Shift+1` … `Win+Shift+9` | send the focused window to that workspace |
| `CloseWindow` | `Win+W` | `WM_CLOSE` to the focused window |
| `MaximizeWindow` / `RestoreWindow` | `Win+M` / `Win+Shift+M` | maximize / restore |
| `CenterWindow` | `Win+C` | center on the monitor |
| `FullscreenWindow` | `Win+F` | borderless fullscreen toggle |
| `MoveWindowToMonitorLeft` … `Down` | `Win+Shift+←/→/↑/↓` | move the window to the monitor in that direction |
| `FocusMode` | `Win+F1` | focus session |
| `Launcher` | `Alt+Space` | command palette (`LauncherAlt` is an optional second chord, empty by default) |
| `StartMenu` | `Win+Space` | the native Start menu |
| `ClipboardManager` | `Win+V` | clipboard history |
| `ScratchpadToggle` | `Win+S` | show / hide the scratchpad |
| `ScratchpadSend` | `Ctrl+Win+S` | send the focused window to the scratchpad |
| `VolumePanel` | `Ctrl+Win+A` | audio panel; ↑ / ↓ then step the level |
| `ThemePicker` | `Ctrl+Win+Shift+Space` | theme picker — ← / → choose, Enter applies |
| `CycleTheme` | *(empty)* | cycle themes without the picker |
| `MoveWindowLeft` … `Down` | *(empty)* | 40 px nudges; `Win+Arrow` is left to Aero Snap |

`Win+Arrow` belongs to Windows: Aero Snap halves, maximizes and docks a window from the keyboard. The nudges sat on those chords in earlier builds; 5.1.0 clears them from existing configs when they are still untouched, so snapping works again. Bind them yourself if you would rather nudge.

Some defaults share a chord with Windows itself — `Win+S` Search, `Win+V` clipboard history, `Win+M` minimize-all, `Win+Shift+M` restore-minimized, `Win+C` Copilot, `Win+F` Feedback Hub, `Win+W` Widgets, `Win+Space` input language. The shell takes them over deliberately (the bundled AutoHotkey runtime claims them before Explorer); clear the field to hand one back. When two actions do end up on the same chord, the second is reported in Settings → Hotkeys and skipped, so one key never fires twice.

On Linux the daemon keeps its tiling chords (`TileFocusLeft` … on `Win+H`, preselect on `Win+Shift+Arrow`) and leaves the Windows-only monitor moves empty. Its tiling scratchpad is unbound there so `Win+S` has exactly one owner.

The scratchpad defaults are `ScratchpadSend` = `Ctrl+Win+S` and `ScratchpadToggle` = `Win+S`. Sending stashes the focused window; toggling opens or closes it from any workspace. Pressing `Ctrl+Win+S` again while the scratchpad window itself is focused takes it back out. The stashed window is restored when WorkspaceOS exits, and if the app is killed (an update, a crash) the next launch re-adopts it from `scratchpad.txt` in the config folder, so `Win+S` still brings it back. Override either binding like any other hotkey; set it to an empty string to disable it.

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

Settings ? Advanced shows the live bridge: runtime, workspace chords covered
(`9/9`), total bindings, captured AutoHotkey key presses and the last one with its timestamp.
Only the generated script's `key:` verb increments `captured`; manual `action:` commands
run without changing it.
The same counters are on the control pipe:

From a checkout, the dev helper sends them positionally:

```powershell
& .\publish\live-test\send.ps1 'hotkeys'   # running=… chords=32 workspace=9/9 captured=1 last=…
& .\publish\live-test\send.ps1 'reapply'   # re-assert theme + wallpaper once
```

The generated `%APPDATA%\WorkspaceOS\workspaceos.ahk` ends with a comment for
every binding AutoHotkey cannot own (for example `Alt+Win+S`, which Windows
reserves); those stay on the built-in hook and are not counted as missing
workspace chords. One `Hotkey: <command>` line is logged per captured press, so
a key that reached Explorer instead is visible as a gap.

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

## Shibumi bar form

`Bar.Profile: "Shibumi"` ports the QS Rise V1 tokens from
[Shibumi-Shell](https://github.com/HANCORE-linux/Shibumi-Shell): a 35 px strip
holding a 32 px island (16 px radius, 5 px inset) with 24 px pills (12 px
radius, 9 px padding per side, 5 px gaps).

`Bar.Shibumi` overrides those tokens without touching the other forms:

```json
"Shibumi": {
  "BarHeight": 35,
  "IslandRadius": 16,
  "PillHeight": 24,
  "PillRadius": 12,
  "PillPaddingX": 9,
  "Frost": true,
  "WorkspaceMarker": "Numbers"
}
```

- `Frost` paints the island translucent instead of solid.
- `WorkspaceMarker` switches the workspace chips between `Numbers` and `Dots`.
- `BarHeight` / `IslandRadius` / `PillHeight` / `PillRadius` / `PillPaddingX`
  are clamped to sane ranges so a hand-edited config cannot break the bar.

## Clock and cava

- `Bar.Use24Hour` (default `false`) — the bar shows standard 12-hour time with
  AM/PM. Set it to `true` for 24-hour.
- `Bar.HideCavaWhenSilent` (default `true`) — the cava widget collapses after
  about 200 ms of silence instead of drawing a row of flat dashes, and comes
  back on the first non-silent frame.

`snapshot-menu:<path>` on the `WorkspaceOS.Ctrl` pipe renders the logo menu to a
PNG from inside the app, so the chips can be checked when a screen grab is not
possible (locked session, layered popups).

## Theme wallpapers

Every built-in theme ships its background under
`Assets/wallpapers/<theme>/wallpaper.jpg`: the ten themes that exist upstream
use their Omarchy photo (Matte Black, Vantablack, Rosé Pine, Tokyo Night,
Catppuccin, Gruvbox, Nord, Everforest, Kanagawa, Miasma), and the four themes
this build defines use a matching original (Gold Rush, Harbor, Harbor Dark,
Pokemon). Switching to a theme sets the desktop wallpaper to that image through
the shell's wallpaper service (one call for every monitor and desktop, SPI only as
a fallback), so the desktop follows the bar.

- `Appearance.ApplyThemeWallpaper` (default `true`) controls this. Set it to
  `false` to leave the desktop wallpaper alone.
