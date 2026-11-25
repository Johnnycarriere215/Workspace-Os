# WorkspaceOS 5.2.0 — glass for File Explorer

## Added

- **File Explorer is themed glass.** Explorer draws its own opaque XAML tree, so
  it is themed through Windhawk's `windows-11-file-explorer-styler`: the
  `WindowGlass` theme with a real `acrylicblur` backdrop, and both of the theme's
  solid background constants replaced by a `WindhawkBlur` in the active theme's
  paper colour — `MainContentBG` (the file list, Home and Gallery background,
  which the theme pins to an opaque `SystemChromeAltHighColor`) and `Background`
  (menus and command-bar flyouts). The wallpaper shows blurred through the title
  bar, command bar, sidebar and file list, and the tint follows every theme change
  without restarting the Windhawk engine. Verified across all fourteen themes.
- **The Explorer tint is deliberately a little more solid than the taskbar's**
  (tint opacity 0.50 vs 0.30): a whole window of text over a bright wallpaper
  needs more separation than a 35 px bar.

## Changed

- **One apply per theme change.** The taskbar and Explorer presets are staged and
  applied together in a single elevated run, so a theme change costs one task run
  instead of two.
- **One shared preset store.** `WindhawkPreset` and `ExplorerGlass` now describe
  only what their preset *is* (settings and YAML); staging, registry JSON,
  comparison, elevation and the scheduled task live in `WindhawkPresetStore`, and
  `WindhawkGlass.Sync` is the single entry point a theme change calls. `ExplorerGlass`
  went from 225 to 126 lines.
- **`docs/CONFIGURATION.md` and the README** describe the Explorer glass, the
  ordered write, and the fact that both styler mods are installed from Windhawk's
  own mod list.

## Fixed

- **`explorer.exe - System Error` could appear when a theme was applied over open
  Explorer windows.** The Windows event log showed a one-off
  `Exception Processing Message 0xc0000005 - Unexpected parameters` in the same
  second the settings were written. Cause: on a settings change the Explorer
  styler tears down and re-initialises its XAML hooks inside every open window,
  and the preset writer deleted the old style entries *before* writing the new
  ones, so the mod could read a half-written set and rebuild against a missing
  style constant. New values are now written first, stale entries pruned
  afterwards, and `SettingsChangeTime` is bumped last, in both the app and the
  script. Cycle-tested across a full lap of 14 themes with an Explorer window
  open: no further popups.
- **The Windhawk glass script was never shipped.** The installer and portable zip
  contained only the exe and the AutoHotkey runtime, yet the README tells users to
  run the bundled `tools\apply-windhawk-taskbar.ps1`. It is now embedded in the
  installer (extracted to `tools\`) and included in the portable zip.
- **The script hardcoded this developer's Windhawk build number**
  (`windows-11-taskbar-styler_1.11.2_593924.dll`), which exists on one machine
  only. It now keeps the compiled library Windhawk already registered when that
  file is present, falls back to the newest compiled library otherwise, and skips
  a mod that is not installed instead of writing a broken entry.
- **The installer could name the wrong version.** Its version string and dialogs
  were hardcoded ("5.1.0"); the version is now generated from
  `src/WorkspaceOS/WorkspaceOS.csproj` during the build, so a release cannot ship
  a setup that advertises another version.
- **The installer's closing dialog** offered `Win+1..4`; all nine workspaces are
  supported and it now says so.
- **Rapid theme switching could leave the glass a theme behind.** The scheduled
  task now runs with `-MultipleInstances Queue` instead of dropping a run that
  arrives while the previous one is still applying.

## Installing

Download **WorkspaceOS-Setup.exe** (per-user, no admin, bundles the AutoHotkey
runtime) or the **portable zip**.

The themed taskbar and Explorer glass need [Windhawk](https://windhawk.net) plus
its **Windows 11 Taskbar Styler** and **Windows 11 File Explorer Styler** mods,
installed from Windhawk's own mod list (it has no command-line installer). After
that, one elevated run of `tools\apply-windhawk-taskbar.ps1` — shipped in the
installer and the zip — applies the theme's glass and registers the task that
keeps it following every theme change. Without Windhawk the app is unchanged.
