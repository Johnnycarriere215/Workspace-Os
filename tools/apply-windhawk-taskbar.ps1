# Applies the Shibumi glass presets to Windhawk's shell styler mods.
# Run elevated once. Windhawk stores mod state per machine under
# HKLM\SOFTWARE\Windhawk\Engine\Mods\<id>, so this needs administrator rights.
#
# Install the mods first, from Windhawk's own mod list:
#   - "Windows 11 Taskbar Styler"       (windows-11-taskbar-styler)
#   - "Windows 11 File Explorer Styler" (windows-11-file-explorer-styler)
# Windhawk has no command-line installer, so it cannot do this part; a mod that
# is not installed is skipped here and its glass stays staged in %APPDATA%.
#
# Besides applying the current theme, this registers the highest-privileges
# "WorkspaceOS Taskbar Preset" task. WorkspaceOS runs it whenever the theme
# changes, so the taskbar and File Explorer keep following the bar without
# another UAC prompt.
#
# Windhawk re-reads a mod's settings when SettingsChangeTime changes, so no
# engine restart is needed - pass -Restart only to (re)inject a mod into a
# running Explorer, which is a one-time cost when setting up.
param([switch]$Restart)

$ErrorActionPreference = 'Stop'

$root = 'HKLM:\SOFTWARE\Windhawk\Engine\Mods'
$taskbarId = 'windows-11-taskbar-styler'
$explorerId = 'windows-11-file-explorer-styler'
$modKey = Join-Path $root $taskbarId
$settingsKey = Join-Path $modKey 'Settings'
$dllDir = 'C:\ProgramData\Windhawk\Engine\Mods\64'
$windhawk = 'C:\Program Files\Windhawk\windhawk.exe'
$backupDir = Join-Path $env:ProgramData 'WorkspaceOS\windhawk-backup'
$backup = Join-Path $backupDir 'Engine-Mods-before-workspaceos.reg'
$taskName = 'WorkspaceOS Taskbar Preset'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script elevated.' }

New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
if (-not (Test-Path $backup)) {
    & reg.exe export 'HKLM\SOFTWARE\Windhawk\Engine\Mods' $backup /y | Out-Null
    Write-Host "backup: $backup"
}

function Set-Dword([string]$path, [string]$name, [int]$value) {
    New-ItemProperty -Path $path -Name $name -Value $value -PropertyType DWord -Force | Out-Null
}
function Set-String([string]$path, [string]$name, [string]$value) {
    New-ItemProperty -Path $path -Name $name -Value $value -PropertyType String -Force | Out-Null
}

# Writes one mod's staged preset. Mods the user has not installed are skipped:
# creating their key would leave a broken entry in Windhawk's mod list.
#
# Order matters. New values go in first and stale array entries are pruned
# afterwards, because on a settings change the File Explorer styler
# re-initialises its XAML hooks inside every open Explorer window; if it reads a
# half-written set, a style can reference a constant that is not there and the
# window fails while processing a message ("explorer.exe - System Error").
function Apply-Preset([string]$modId, [string]$presetPath, [string[]]$stalePrefixes) {
    $key = Join-Path $root $modId
    $settings = Join-Path $key 'Settings'

    $library = (Get-ItemProperty -Path $key -ErrorAction SilentlyContinue).LibraryFileName
    if (-not $library) { Write-Host "$modId : not installed, skipped"; return }
    if (-not (Test-Path $presetPath)) { Write-Warning "$modId : no staged preset at $presetPath"; return }

    New-Item -Path $settings -Force | Out-Null
    $preset = Get-Content -Raw -LiteralPath $presetPath | ConvertFrom-Json

    $written = New-Object System.Collections.Generic.List[string]
    foreach ($property in $preset.PSObject.Properties) {
        $written.Add($property.Name)
        if ($property.Name -eq 'clickThroughTaskbar') {
            Set-Dword $settings $property.Name ([int]$property.Value)
        } else {
            Set-String $settings $property.Name ([string]$property.Value)
        }
    }

    # Prune only what a longer, older preset left behind - after the write.
    $existing = (Get-Item -Path $settings).Property
    foreach ($name in $existing) {
        if ($written -contains $name) { continue }
        foreach ($prefix in $stalePrefixes) {
            if ($name.StartsWith($prefix)) {
                Remove-ItemProperty -Path $settings -Name $name -ErrorAction SilentlyContinue
                break
            }
        }
    }

    # Announce the change last, once the set is complete.
    Set-Dword $key 'SettingsChangeTime' ([int][DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
    Write-Host "$modId : preset applied"
}

# Windhawk compiles each mod to <id>_<version>_<build>.dll, and the build number
# is per machine - never hardcode it. Keep the library Windhawk already points
# at when it is still on disk (that is the build it activated); only fall back to
# the newest compiled one on a machine where the mod was never registered, since
# a newer file may be an update Windhawk has not switched to yet.
$existingLibrary = (Get-ItemProperty -Path $modKey -ErrorAction SilentlyContinue).LibraryFileName
$existingPath = if ($existingLibrary) { Join-Path $dllDir $existingLibrary } else { $null }

if ($existingPath -and (Test-Path $existingPath)) {
    $taskbarDll = Get-Item $existingPath
} else {
    $taskbarDll = Get-ChildItem -Path $dllDir -Filter "${taskbarId}_*.dll" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if ($taskbarDll) {
    $taskbarVersion = if ($taskbarDll.Name -match '_([0-9]+\.[0-9.]+)_') { $matches[1] } else { '1.0' }
    New-Item -Path $modKey -Force | Out-Null
    Set-Dword $modKey 'Disabled' 0
    Set-String $modKey 'Include' 'explorer.exe'
    Set-String $modKey 'Architecture' 'x86-64'
    Set-String $modKey 'Version' $taskbarVersion
    Set-String $modKey 'LibraryFileName' $taskbarDll.Name
    Write-Host "taskbar styler: v$taskbarVersion ($($taskbarDll.Name))"
} else {
    Write-Warning ("Windows 11 Taskbar Styler is not installed in Windhawk - install it from " +
        "Windhawk's mod list, then run this script again. Its glass stays staged meanwhile.")
}

$appData = Join-Path $env:APPDATA 'WorkspaceOS'
Apply-Preset $taskbarId (Join-Path $appData 'taskbar\windows-11-taskbar-styler.registry.json') @('controlStyles[')
Apply-Preset $explorerId (Join-Path $appData 'explorer\windows-11-file-explorer-styler.registry.json') @('styleConstants[')

if ($taskbarDll) {
    Write-Host 'taskbar styler state:'
    Get-ItemProperty -Path $modKey |
        Select-Object Disabled, Include, Architecture, Version, LibraryFileName | Format-List
}

# One-time setup for automatic theme changes: a highest-privileges task that
# re-runs this script. WorkspaceOS starts it by name (schtasks /run), which does
# not prompt, so the glass follows every theme without elevation each time.
#
# Queue, not IgnoreNew: switching themes twice in quick succession must not drop
# the newer run, or the glass would stay on the previous theme's tint.
$taskAction = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument (
    '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"')
$taskTrigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$taskPrincipal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType Interactive -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet -MultipleInstances Queue `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -Action $taskAction -Trigger $taskTrigger `
    -Principal $taskPrincipal -Settings $taskSettings -Force `
    -Description 'Applies the WorkspaceOS theme glass to the taskbar and File Explorer.' | Out-Null
Write-Host "task: $taskName registered"

if ($Restart) {
    if (Test-Path $windhawk) {
        Write-Host 'restarting the Windhawk engine to inject the mods...'
        Start-Process -FilePath $windhawk -ArgumentList '-restart' -WindowStyle Hidden
        Start-Sleep -Seconds 10
    } else {
        Write-Warning "Windhawk not found at $windhawk"
    }
}

Write-Host 'loaded modules:'
$modules = & tasklist.exe /m /fi 'imagename eq explorer.exe' | Out-String
foreach ($id in $taskbarId, $explorerId) {
    if ($modules -match [regex]::Escape($id)) { Write-Host "  $id loaded" }
    else { Write-Host "  $id NOT loaded" }
}
