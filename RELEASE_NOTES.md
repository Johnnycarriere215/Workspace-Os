# WorkspaceOS 5.2.4 — click anywhere and the dropdown goes away

## Changed

- **Bar dropdowns dismiss on a click anywhere else.** The detail sheets that drop
  from the bar's pills (CPU, memory, GPU, disk, temperature, volume, network,
  battery, brightness, power, Bluetooth, quick access) now close when you click
  outside them. Before, they only closed when focus moved to another app — and
  because the bar never takes focus, clicking the bar itself, another pill or the
  desktop left the sheet open until you pressed Escape. Each sheet now watches the
  mouse and closes on the first click outside it, while a click on the pill that
  opened it still toggles as before.
- **Lighter metric refresh.** A sheet's once-a-second refresh runs at background
  priority and skips ticks while it is hidden or minimised, so a metric tick can no
  longer get in front of input or painting. Rendering, shadows and blur are
  untouched — this is a scheduling change, not a visual one.

Nothing else changes: the installer, the bar, the update notice and the logon
autostart behaviour from 5.2.3 are all the same.

## Installing

Download **WorkspaceOS-Setup.exe** (per-user, no admin, bundles the AutoHotkey
runtime) or the **portable zip**, or build it yourself with
`tools/install-from-source.ps1`. The installer is unsigned, so Windows SmartScreen
asks once: **More info → Run anyway**. Building from source skips that prompt and
needs no admin rights, but it compiles, so it takes a few minutes.

The themed taskbar and Explorer glass need [Windhawk](https://windhawk.net) plus
its **Windows 11 Taskbar Styler** and **Windows 11 File Explorer Styler** mods,
installed from Windhawk's own mod list (it has no command-line installer). After
that, one elevated run of `tools\apply-windhawk-taskbar.ps1` — shipped in the
installer and the zip — applies the theme's glass and registers the task that
keeps it following every theme change. Without Windhawk the app is unchanged.
