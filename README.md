# WorkspaceOS

A Linux-style productivity desktop environment layer for **Windows 10/11** — inspired by Hyprland, GNOME Workspaces, i3, Polybar and Waybar.

WorkspaceOS is **not a tiling window manager**. Windows behave completely normally; WorkspaceOS manages *workspaces, window visibility, window assignment and productivity tools* on top of the normal Windows desktop.

![status](https://img.shields.io/badge/platform-Windows%2010%2F11-blue) ![lang](https://img.shields.io/badge/built%20with-.NET%208%20%2B%20Win32-purple)

## Features

- **4 Linux-style workspaces** out of the box (`Win+1..4`, up to 9), with naming, persistence, and window rules
- **Polybar-style top bar** — real Win32 appbar (reserves screen space): workspaces left, `14:37 Thu 9 Jul` clock center, CPU/RAM/GPU/Disk/Net/Battery/Volume right
- **System monitor dashboard** — CPU, memory, GPU/VRAM, disk space & speed, network, top processes
- **Window rules** — auto-assign apps to workspaces by executable, process, title, class or regex
- **Window commands** — `Win+Arrow` nudge, `Win+M` maximize, `Win+Shift+M` restore, `Win+C` center, `Win+F` fullscreen (never tiles anything)
- **Focus Mode** (`Win+F1`) — gamified focus timer with presets, blocked-app detection, failure/success screens, stats and streaks
- **Launcher / command palette** (`Alt+Space`) — apps, files, commands, calculator, settings and workspace actions (`>` prefix)
- **Clipboard manager** (`Win+V`) — history, search, pinning
- **Screenshot tool** (`Win+Shift+S`) — region capture + annotation, saved to `Pictures\WorkspaceOS` and clipboard
- **Everything configurable** — colors, fonts, hotkeys, modules, rules; JSON config with export/import
- Crash-safe: hidden windows are journaled to disk and always recovered

## Install

1. Download **WorkspaceOS-Setup.exe** from the [latest release](../../releases/latest)
2. Run it (per-user install, no admin required)
3. Done — WorkspaceOS starts immediately and on every login

> Silent install: `WorkspaceOS-Setup.exe /S` · Uninstall: Add/Remove Programs, or `Uninstall.exe /uninstall`

## Default hotkeys

| Keys | Action |
|---|---|
| `Win+1..9` | Switch workspace |
| `Win+Shift+1..4` | Send focused window to workspace |
| `Win+Arrow` | Nudge window |
| `Win+M` / `Win+Shift+M` | Maximize / restore |
| `Win+C` | Center window |
| `Win+F` | Borderless fullscreen toggle |
| `Win+F1` | Focus Mode |
| `Alt+Space` | Launcher / command palette |
| `Win+V` | Clipboard history |
| `Win+Shift+S` | Screenshot |

All hotkeys are rebindable in **Settings → Hotkeys**. Combos that Windows reserves are taken over via a low-level keyboard hook, so the defaults above really work.

## Build from source

Requirements: [.NET 8 SDK](https://dot.net), Windows 10/11.

```powershell
.\build.ps1
```

Outputs `publish\WorkspaceOS.exe` (self-contained app) and `publish\WorkspaceOS-Setup.exe` (installer). No other tooling needed — the installer is compiled with the C# compiler that ships with Windows.

## Configuration

Settings live in `%APPDATA%\WorkspaceOS\config.json` and are fully editable through the Settings UI (gear icon on the bar) or by hand. See [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

## Architecture

```
src/WorkspaceOS/
  Core/
    Interop/       Win32 P/Invoke surface
    Hotkeys/       RegisterHotKey + low-level keyboard hook fallback
    WindowSystem/  window enumeration, manageability rules, window commands
    Workspaces/    workspace engine, rules, crash-recovery journal, session persistence
    Metrics/       perf counters, CoreAudio volume, battery, network deltas
    Focus/         focus timer, blocked-app watchdog, history
    Clip/          clipboard listener + history store
  UI/              top bar (appbar), monitor, settings, focus, launcher, clipboard, screenshot
installer/         self-extracting setup (in-box csc)
```

Workspaces are implemented by hiding/showing top-level windows (`ShowWindow`), never by moving or resizing them. Every hidden window handle is journaled to `%APPDATA%\WorkspaceOS\hidden-windows.json`; on startup after a crash all windows are restored. Explorer restarts are handled by re-registering the appbar on `TaskbarCreated`.

## Known limitations

- Memory usage is above the original 50 MB target (~120–250 MB working set) — cost of the self-contained WPF stack; idle CPU is near 0%.
- CPU/GPU temperatures depend on WMI/vendor support and often read *n/a* without vendor SDKs.
- UWP apps (e.g. Settings, Store apps) sometimes re-show themselves; they are re-hidden by the periodic sweep.
- Workspace assignments are restored across restarts by best-effort exe/title matching (Windows gives no stable window identity across sessions).
- The setup exe is unsigned, so SmartScreen may warn on first run (More info → Run anyway).

## License

MIT
