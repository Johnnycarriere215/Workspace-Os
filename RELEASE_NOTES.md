# WorkspaceOS 5.2.3 — an update now starts with one button, not a compiler

## Changed

- **The update notice leads with the installer.** When a newer release exists,
  Settings → Advanced → About now shows a **Download WorkspaceOS-Setup.exe** button
  pinned to that release, with the **Release page** next to it. One click starts the
  download; the installer replaces this copy for your user, keeps your settings and
  needs no administrator rights. Nothing is downloaded or installed until you click.
- **Building from source is now the fallback, and it says so.** The command that
  rebuilds from source and reinstalls is still there — clearly labelled
  "Prefer to build from source? It needs the .NET 8 SDK and a few minutes of
  compiling" — with **Copy install command**. It is for anyone who would rather not
  run a downloaded installer, not the default path.
- **`tools/install-from-source.ps1`** now prints the release's installer URL first
  when it finds a newer version, and the rebuild command second.
- **The bar's update chip tooltip is accurate.** The chip still only opens Settings;
  it now says so, instead of promising a download it never started.

## Still true from 5.2.2

- The notice itself is unchanged in spirit: at most one GitHub API call twice a day,
  cached in `%APPDATA%\WorkspaceOS\update-check.json`, silent when GitHub is
  unreachable, and it never updates anything on its own.
- A build-output copy still cannot take the logon entry away from the installed
  copy, and the check logs one line — `Update: v5.2.3 available (this copy is
  v5.2.0).` — when it finds a release.

## Installing

Download **WorkspaceOS-Setup.exe** (per-user, no admin, bundles the AutoHotkey
runtime) or the **portable zip**, or let the app tell you about the next release and
click the button it shows. The installer is unsigned, so Windows SmartScreen asks
once: **More info → Run anyway**. Building from source —
`tools/install-from-source.ps1` — skips that prompt entirely and needs no admin
rights, but it compiles, so it takes a few minutes.

The themed taskbar and Explorer glass need [Windhawk](https://windhawk.net) plus
its **Windows 11 Taskbar Styler** and **Windows 11 File Explorer Styler** mods,
installed from Windhawk's own mod list (it has no command-line installer). After
that, one elevated run of `tools\apply-windhawk-taskbar.ps1` — shipped in the
installer and the zip — applies the theme's glass and registers the task that
keeps it following every theme change. Without Windhawk the app is unchanged.
