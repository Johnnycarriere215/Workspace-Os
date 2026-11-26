# WorkspaceOS 5.2.2 — it tells you when there is a newer release

## Added

- **The bar and Settings now notice a newer release.** At most twice a day the app
  asks GitHub for the latest release and compares it with its own version. When one
  is newer, a single quiet chip appears in the bar's right dock — `v5.2.2 available`
  — and Settings → Advanced → About shows the same thing with the exact command that
  installs it, a **Copy command** button and a link to the release page.
- **Nothing updates on its own.** This is a notice, not an updater: no download, no
  staging, no silent install. The release number is cached in
  `%APPDATA%\WorkspaceOS\update-check.json` so the bar can show a known answer
  immediately at startup, and the app goes on working exactly as before if GitHub is
  unreachable, rate-limited or the check fails — it simply stays quiet.
- **The check understands real tags.** `v5.2.2`, `5.2.2` and `v5.3.0-rc1` all parse,
  and only a version that is genuinely newer counts, so a prerelease or a re-tagged
  older release never announces itself.

## Changed

- **A build-output copy can no longer take autostart away from the installed one**
  (from 5.2.1): the logon entry is only owned by this copy when it is the installed
  copy, or when the entry it finds points at a file that no longer exists.
- **The update check logs one line** when it finds a release —
  `Update: v5.2.2 available (this copy is v5.2.0).` — so "why is there/isn't there a
  chip" is answerable from `workspaceos.log`.

## Installing

Download **WorkspaceOS-Setup.exe** (per-user, no admin, bundles the AutoHotkey
runtime) or the **portable zip**, or let the app tell you about the next release and
run the command it shows. Building from source —
`tools/install-from-source.ps1` — still needs no admin rights and no SmartScreen
approval, and now reports whether an update is available before doing anything.

The themed taskbar and Explorer glass need [Windhawk](https://windhawk.net) plus
its **Windows 11 Taskbar Styler** and **Windows 11 File Explorer Styler** mods,
installed from Windhawk's own mod list (it has no command-line installer). After
that, one elevated run of `tools\apply-windhawk-taskbar.ps1` — shipped in the
installer and the zip — applies the theme's glass and registers the task that
keeps it following every theme change. Without Windhawk the app is unchanged.
