# WorkspaceOS 4.0.0 — Windows 11 everywhere + tiling by default

## Fixed

- **Workspaces work on every Windows 11 build (21H2, 22H2, 23H2, 24H2, 25H2).**
  This is the headline fix: on Windows 11, switching workspaces did nothing —
  windows stayed put and the bar never updated. The cause was in the shell COM
  interop, which hardcoded the **Windows 10-era interface GUIDs and method
  layouts** for the immersive shell's virtual-desktop services:
  - Windows 11 changed the `IVirtualDesktop` and `IVirtualDesktopManagerInternal`
    GUIDs (the Win10 GUIDs simply don't exist on Win11), so every call failed.
  - Windows 11 24H2 changed the manager's **method order** again *without
    changing its GUID* — calling `CreateDesktop` with the old layout actually
    invoked `SwitchDesktopAndMoveForegroundView`, switching desktops and
    dragging the foreground window along.
  WorkspaceOS now detects the OS build and binds the exact interface set for
  **Windows 10, Windows 11 pre-24H2, and Windows 11 24H2/25H2**, probing each
  candidate with a safe sanity call and logging which one connected
  (`win10` / `win11` / `win11-24h2` in `%APPDATA%\WorkspaceOS\workspaceos.log`).
- The **key-simulation fallback stays available** for an unknown future
  Windows build that ships yet another interface change: switching walks with
  Ctrl+Win+Arrow and the bar tracks the active index, so the product keeps
  working (without absolute jumps) until the interfaces are re-learned.
- Window pinning ("show on all desktops"), send-to-workspace and the bar's
  occupancy dots all flow through the same fixed interfaces, so they now work
  on Windows 11 too.

## Added

- **Hyprland-style tiling is now ON by default.** New windows automatically
  tile Dwindle-style (split the focused window, orientation from its
  geometry), with all the existing controls: `Win+H/J/K/L` focus,
  `Win+Shift+H/J/K/L` move, `Win+Ctrl+H/J/K/L` resize, `Win+Shift+Space`
  float, `Win+P` togglesplit, `Win+Shift+Arrow` preselect. Don't want it?
  `Win+Shift+T` turns tiling off (or Settings → Tiling) and Windows behaves
  normally again.
- One-time config migration: existing 2.x configs get tiling enabled and the
  gold indicator exactly once (tracked by `Tiling.SchemaVersion`), so a later
  deliberate opt-out is never reverted.

## Changed

- **Pokémon cosmetic restyle of the top bar and Settings window** (visuals
  only — no behavior changes), matching the Omarchy *pokemon* theme:
  - Workspace buttons in a **Pokéball split-border** cluster (red/white),
    active workspace glowing **electric gold** with occupied/empty states.
  - Clock in glowing gold like the theme's `#clock` module; calendar popover
    with the signature **fire/frost split border**.
  - System modules rendered as rounded **Pokémon-type-colored pills** — CPU
    electric yellow, network fire/ice, GPU Mewtwo blue, disk grass green,
    battery water teal (gold charging, orange low, red critical), volume
    fairy pink — with the monitor/settings buttons as type-colored chips.
  - Settings window: gold selected-tab highlight with fire border, ember
    hover on buttons, fire/frost bordered panel card, red/gold Apply button.

## Upgrade notes

- Install over the top of any 2.x version (the installer stops the old one).
- If you had tiling deliberately disabled: v4 enables it once during the first
  start after upgrading. Press `Win+Shift+T` to turn it back off — it stays off.
- To verify the fix on Windows 11: after starting, the log should contain
  `Virtual desktop API connected (win11…, N desktops)` and `Win+2` should
  actually switch desktops (bar button highlight follows).

# WorkspaceOS 2.0.4 — Close window hotkey + Linux setup guide

## Added
- **`Win+W` closes the focused window** (`CloseWindow` action). It sends `WM_CLOSE` — the same
  message as clicking the title-bar X — so apps get their normal save/exit path and unsaved
  work is never silently discarded. Windows without a close button (no `WS_SYSMENU`, e.g.
  dialogs and shell furniture) are ignored. Rebindable in Settings → Hotkeys like every
  other action.

## Changed
- README gained a full **step-by-step setup tutorial**: Windows install + AutoHotkey setup
  (bundled, portable, and custom-path routes, verification, security-software notes) and a
  dedicated **Linux Mint guide** for the Wine-wrapped `.deb` (Wine install, prefix, menu
  entry, what works / what doesn't under Wine).
- The `.deb` now ships a **menu entry** (`workspaceos.desktop`), so on Linux Mint it can be
  launched from the application menu instead of a terminal.

# WorkspaceOS 2.0.3 — Icon integrity

## Fixed
- **Bar icons always render**: JetBrainsMono Nerd Font is now **embedded in the executable**
  (`pack://application` font). The monitor/settings glyphs and the clock no longer depend on a
  Nerd Font being installed on the machine — no more tofu boxes on stock Windows.
- Audited every glyph used by the bar and calendar popover against the embedded font's coverage.

# WorkspaceOS 2.0.2 — Quickshell parity (summary)
- Top bar restyled to match the user's custom Quickshell bar: same MDI icon vocabulary,
  glow+dot workspace indicators, calendar popover on clock click, panel-style Settings window,
  palette-driven app-wide theme.

# WorkspaceOS 2.0.1 — Pokémon top bar (summary)
- Bar and app palette matched to the live Omarchy theme (navy/cream/steel, Charizard accent).

# WorkspaceOS 2.0.0 — "Dwindle"

The Hyprland-inspired release. WorkspaceOS grows from a workspace layer into a
real tiling window manager for Windows 10/11 — while keeping every existing
feature (top bar, focus mode, launcher, clipboard, screenshots).

## Highlights

### Win+1..9 now switch workspaces — reliably
- Global hotkeys are captured by a **bundled AutoHotkey v2 runtime** and sent to
  WorkspaceOS over a named pipe — before the Windows shell can turn `Win+3`
  into "open the third pinned taskbar app". No taskbar functionality is
  modified or disabled; the hotkeys are simply intercepted first.
- The runtime ships inside the installer and portable zip — nothing to install.
- The script is generated from your WorkspaceOS hotkey config (including extra
  workspaces 5–9), restarts if it crashes, is killed on app exit, and falls
  back to WorkspaceOS's built-in hook when AutoHotkey is unavailable.
- `Win+Shift+1..9` send the focused window to a workspace through the same
  bridge, with a configurable "follow moved window" behavior.

### A real tiling window manager (Hyprland Dwindle-style)
- **BSP layout tree**: every workspace on every monitor keeps its own binary
  split tree — splits, ratios, orientation, pseudotile flags.
- **Dynamic (Dwindle) splitting**: new windows split the focused window; the
  orientation comes from the *window's* geometry (wide → side-by-side,
  tall → stacked), optionally locked with **preserve_split**.
- **Smart split**: splits where the cursor enters the window.
- **Manual preselect** (`Win+Shift+Arrow`): choose where the next window lands;
  one-shot by default, persistent optionally.
- **Directional focus** `Win+H/J/K/L` — geometric nearest-neighbor search, not
  list order; works on any BSP shape. Optional wrap.
- **Directional move/swap** `Win+Shift+H/J/K/L` — updates the tree, not just
  the rectangle; falls back to toggling the split at edges (Hyprland behavior).
- **Keyboard resize** `Win+Ctrl+H/J/K/L` — adjusts split ratios with a
  configurable step (default 5%), clamped to sane minimums.
- **Floating windows** `Win+Shift+Space` — float/tile toggle; floaters keep
  their rect, stay inside the work area, never join the tree.
- **Pseudotile** `Win+Shift+P` — keep a preferred size centered in the slot.
- **togglesplit** `Win+P` — flip the split under the focused window.
- **Scratchpad** `Win+S` (optional) — hide/show a window as an overlay.
- **Gaps & indicator** — inner/outer gaps, smart gaps, active-window accent
  border (Windows 11 DWM border color; silently no-op on Windows 10).
- **Single window handling** — full work area by default; optional centered
  single-window mode with a max width.
- **Window rules** — float / tile / ignore per executable, process, class,
  title or regex; dialogs and UWP helpers are detected automatically.
- **Multi-monitor** — per-monitor trees, correct per-monitor work areas
  (taskbar and the WorkspaceOS bar reserved via the appbar API), DPI-aware,
  monitor hotplug resync.
- **Fullscreen-aware** — maximized/fullscreen windows are left alone; games
  are not retiled behind your back.

### Everything else
- Settings → **Tiling** tab: general, dwindle, floating, appearance, behavior,
  tiling rules, AutoHotkey status, debug logging.
- Debug dump of trees/areas/events via config (`TilingDebug`) and the log.
- `ToggleTiling` action (`Win+Shift+T`) for instant on/off.
- New tests for the layout core; CI on every push; automated release builds.

## Install

1. Download **WorkspaceOS-Setup.exe** (or the portable zip) from the release.
2. Run it — the AutoHotkey runtime is bundled; no admin, nothing else needed.
3. Tiling is **opt-in**: open Settings → Tiling → "Enable tiling window
   manager". Existing behavior is unchanged until you do.

### Upgrading from 1.x
- Install over the top (the installer stops the old version). Settings are
  preserved; new tiling keys get defaults on first save.
- If you pinned WorkspaceOS hotkeys yourself, check Settings → Hotkeys: the
  new tiling defaults (Win+H/J/K/L etc.) may collide with your custom binds —
  conflicts are flagged in the Hotkeys tab warnings.

## Known limitations (honest list)
- Global hotkey capture uses AutoHotkey's driver-level hook; some security
  software blocks low-level hooks. The C# hook fallback then handles what
  Windows allows (Win+number stays with the taskbar in that case).
- The active-window indicator needs Windows 11 (DWM border color attribute);
  on Windows 10 it is a no-op.
- Elevation: an elevated app's window can only be managed by an elevated
  WorkspaceOS. Tiling skips what it cannot move and logs it.
- The .deb package is an evaluation wrapper: it runs the Windows build under
  Wine. Wine lacks global hotkey capture and virtual-desktop COM, so on Linux
  you get the tiling engine with its keyboard-hook fallback, not the full
  native experience. Native Windows 10/11 is the supported platform.
