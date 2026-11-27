# WorkspaceOS 5.2.5 — install-from-source that actually survives a new PC

This release is about the one-line source install. On a clean Windows machine it
failed in four separate places; each one is fixed in `tools/install-from-source.ps1`
and `build.ps1`, which is exactly what the one-line command fetches from `main`.
The app itself is unchanged from 5.2.4.

## Fixed in the from-source install

- **No NuGet package source.** A per-user .NET SDK installed by `dotnet-install`
  can arrive with no package source at all, so restore failed with `NU1100` even
  though nuget.org was reachable. The script now checks `dotnet nuget list source`
  and adds `https://api.nuget.org/v3/index.json` itself before building.
- **git's own progress output aborted the build.** Under
  `$ErrorActionPreference = 'Stop'`, PowerShell 5.1 turns a native command's
  stderr into a terminating error, and `git clone`/`git pull` report progress
  there — the script died before compiling anything. Both calls now run under
  `Continue`, are judged only by exit code, and use `--quiet` so no red
  `NativeCommandError` text appears mid-install.
- **The AutoHotkey download 404'd.** The URL tried first
  (`autohotkey.com/download/2.0.27/…`) does not exist; GitHub's release zip is now
  tried first, so the bundled runtime actually arrives.
- **Test packages missing no longer kills the build.** If the test project cannot
  restore its packages, `build.ps1` warns and continues to publish the app. A real
  test failure still fails the build.
- **Start at login is part of the install.** The script registers the delayed
  `WorkspaceOS Startup` logon task itself, beside the Run key, so a pasted
  one-liner needs no shell quoting of its own — and the app is single-instance, so
  the two never fight.
- **The script no longer calls `exit`.** It is pasted into a live PowerShell
  window; `exit` would have closed that window. Failures now `throw` instead.

## Unchanged from 5.2.4

- Bar dropdowns dismiss on a click anywhere else, and the owning pill still
  toggles normally.
- The update notice leads with the release's `Setup.exe` download; the from-source
  command is the labelled fallback.
- Sheet metric refresh stays on background priority and skips ticks while hidden.

## Installing

Download **WorkspaceOS-Setup.exe** (per-user, no admin, bundles the AutoHotkey
runtime) or the **portable zip**. The installer is unsigned, so Windows SmartScreen
asks once: **More info → Run anyway**. Building from source needs no admin and
skips that prompt, but it compiles, so it takes a few minutes.

The themed taskbar and Explorer glass need [Windhawk](https://windhawk.net) plus
its **Windows 11 Taskbar Styler** and **Windows 11 File Explorer Styler** mods,
installed from Windhawk's own mod list (it has no command-line installer). After
that, one elevated run of `tools\apply-windhawk-taskbar.ps1` — shipped in the
installer and the zip — applies the theme's glass and registers the task that
keeps it following every theme change. Without Windhawk the app is unchanged.
