# WorkspaceOS 5.1.0 — the Shibumi shell, on Windows

## Added

- **The Shibumi shell, ported from its own code.** The bar is now a direct port
  of `hancore.shibumi.bar`'s V1 presentation contract: a 35 px strip, one 32 px
  full-width island inset 5 px with a 16 px radius, and 24 px pills (12 px radius,
  9 px padding per side, 5 px content gap). The appbar reserves 40 px in the
  Shibumi form, leaving a 5 px gap below the island so maximized windows never
  touch the bar. Paper, ink and sumi are mixed with the same formulas as
  `VisualTokens.qml`, so every Omarchy theme reaches the bar through the suite's
  own colour model. The workspace numbers are 24 × 20 px badges with 13 px bold
  active type, and the launcher is the real SHIBUMI wordmark with the fork's
  Omarchy, Hyprland, Arch, OMACOM, Dragon and mark assets in the menu.
- **The suite's real plugin catalogue.** `Assets/shibumi/plugins.json` is
  generated from the fork's `manifest.json` files — ids, roles G1–G18, default
  sections, categories and descriptions. The bar draws the launcher mark then
  workspaces and the left widgets, the clock and cava in the centre, and quick
  access, network, battery, brightness, power and the control centre on the right.
- **Windows backends for each plugin.** CPU, memory, GPU, storage and temperature
  use the metrics service; audio uses Core Audio; network uses Win32 interfaces
  and `netsh`; battery uses `GetSystemPowerStatus`; brightness uses WMI
  `WmiMonitorBrightness`; power profiles use `powercfg`; Bluetooth uses
  `BluetoothFindFirstRadio`. Plugins Windows cannot serve (AI usage, Omarchy's
  update channel, the Wayland media bridge) stay in the catalogue, marked
  unavailable instead of disappearing.
- **Detail panels.** Every pill opens a Shibumi panel anchored 8 px under the
  bar: 12 px radius, 1 px panel border, 94 % paper, optional shadow, one-second
  live refresh, Escape and click-away dismissal. CPU and memory list top
  processes; audio has a capsule slider and mute; network shows the SSID and
  rates; brightness and power profiles are writable; Bluetooth and the control
  centre link into Windows Settings.
- **A Shibumi configuration page.** Bar logo menu → **Shibumi shell** lists every
  plugin with its backend and lets you move it left/centre/right or hide it, and
  set bar height, pill height and radius, island radius, padding, frost, borders,
  shadow and the workspace marker style. Changes apply to every monitor at once.
- **Themes reach the whole PC.** One apply writes the theme's wallpaper once to
  every monitor and every virtual desktop through the shell's `IDesktopWallpaper`
  service, then the Windows accent colour, the light/dark app mode, the bar
  palette and the Windhawk taskbar preset. `SystemParametersInfo` is only the
  fallback, not a second apply. The wallpaper is copied into
  `%LOCALAPPDATA%\WorkspaceOS\wallpapers` so Windows never locks the app's own
  files, and the whole transaction is re-asserted at startup.
- **A native taskbar surface.** On Windows 10 and classic taskbars, the appbar
  surface is set with the theme's paper tint and a Dynamic / Clear / Blur /
  Acrylic mode, re-applied after Explorer restarts. Windows 11 22H2+'s XAML
  taskbar is detected (it ignores that API), and the app writes the matching
  Windhawk `windows-11-taskbar-styler` preset. The bundled
  `tools/apply-windhawk-taskbar.ps1` enables the mod and applies the theme's tint
  with one elevation; `diag` reports `windhawk=enabled`. WorkspaceOS itself does
  not inject a DLL into Explorer.
- **Verifiable hotkeys.** Settings → Advanced and the `hotkeys` pipe command
  report the AHK runtime, the number of bindings, workspace chords covered
  (`9/9`), captured physical key presses and the last one with a timestamp; only
  the script's `key:` verb increments the counter, so manual `action:` commands
  cannot inflate it. The log writes one `Hotkey: key:…` line per real press.

## Changed

- **Tiling is gone.** GlazeWM, its bundled payload, its first-launch prompt, its
  window animator and the fullscreen border workaround are removed from the app,
  the build, the installer, the CI payload and the portable zip.
- **Workspaces are one layer again.** `Win+1..9` move the real Windows virtual
  desktops through the bundled AutoHotkey bridge; the bar reads the active
  desktop from the shell, so it can never disagree with Task View.
- **The bar's default form is Shibumi.** An untouched `Full` bar migrates once on
  update; a form you picked yourself is left alone.
- **Panel surfaces.** The control centre now uses the same 12 px / 94 % paper /
  1 px border surface as the widget panels, with theme cycling, Bluetooth and
  Display shortcuts added.

## Fixed

- **Theme wallpaper was applied twice, and could reach only one desktop.** The
  theme used to set the wallpaper through the shell and then again through
  `SystemParametersInfo`; it is now one shell call for all monitors, verified per
  monitor, with SPI only when the shell service is unavailable.
- **Explorer could win Win+1..9.** The bundled AutoHotkey script owns the nine
  workspace chords (`#UseHook`) and is verified at generation (`9/9`), so the
  taskbar shortcuts no longer fire first; bindings Windows reserves (such as
  `Alt+Win+S`) are listed at the end of the script and stay on the built-in
  hook.
- **The theme could half-apply.** Palette, wallpaper, accent and app mode are one
  operation with separate log lines, so a locked session or a missing wallpaper
  file no longer leaves the desktop on the previous theme silently.
- **Installed copies kept their wallpapers and catalogue.** The theme artwork and
  the Shibumi plugin manifests are embedded in the executable and extracted to
  `%LOCALAPPDATA%\WorkspaceOS\wallpapers` on first use, so the installer and the
  portable zip — which ship one file — no longer arrive without them.
- **Widget panels no longer clip their values.** The panel card's padding and
  border are subtracted from the content width, so "Muted" and "100%" fit.
- **Widgets without hardware stay hidden.** Battery, brightness and Bluetooth
  pills are only built when the machine reports the sensor.

## Hotkeys

- **Workspaces 5–9 can be sent to.** Only `Win+Shift+1..4` shipped with a
  default; the other five now match their switch chords, so all nine workspaces
  work both ways out of the box.
- **Aero Snap has `Win+Arrow` back.** The window nudges no longer claim
  `Win+←/→/↑/↓`, and the 5.1.0 migration clears them from existing configs when
  they are still the untouched default. `Win+Shift+Arrow` keeps moving a window
  between monitors.
- **One chord, one action.** The hotkey manager normalises each chord (modifier
  order and case do not matter) and reports a duplicate in Settings → Hotkeys
  instead of registering it twice — previously the first copy went through
  `RegisterHotKey` and the second through the hook fallback, so both fired. The
  Linux daemon's tiling scratchpad no longer competes with the shell's `Win+S`.
- **The binding table is tested.** `HotkeyDefaultsTests` asserts that every
  workspace is reachable both ways, that no chord is bound twice on either
  platform, that Windows ships no tiling actions, and that the documented chords
  (`Alt+Space`, `Win+Space`, `Ctrl+Win+A`, `Ctrl+Win+Shift+Space`) are the ones
  actually shipped.
- **The docs match the build.** The README and `docs/CONFIGURATION.md` list the
  real defaults — including the theme picker and the audio panel — and spell out
  which Windows shortcuts the shell takes over and how to give them back.

## Removed

- GlazeWM bundling and installation, `TilingWm` settings, the first-run tiling
  question, `Win+Shift+T`, and the `Alt`-based window-management chords that
  belonged to GlazeWM.
