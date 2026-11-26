# WorkspaceOS 5.2.1 — the bar stays, and so does autostart

## Fixed

- **The cava visualiser no longer disappears when the music stops.** The bar hid
  the widget after a few silent ticks, so it blinked out during quiet passages and
  stayed gone until something started playing again. The widget now follows only
  its own on/off setting and the bar's width; silence leaves the (flat) bars in
  place. The undocumented `Bar.HideCavaWhenSilent` setting that drove the hiding
  is gone — an old key in `config.json` is ignored.
- **Autostart now survives the copy it points at going away.** The logon entry is
  repaired at every launch when the exe it names no longer exists, so a moved or
  uninstalled copy cannot leave Windows starting nothing. A run from a build
  output (`publish\`) also no longer steals the logon entry from the installed
  copy; only the installed copy under `%LOCALAPPDATA%\Programs\WorkspaceOS` owns
  autostart.
- **A disabled autostart entry is no longer silent.** Windows keeps its own
  enabled/disabled flag beside each Run entry; the app now logs when Windows has
  the entry switched off (Settings → Startup apps), so "it did not start" has a
  reason in `workspaceos.log` instead of no trace at all.
- **The installer clears that disabled flag** when it registers the Run entry, so
  reinstalling re-arms login startup even if the old entry had been switched off
  in Task Manager.
- **Uninstalling no longer leaves `Uninstall.exe` behind.** The self-delete now
  makes a second pass after the installer has exited, instead of failing on its
  own locked file and leaving an empty `Programs\WorkspaceOS` folder.

## Changed

- **The install-from-source script says when an update is available.** Run again
  on a machine that already has WorkspaceOS, it now compares the installed
  version with the latest release: current copies report "up to date", and older
  ones report the version difference and print the exact one-line command that
  installs the update instead of "nothing to do".
- **The script verifies autostart after installing.** It re-registers the logon
  entry for the installed exe, clears any stale disabled flag, reads the entry
  back and prints `Autostart: yes (…)` in the summary.

## Installing

Download **WorkspaceOS-Setup.exe** (per-user, no admin, bundles the AutoHotkey
runtime) or the **portable zip**, or build it on the machine with
`tools/install-from-source.ps1` (no SmartScreen prompt, .NET SDK and cava handled
for you).

The themed taskbar and Explorer glass need [Windhawk](https://windhawk.net) plus
its **Windows 11 Taskbar Styler** and **Windows 11 File Explorer Styler** mods,
installed from Windhawk's own mod list (it has no command-line installer). After
that, one elevated run of `tools\apply-windhawk-taskbar.ps1` — shipped in the
installer and the zip — applies the theme's glass and registers the task that
keeps it following every theme change. Without Windhawk the app is unchanged.
