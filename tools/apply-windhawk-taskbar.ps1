# Enables Windhawk's Windows 11 Taskbar Styler and applies the Shibumi/rice taskbar.
# Run elevated. Windhawk stores mod state per machine under
# HKLM\SOFTWARE\Windhawk\Engine\Mods\<id>, so this needs administrator rights.
$ErrorActionPreference = 'Stop'

$id = 'windows-11-taskbar-styler'
$root = "HKLM:\SOFTWARE\Windhawk\Engine\Mods"
$modKey = Join-Path $root $id
$settingsKey = Join-Path $modKey 'Settings'
$windhawk = 'C:\Program Files\Windhawk\windhawk.exe'
$backupDir = Join-Path $env:ProgramData 'WorkspaceOS\windhawk-backup'
$backup = Join-Path $backupDir 'Engine-Mods-before-workspaceos.reg'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script elevated.' }

New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
if (-not (Test-Path $backup)) {
    & reg.exe export 'HKLM\SOFTWARE\Windhawk\Engine\Mods' $backup /y | Out-Null
    Write-Host "backup: $backup"
}

New-Item -Path $modKey -Force | Out-Null
New-Item -Path $settingsKey -Force | Out-Null

function Set-String([string]$name, [string]$value) {
    New-ItemProperty -Path $modKey -Name $name -Value $value -PropertyType String -Force | Out-Null
}
function Set-SettingString([string]$name, [string]$value) {
    New-ItemProperty -Path $settingsKey -Name $name -Value $value -PropertyType String -Force | Out-Null
}

# State: enabled, explorer only, 64-bit engine.
New-ItemProperty -Path $modKey -Name 'Disabled' -Value 0 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $modKey -Name 'SettingsChangeTime' -Value ([int][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -PropertyType DWord -Force | Out-Null
Set-String 'Include' 'explorer.exe'
Set-String 'Architecture' 'x86-64'
Set-String 'Version' '1.11.2'
# Windhawk resolves the compiled library from this value; the installed mod is
# already compiled in ProgramData, so only the pointer and the state are needed.
Set-String 'LibraryFileName' 'windows-11-taskbar-styler_1.11.2_593924.dll'

# The app regenerates this JSON on every theme change; consume it so the
# taskbar really follows the bar instead of re-applying a stale hard-coded tint.
$presetPath = Join-Path $env:APPDATA 'WorkspaceOS\taskbar\windows-11-taskbar-styler.registry.json'
$theme = 'TranslucentTaskbar'
$clickThrough = 0
$resource = 'CommonBgBrush=<WindhawkBlur BlurAmount="20" TintColor="#1F1F28" TintOpacity="0.55" TintLuminosityOpacity="0.70" FallbackColor="#1F1F28"/>'
if (Test-Path $presetPath) {
    $preset = Get-Content -Raw -LiteralPath $presetPath | ConvertFrom-Json
    if ($preset.theme) { $theme = [string]$preset.theme }
    if ($null -ne $preset.clickThroughTaskbar) { $clickThrough = [int]$preset.clickThroughTaskbar }
    if ($preset.'themeResourceVariables[0]') { $resource = [string]$preset.'themeResourceVariables[0]' }
}

Set-SettingString 'theme' $theme
New-ItemProperty -Path $settingsKey -Name 'clickThroughTaskbar' -Value $clickThrough -PropertyType DWord -Force | Out-Null
Set-SettingString 'themeResourceVariables[0]' $resource

# Remove any previously applied flat control-style values from WorkspaceOS so
# the theme is the single owner of the taskbar surface.
Get-Item -Path $settingsKey | Select-Object -ExpandProperty Property |
    Where-Object { $_ -like 'controlStyles*' -or $_ -like 'themeResourceVariables*' } |
    ForEach-Object { Remove-ItemProperty -Path $settingsKey -Name $_ -ErrorAction SilentlyContinue }
Set-SettingString 'themeResourceVariables[0]' $resource

Write-Host 'state:'
Get-ItemProperty -Path $modKey |
    Select-Object Disabled, Include, Architecture, Version, LibraryFileName | Format-List
Write-Host 'settings:'
Get-ItemProperty -Path $settingsKey |
    Select-Object theme, clickThroughTaskbar, 'themeResourceVariables[0]' | Format-List

if (Test-Path $windhawk) {
    Write-Host 'restarting the Windhawk engine...'
    Start-Process -FilePath $windhawk -ArgumentList '-restart' -WindowStyle Hidden
    Start-Sleep -Seconds 10
} else {
    Write-Warning "Windhawk not found at $windhawk"
}

Write-Host 'explorer modules:'
& tasklist.exe /m /fi 'imagename eq explorer.exe' |
    Select-String -Pattern 'taskbar-styler|explorer.exe' |
    ForEach-Object { $_.Line.Trim() }
