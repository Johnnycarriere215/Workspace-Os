# WorkspaceOS 4.7.1 — the scratchpad survives restarts

## Fixed

- **A restart no longer empties the scratchpad.** The stashed window's handle is
  now written to `scratchpad.txt` in the config folder and re-adopted at launch,
  so an update or crash leaves `Win+S` able to bring the window back instead of
  starting with an empty pad. Recovery only trusts the record when the same
  process still owns that handle, never a recycled one, and it restores the pin
  so releasing the window still unpins it.
- **Updates restore the window instead of stranding it.** Setup now sends `exit`
  over the command pipe before falling back to `Kill()`, so a clean shutdown puts
  the stashed window back on the desktop. Previously it was left hidden while its
  audio kept playing.

## Notes

- `Win+S` also retries recovery when the in-memory pad is empty, which covers a
  state file written after launch.

# WorkspaceOS 4.7.0 — Snipping Tool gets its shortcut back

## Changed

- **The in-app screenshot tool is removed.** It owned `Win+Shift+S`, which meant
  Windows' own Snipping Tool never opened. That chord is native again, and the
  existing `Screenshot` hotkey is deleted from your config on first launch so the
  bundled AutoHotkey runtime stops swallowing it. The launcher entry is gone too.
- **Download 4K wallpaper needs a theme.** With a custom or wallpaper-derived
  palette selected it used to fall back to the first preset and quietly export
  Neon Void art; it now asks you to pick a theme, matching *Set as desktop*.

## Notes

- Every remaining reader of `Appearance.Theme` treats `Custom` as "no preset":
  the gallery derives its state from the colours and only a preset tile lights up
  when the nine colours actually equal one.

# WorkspaceOS 4.6.1 — custom palettes and photo credits

## Fixed

- Saving a wallpaper-derived or hand-edited palette now writes it as **Custom**
  instead of keeping the previously selected theme in your config. Reopening
  Settings highlights a new **Custom** tile (with a preview of your colours) and
  no preset, so the gallery no longer claims a theme you are not using.
- Editing a single colour of a selected preset also turns it into Custom on save.

## Added

- Photographer credits on the Unsplash photo cards: each card links to the
  photographer's profile — Bailey Zindel, Sebastian Unrau, NASA and Matt Hardy
  are identified; the rest link to Unsplash.

# WorkspaceOS 4.6.0 — wallpaper-aware theming

## Added

- **Set as desktop wallpaper.** The Appearance page can now put a theme's
  generated 4K art straight onto the Windows desktop (fill mode), no file
  picker needed. The PNG is cached under
  `%LOCALAPPDATA%\WorkspaceOS\wallpapers`.
- **Real 4K photos.** A new *Photo wallpapers* row pulls eight free Unsplash
  photos at 3840 px wide, each with **Save** (to `Pictures\WorkspaceOS`) and
  **Set** (desktop wallpaper). Photos are free under the Unsplash License and
  load on demand, so no image data ships with the app.
- **Auto-theme from your wallpaper.** *Theme from current wallpaper* reads the
  image Windows is using (or any picture you choose) and derives the whole bar
  palette from it: darkest hue for the surface, most vivid hue for the active
  slot. A **Match bar colours to the wallpaper** option does the same
automatically whenever a wallpaper is set from the app.
- **Four more themes:** **Deep Ocean**, **Sakura Bloom**, **Copper Dusk** and
  **Monochrome**, each with its own generated 4K wallpaper — ten in total.

## Fixed

- `Set as desktop wallpaper` no longer falls back to a preset and overwrites an
  auto-derived palette when the bar is matched to an image; it re-applies that
  same image instead.

## Install

Download `WorkspaceOS-Setup.exe` (or the portable zip) and run it. It upgrades
over 4.5.0 and keeps your config.

# WorkspaceOS 4.5.0 — built-in themes with 4K wallpapers

## Added

- **Six built-in themes**, each a full bar palette: **Neon Void** (GD cyan),
  **Midnight Synth** (retrowave pink), **Toxic Bloom** (acid green),
  **Solar Flare** (amber), **Frost Byte** (ice cyan) and **Blood Moon**
  (crimson). Clicking a theme tile fills all nine colour fields, so the bar can
  be previewed in the editor before you save it.
- **A matching 4K wallpaper for every theme.** Settings → Appearance has a
  **Download 4K wallpaper** button that renders the selected theme at
  **3840 × 2160** and saves it (defaults to `Pictures\WorkspaceOS`). The art is
  original vector work drawn by the app: true 4K detail, no download, no
  third-party image licence.
- Theme tiles show a live wallpaper thumbnail, and the active theme is
  outlined in cyan. The chosen theme name is stored as `Appearance.Theme`.

## Fixed

- Tab content is exposed to accessibility tools again: the custom tab template
  was missing `PART_SelectedContentHost`, which also broke keyboard traversal
  into the page body.

## Install

Download `WorkspaceOS-Setup.exe` (or the portable zip) and run it. It upgrades
over 4.4.1 and keeps your config.

# WorkspaceOS 4.4.1 — visual polish

## Fixed

- **All seven Settings tabs fit.** At the old width the pill strip clipped the
  `Advanced` tab; the window is wider and the tabs are slightly tighter.
- **No more clipped labels or values.** The font-family field, the bar `Modules`
  field, the custom ship path and the AutoHotkey path are wide enough, and the
  over-long labels (`Custom image`, `AutoHotkey64 path`) were shortened.
- **Scrolled content no longer touches the tab strip.** The page body starts a
  few pixels below the tab hairline.
- **Hints have breathing room.** The Workspaces and Focus Mode notes are now
  spaced, smaller grey hints instead of text pressed against the field above.
- **Rules tab empty state.** An empty rules list explains itself, and the
  `+ Add rule` button moved up under the section header instead of floating at
  the bottom of the card.
- **Sharper bar ship.** The built-in marks use a crisp translucent neon rim
  instead of a blurred drop shadow, which turned to mush at 30 px, and the
  built-in fish was redrawn so its tail no longer scribbles across the body.
- **Smoother custom icons.** Community PNG/JPG icons are scaled with
  high-quality sampling instead of nearest-neighbour blocks.
- **Readable icon preview.** The Appearance preview is a fixed 3× box now, so
  the ship (or your own image) can actually be judged before saving.
- **Clean text on the dark surface.** Settings renders text in grayscale
  antialiasing, removing the orange/cyan fringes around labels at 125% DPI.

## Changed

- The bar height slider covers the full 20–80 px range the config always
  accepted, so an existing 80 px bar is no longer silently shrunk on save.

## Install

Download `WorkspaceOS-Setup.exe` (or the portable zip) and run it. It upgrades
over 4.4.0 and keeps your config.

# WorkspaceOS 4.4.0 — bar cosmetics studio

## Added

- **Every bar colour is editable.** Settings → Appearance gives each colour a
  live swatch plus a hex field: bar background, bar text, active and inactive
  workspace colours, module label, module background, module border and the
  separator hairline. Click a swatch for the Windows colour picker; the alpha
  channel is preserved so translucent surfaces stay translucent.
- **Bar size slider.** The bar's height is now a 20–72 px slider instead of a
  number box. The strip re-reserves that many pixels on every monitor as soon
  as you save, and maximized windows follow.
- **Settings gear on the bar.** A new gear chip at the right end of the bar
  opens Settings directly, next to the display and task-manager chips.
- **GD-style ships for the left of the bar.** Eight original vector ships —
  `GD Fish`, `Comet`, `Viper`, `Nova`, `Prism`, `Warden`, `Specter`, `Ember` —
  pick up the bar's accent, ink and border colours and scale with a 12–64 px
  icon-size slider.
- **Custom ship image.** Point the icon at any PNG/JPG/GIF/BMP you own (for
  example a community-shop icon) and it replaces the built-in ship. A missing
  or unreadable file falls back to the selected ship instead of leaving a gap.
- **Redesigned Settings window.** Card layout, pill tab strip, section headers
  with hairline rules, a header mark and new cyan sliders. Wider default size
  and a minimum size so the layout no longer clips.

## Changed

- Module chips honour `ModuleBackground` / `ModuleBorder`, so the two new
  appearance keys actually restyle the CPU/RAM/GPU pills.

## Fixed

- A tall bar icon can no longer overflow a short bar: the mark shrinks to fit
  the reserved strip while keeping its aspect ratio.
- Narrow (compact) bars no longer rebuild their module chips on every refresh,
  which had been resetting the eased CPU/RAM/network values twice a second.

## Install

Download `WorkspaceOS-Setup.exe` (or the portable zip) and run it. It upgrades
over 4.3.1 and keeps your config. The portable zip contains the app and its
bundled AutoHotkey runtime.

# WorkspaceOS 4.3.1 — fullscreen browser video

## Fixed

- **Fullscreen YouTube and other browser video now hide the bar.** Chrome and
  Edge can keep a browser window in its maximized state while YouTube's HTML5
  player takes over the screen. The fullscreen detector now checks the native
  window frame as well as the geometry, so that frameless fullscreen window is
  recognized even when the browser still reports itself as zoomed.
- Every bar hides and releases its 32px Win32 appbar reservation for fullscreen
  browser video, exactly as it already did for games. The bar and reservation
  return when the video exits fullscreen or focus moves away.
- Normal maximized windows still keep the bar: a window with a native caption
  or sizing frame is not mistaken for fullscreen.

## Install

Download `WorkspaceOS-Setup.exe` (or the portable zip) and run it. It upgrades
over 4.3.0 and keeps your config. The portable zip contains the app and its
bundled AutoHotkey runtime.

# WorkspaceOS 4.3.0 — scratchpad + acrylic bar

## Added

- **Scratchpad.** `Ctrl+Win+S` stashes the focused window in a single
  scratchpad slot and `Win+S` opens or closes it from any workspace. Pressing
  `Ctrl+Win+S` while the scratchpad window itself is focused takes it back out.
  The stashed window is pinned across native desktops while hidden and is
  restored when WorkspaceOS exits. Both bindings are rebindable in
  **Settings → Hotkeys**.
- **Acrylic glass bar surface.** The bar uses Windows acrylic blur with a
  translucent fallback, so the wallpaper and the windows below show through
  instead of the bar sitting on an opaque black strip.
- **Faster focus-follows-mouse.** The hover dwell is now 120 ms, and switching
  workspaces immediately focuses the window under the pointer, so the first
  `Ctrl+T` or keystroke reaches the app you just revealed. This release keeps
  the game/drag/bar/taskbar exclusions from 4.2.0.
- **New README previews.** The hero animation mirrors the live bar: the cyan
  active-slot pill slides, the clock fades at the next minute, and CPU/RAM/GPU
  values ease toward their next readings. The Win+W demo uses the same theme.

## Changed

- The bar clock uses 12-hour `h:mm tt` formatting (for example `2:37 PM`),
  matching the Windows taskbar.
- Release builds stay **self-contained x64**: no admin, no .NET runtime and no
  separate AutoHotkey install. Windows 11 ARM64 can run the x64 build through
  x64 emulation. 32-bit Windows is refused by the installer with a clear
  message.

## Install

Download `WorkspaceOS-Setup.exe` (or the portable zip) and run it. It upgrades
over 4.2.0 and keeps your config. The portable zip contains the app and its
bundled AutoHotkey runtime.

# WorkspaceOS 4.2.0 — game-safe fullscreen bar

## Added

- **The bar gets out of the way of fullscreen games.** When a game (or any
  fullscreen/borderless app) owns the screen, every WorkspaceOS bar hides and
  releases its 32px Win32 appbar reservation, so the game gets the whole
  monitor and the taskbar area back. When the game closes or is alt-tabbed
  away, the bar and its reservation return automatically.
- **Settings → Advanced → “Hide the bar and release its screen space while a
  fullscreen game or app is open”** controls the behavior; `General.HideBarInFullscreen`
  in `config.json` is the same setting and defaults to `true`.
- Detection uses the shell's `ABN_FULLSCREENAPP` notification as the fast path
  plus a 300ms `SHQueryUserNotificationState` / foreground-window check for
  borderless games. WorkspaceOS never modifies the game window, its mode,
  borders or resolution, and it never activates or focuses a window.
- **Focus follows mouse.** Rest the pointer on a normal app window for 250 ms
  and it receives focus without a click, so switching a desktop and then typing
  goes to the window under the mouse instead of the last-focused window. It
  ignores fullscreen games, mouse drags, the bar, taskbar, desktop, menus, tool
  windows and cloaked windows. `General.FocusWindowOnHover` (default `true`) and
  **Settings → Advanced** control it.
- The installer refuses 32-bit Windows with a clear message and documents the
  64-bit Windows 10/11 requirement up front.

## Fixed

- Normal maximized windows are not mistaken for games: a maximized window is
  detected as zoomed and keeps the bar visible. Shell windows, hidden/minimized
  windows and WorkspaceOS's own windows are ignored.

# WorkspaceOS 4.1.1 — maximize new windows, then leave them alone

## Added

- **Normal app windows open maximized once.** The window fills the work area on
  open, then behaves like an ordinary Windows window: restore, drag, resize and
  move it freely. There is no tiling, layout lock or re-adoption.
- **Settings → Advanced → "Maximize new windows when they open"** turns the
  behavior off without affecting workspace switching or other commands.
- Fullscreen and borderless games, dialogs, tool windows, fixed-size windows and
  already-maximized windows are skipped, so they keep their own presentation.

## Fixed

- Windows that already existed at startup are never maximized by the new service,
  including when a virtual-desktop switch re-shows them.
- Restored windows stay restored and keep their exact geometry when switching
  desktops.

# WorkspaceOS 4.1.0 — no tiling, monitor moves + native Start on Win+Space

## Changed

- **Tiling has been removed from the Windows app.** WorkspaceOS no longer tiles,
  resizes or changes the borders of windows automatically. This fixes games such
  as Geometry Dash opening in a bordered, pixelated window instead of their own
  chosen mode. Old `Tile*` / `ToggleTiling` hotkeys and the `Tiling` config
  section are removed automatically.
- Existing window commands and workspace switching remain; windows behave like
  normal Windows windows.

## Added

- **Move the focused window to another monitor with `Win+Shift+Arrow`.**
  Left/right/up/down picks the nearest monitor in that direction. The window
  keeps its relative position and size; no layout or tiling state is involved.
- **`Win+Space` now opens the native Windows Start menu**, exactly like tapping
  Win alone. `Alt+Space` remains the WorkspaceOS launcher.

## Fixed

- The top bar clock fits the laptop screen without colliding with the CPU/RAM
  module row: compact modules engage earlier and the clock centers only when
  there is genuinely clear space.
- Launcher results render correctly (bound properties instead of fields), and
  `Win+Space` no longer leaves the Start menu fighting the launcher for focus.

## Install

- `WorkspaceOS-Setup.exe` is a per-user, self-contained x64 install: no admin,
  no .NET runtime, no separate AutoHotkey install. The portable zip contains
  the app and bundled AutoHotkey runtime.
- Existing configs migrate automatically; settings are preserved.

The v4.0.1 notes below are kept for history.

# WorkspaceOS 4.0.1 — native Linux .deb with the v4 fixes

The v4.0.0 tag was cut from the Windows-only branch, so its `.deb` was still the
old Wine wrapper. This release is built from `main`, where the v3.x **native
Linux daemon** (no Wine, no .NET runtime, automatic keybinding registration on
Cinnamon/MATE/xbindkeys) is merged with every v4 fix:

- Windows 11 support on every build (21H2 → 25H2) — per-build virtual-desktop
  COM binding, fixing "switching workspaces does nothing"
- Hyprland-style tiling **on by default** on Windows and Linux
- Geometry Dash-themed top bar and Settings window: void-black surface,
  electric-cyan glow, GD fish + spike marks, cyan active workspace, matching panels
- Linux daemon: `workspaceos start | status | keys | retile | action <Name>`,
  systemd user autostart, keymap applied live during `apt install`

Windows assets in this build also include the v4.0.1 fixes: hotkeys can no
longer abort startup before the bar exists, tiled windows respect the bar's
reserved strip, AutoHotkey bindings are de-duplicated and log their errors,
and CPU load falls back to kernel timings when performance counters are
unavailable.

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

# WorkspaceOS 3.0.1 — Launch fix for the Linux .deb

## Fixed
- **The menu entry now actually launches WorkspaceOS.** In 3.0.0 it invoked
  `workspaceos retile`, which only talks to an *already-running* daemon — if none
  was up (e.g. the systemd unit never received a DISPLAY), the entry exited
  silently. The entry (and the XDG autostart file) now run the new
  `workspaceos start` command: idempotent, detached, no terminal, and it works
  whether or not systemd user units are available.
- **The systemd user unit no longer stalls without a DISPLAY**: it defaults to
  `:0` (the X11 default on Mint/Ubuntu) and the daemon itself now waits
  indefinitely for a display instead of giving up after 60 s.
- **Installing now starts the daemon immediately** for every logged-in session
  (postinst ran only `enable` before, so WorkspaceOS only came up after the next
  logout/login).

# WorkspaceOS 3.0.0 — Native Linux (no Wine) with automatic keybindings

The `.deb` is no longer a Wine wrapper. WorkspaceOS now runs **natively on Linux** as a
daemon built from the same Dwindle layout core and the same `config.json` as the Windows
build — and on Linux, the keybindings configure themselves.

## Added
- **Native Linux daemon** (`workspaceos_<version>_amd64.deb`, amd64, no Wine, no .NET
  runtime dependency — self-contained single file):
  - Same **Dwindle BSP tiling core** as Windows (`LayoutTree` compiled unchanged): gaps,
    smart gaps, pseudotile, floating, preselection, directional focus/move/resize,
    togglesplit, per-workspace trees, tiling rules.
  - Same **config model and file**: reads/writes `~/.config/WorkspaceOS/config.json`
    (the XDG counterpart of `%APPDATA%\WorkspaceOS\config.json`), including every
    `Hotkeys` binding, workspace and tiling setting.
  - Drives the session through standard X11 tooling (`wmctrl`, `xdotool`, `xprop`,
    `xwininfo`) — installed automatically as deb dependencies.
  - Window commands: close (WM_DELETE, the polite path), maximize/restore, center,
    fullscreen, workspace send with follow.
  - Rule-based auto-assignment of windows to workspaces, same rule model as Windows.
  - CLI: `workspaceos status | keys | retile | action <Name>` over a unix socket
    (`$XDG_RUNTIME_DIR/workspaceos.sock`); the IPC verb surface matches the Windows
    named-pipe protocol (`ws:3`, `send:2`, `focus:left`, `action CloseWindow`, …).
  - Autostart via a systemd **user** unit and XDG autostart; single instance;
    config.json changes are picked up live.
- **Automatic keybinding registration on Linux** — the headline feature:
  - **Cinnamon (Linux Mint):** every binding is registered as a Cinnamon custom
    keybinding via gsettings, running `workspaceos action <Name>`. Only WorkspaceOS's
    own entries are managed; user keybindings are never touched. Re-applied
    idempotently on every daemon start and config change.
  - **MATE:** mapped onto Marco's `run-command-1..12`.
  - **Anything else:** a generated `xbindkeys` config (`~/.config/WorkspaceOS/xbindkeysrc`).
  - The postinst applies the keymap to logged-in sessions **during `apt install`** —
    `Win+1..9`, `Win+W`, `Win+H/J/K/L`, … work immediately, no relog, no manual
    binding editor, and no AutoHotkey-style hook is needed anywhere.
- New unit tests for the Linux keymap translator (combo → X11 keysym strings,
  including rejection of unbindable combos and gsettings-quoting safety); the whole
  suite now runs on Linux CI as well.
- The release workflow builds the deb on ubuntu-latest, runs the tests, smoke-tests the
  daemon against a real Xvfb display, and verifies the package payload before publish.

## Changed
- **The Wine-wrapped `.deb` is gone** (it was experimental and evaluation-only).
  Native Windows 10/11 remains fully supported and unchanged: Setup.exe, portable zip
  and the AutoHotkey bridge all ship exactly as before — including the bundled
  AutoHotkey runtime, which still requires **no manual install** on Windows.
- Version bumped to 3.0.0 to mark the packaging change.

## Linux notes
- Works on X11 sessions (Cinnamon, MATE, XFCE, other EWMH WMs). On Wayland, Xwayland
  windows are managed; native Wayland capture is future work.
- Tiling on Linux is **opt-in** like on Windows: `workspaceos action ToggleTiling`.
- Multi-head: the engine currently uses the root `_NET_WORKAREA` (single work area);
  per-monitor trees on Linux are planned.

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
