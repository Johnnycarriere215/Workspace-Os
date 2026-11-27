# WorkspaceOS — install from source.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/Johnnycarriere215/Workspace-Os/main/tools/install-from-source.ps1 | iex"
#
# Builds the app from the repository on this machine and installs it per-user.
# Nothing prebuilt is downloaded and nothing is run with a Mark-of-the-Web, so
# there is no SmartScreen "unknown publisher" prompt: the exe is compiled here
# from the source you can read.
#
# Everything the app needs to work on a fresh machine is handled:
#   - the .NET 8 SDK, installed per user when it is missing (no admin);
#   - AutoHotkey v2, fetched by build.ps1 and embedded in the installer;
#   - cava, the bar's audio visualiser, fetched from its own release and unpacked
#     to %LOCALAPPDATA%\cava — the first place the app looks for it.
# Optional glass (taskbar + File Explorer blur) needs Windhawk and its styler
# mods; that is a system-wide tool and is never installed automatically.
#
# If WorkspaceOS is already installed this reports the installed version and stops;
# when a newer release exists it says so and prints the command that updates it.
# Pass -Force to rebuild and reinstall over the current copy.
#
# Writes only as your user: %LOCALAPPDATA% (source, install, cava), %TEMP% (SDK,
# AutoHotkey and cava downloads), plus the usual HKCU Run entry and Start-menu
# shortcut. Stops the running copy before installing.
#
# Options (only when run as a file, e.g. .\install-from-source.ps1 -Force):
#   -Root <dir>     where the source is checked out   (default %LOCALAPPDATA%\WorkspaceOS\source)
#   -Branch <name>  branch to build                   (default main)
#   -SkipInstall    build only, leave the app in <Root>\publish
#   -NoLaunch       install but do not start the app
#   -Force          rebuild and reinstall even if it is already installed

param(
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'WorkspaceOS\source'),
    [string]$Branch = 'main',
    [string]$Repo = 'https://github.com/Johnnycarriere215/Workspace-Os.git',
    [string]$CavaMsi = 'https://github.com/karlstav/cava/releases/download/1.0.0/cava_win_x64_install.msi',
    [switch]$SkipInstall,
    [switch]$NoLaunch,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Say([string]$text) { Write-Host "== $text" -ForegroundColor Cyan }
# throw, not exit: this script is meant to be pasted straight into a PowerShell
# window, and exit would close the user's window on them.
function Fail([string]$text) { Write-Host "!! $text" -ForegroundColor Red; throw $text }

Say 'WorkspaceOS: install from source'

if (-not [Environment]::Is64BitOperatingSystem) { Fail 'WorkspaceOS needs 64-bit Windows 10 or 11.' }
$build = [Environment]::OSVersion.Version.Build
if ($build -lt 17763) { Fail "Windows 10 1809 (build 17763) or newer is required; this is build $build." }

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\WorkspaceOS'
$appPath = Join-Path $installDir 'WorkspaceOS.exe'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WorkspaceOS'

# ---- Already installed? ------------------------------------------------------
$installedVersion = ''
if (Test-Path $uninstallKey) {
    $entry = Get-ItemProperty -Path $uninstallKey -ErrorAction SilentlyContinue
    if ($entry -and $entry.DisplayVersion) { $installedVersion = [string]$entry.DisplayVersion }
}
if (-not $installedVersion -and (Test-Path $appPath)) {
    try { $installedVersion = (Get-Item $appPath).VersionInfo.FileVersion } catch { }
}

# Newest published release: drives the update notice below and says whether an
# existing install is current. A failure here is not fatal - the rest still works.
$latestVersion = ''
$updateCommand = 'powershell -NoProfile -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/Johnnycarriere215/Workspace-Os/main/tools/install-from-source.ps1 -OutFile $env:TEMP\wos-install.ps1; & $env:TEMP\wos-install.ps1 -Force"'
try {
    $tag = (Invoke-RestMethod -TimeoutSec 8 -UseBasicParsing `
        -Uri 'https://api.github.com/repos/Johnnycarriere215/Workspace-Os/releases/latest').tag_name
    if ($tag) { $latestVersion = ([string]$tag).TrimStart('v', 'V') }
} catch { }

if ($installedVersion -and -not $Force -and -not $SkipInstall) {
    $outdated = $false
    if ($latestVersion) {
        try { $outdated = ([version]$installedVersion) -lt ([version]$latestVersion) } catch { }
    }

    if ($outdated) {
        Say "WorkspaceOS $installedVersion is installed, and v$latestVersion is available."
        Write-Host "   Installed:  $installedVersion in $installDir"
        Write-Host "   Available:  v$latestVersion"
        Write-Host ("   Running:    " + $(if (Get-Process WorkspaceOS -ErrorAction SilentlyContinue) { 'yes' } else { 'no' }))
        Write-Host ''
        Write-Host '   Nothing was changed. To update, either download the release installer:'
        Write-Host "   https://github.com/Johnnycarriere215/Workspace-Os/releases/latest/download/WorkspaceOS-Setup.exe"
        Write-Host ''
        Write-Host '   or rebuild from source and reinstall over the current copy, keeping your'
        Write-Host '   settings (needs the .NET 8 SDK and a few minutes of compiling):'
        Write-Host "   $updateCommand"
        return
    }

    Say "WorkspaceOS $installedVersion is installed and up to date - nothing to do."
    Write-Host "   Location:  $installDir"
    Write-Host ("   Running:   " + $(if (Get-Process WorkspaceOS -ErrorAction SilentlyContinue) { 'yes' } else { 'no' }))
    if ($latestVersion) { Write-Host "   Newest release: v$latestVersion" }

    $cava = Join-Path $env:LOCALAPPDATA 'cava\cava.exe'
    Write-Host ("   cava:      " + $(if (Test-Path $cava) { 'installed' } else { 'not installed (the bar visualiser stays hidden)' }))

    Write-Host ''
    Write-Host '   Nothing was changed. To rebuild and reinstall anyway, run:'
    Write-Host "   $updateCommand"
    return
}

# ---- .NET 8 SDK (per-user install, no administrator needed) -------------------------
function Get-Dotnet {
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) {
        $sdks = & $onPath.Source --list-sdks 2>$null
        if ($sdks -match '(^|\s)8\.') { return $onPath.Source }
    }

    # build.ps1 also looks here, so a per-user SDK is found by both.
    $local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $local) {
        $sdks = & $local --list-sdks 2>$null
        if ($sdks -match '(^|\s)8\.') { return $local }
    }

    Say 'Installing the .NET 8 SDK for this user'
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 8.0 `
        -InstallDir (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet') | Out-Null
    if (-not (Test-Path $local)) { Fail 'The .NET 8 SDK install did not complete.' }
    return $local
}

$dotnet = Get-Dotnet
Say "Using $dotnet"
$env:PATH = (Split-Path $dotnet) + ';' + $env:PATH

# ---- Package source ----------------------------------------------------------
# A per-user SDK installed by dotnet-install can come up with no package source,
# and restore then fails with NU1100 ("unable to resolve") even though nuget.org
# is perfectly reachable. Register it before building.
function Ensure-NuGetSource([string]$dotnetPath) {
    try {
        $list = (& $dotnetPath nuget list source 2>$null | Out-String)
        if ($list -match 'api\.nuget\.org') { return 'nuget.org already configured' }
        & $dotnetPath nuget add source 'https://api.nuget.org/v3/index.json' -n 'nuget.org' 2>$null | Out-Null
        $list = (& $dotnetPath nuget list source 2>$null | Out-String)
        if ($list -match 'api\.nuget\.org') { return 'nuget.org added' }
        return 'nuget.org could not be added'
    } catch { return 'could not check package sources: ' + $_.Exception.Message }
}

$nugetSource = Ensure-NuGetSource $dotnet
Write-Host "   Package source: $nugetSource"

# ---- Source ------------------------------------------------------------------
if (Test-Path (Join-Path $Root 'build.ps1')) {
    Say "Updating the source in $Root"
    Push-Location $Root
    try {
        if (Get-Command git -ErrorAction SilentlyContinue) {
            # git reports progress on stderr, and PowerShell 5.1 turns a native
            # command's stderr into a terminating error under 'Stop'. Merge the
            # streams and trust only the exit code.
            $pull = (& git pull --ff-only 2>&1 | Out-String)
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "git pull failed; building what is already checked out."
            } elseif ($pull.Trim()) { Write-Host ($pull.Trim()) }
        }
    } finally { Pop-Location }
} else {
    New-Item -ItemType Directory -Force -Path $Root | Out-Null

    if (Get-Command git -ErrorAction SilentlyContinue) {
        Say "Cloning $Repo ($Branch) into $Root"
        # Same stderr-to-error trap as the pull above: merge, then check the code.
        $clone = (& git clone --depth 1 --branch $Branch $Repo $Root 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) { Fail ('git clone failed. ' + $clone.Trim()) }
        if ($clone.Trim()) { Write-Host ($clone.Trim()) }
    } else {
        # No git: GitHub's own source zip for the branch.
        $zip = Join-Path $env:TEMP 'workspaceos-source.zip'
        $zipRoot = Join-Path $env:TEMP 'workspaceos-source'
        Say 'Downloading the source zip'
        $url = 'https://codeload.github.com/Johnnycarriere215/Workspace-Os/zip/refs/heads/' + $Branch
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $zip
        if (Test-Path $zipRoot) { Remove-Item $zipRoot -Recurse -Force }
        Expand-Archive -Path $zip -DestinationPath $zipRoot -Force
        $inner = Get-ChildItem $zipRoot -Directory | Select-Object -First 1
        if (-not $inner) { Fail 'The source zip was empty.' }
        Copy-Item (Join-Path $inner.FullName '*') $Root -Recurse -Force
    }
}

if (-not (Test-Path (Join-Path $Root 'build.ps1'))) { Fail "No build.ps1 under $Root - the checkout looks wrong." }

# ---- Build (tests, publish, installer, portable zip) -------------------------
Say 'Building (tests run first, this takes a few minutes)'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'build.ps1')
if ($LASTEXITCODE -ne 0) { Fail 'The build failed; see the output above.' }

$app = Join-Path $Root 'publish\WorkspaceOS.exe'
$setup = Join-Path $Root 'publish\WorkspaceOS-Setup.exe'
if (-not (Test-Path $app)) { Fail 'The build produced no WorkspaceOS.exe.' }

if ($SkipInstall) {
    Say "Built. Run $app (nothing was installed)."
    return
}

# ---- cava, the bar visualiser ------------------------------------------------
function Ensure-Cava {
    $target = Join-Path $env:LOCALAPPDATA 'cava\cava.exe'
    if (Test-Path $target) { return "already installed ($target)" }

    $otherSpots = @(
        (Join-Path $env:ProgramFiles 'cava\cava.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'cava\cava.exe')
    )
    foreach ($spot in $otherSpots) {
        if ($spot -and (Test-Path $spot)) { return "already installed ($spot)" }
    }
    if (Get-Command cava -ErrorAction SilentlyContinue) { return 'already on PATH' }

    Say 'Installing cava (the bar visualiser)'
    $msi = Join-Path $env:TEMP 'cava_win_x64_install.msi'
    $unpack = Join-Path $env:TEMP 'cava-unpack'
    Invoke-WebRequest -UseBasicParsing -Uri $CavaMsi -OutFile $msi

    # Administrative install: unpacks the package without registering it and
    # without administrator rights. Its payload lands in LocalApp\cava\cava.exe,
    # which is exactly the per-user path the app probes first.
    if (Test-Path $unpack) { Remove-Item $unpack -Recurse -Force }
    & msiexec.exe /a $msi /qn "TARGETDIR=$unpack" | Out-Null

    $exe = Join-Path $unpack 'LocalApp\cava\cava.exe'
    for ($i = 0; $i -lt 40 -and -not (Test-Path $exe); $i++) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path $exe)) { throw 'cava.exe was not inside the downloaded MSI' }

    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item $exe $target -Force
    return "installed ($target)"
}

$cavaStatus = ''
try { $cavaStatus = Ensure-Cava } catch {
    $cavaStatus = 'could not be installed: ' + $_.Exception.Message
    Write-Warning "cava: $cavaStatus (the app still works; the visualiser stays hidden)"
}

# ---- Install per user --------------------------------------------------------
# Stop the running copy so the payload can be replaced (same graceful exit the
# installer uses: a stashed scratchpad window is restored, not orphaned).
try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'WorkspaceOS.Ctrl', [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(800)
    $bytes = [Text.Encoding]::UTF8.GetBytes("exit`n")
    $pipe.Write($bytes, 0, $bytes.Length)
    $pipe.Flush()
    Start-Sleep -Milliseconds 1500
    $pipe.Dispose()
} catch { }

Get-Process WorkspaceOS -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_ | Stop-Process -Force -ErrorAction SilentlyContinue } catch { }
}
Start-Sleep -Seconds 1

Say "Installing to $installDir"
if (Test-Path $setup) {
    # The installer built by build.ps1 is our own binary from this source.
    Start-Process -FilePath $setup -ArgumentList '/S' -Wait
} else {
    # No installer payload: lay the publish output out by hand, matching it.
    New-Item -ItemType Directory -Force -Path (Join-Path $installDir 'AutoHotkey') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $installDir 'tools') | Out-Null
    Copy-Item $app (Join-Path $installDir 'WorkspaceOS.exe') -Force
    $ahk = Join-Path $Root 'publish\AutoHotkey\AutoHotkey64.exe'
    if (Test-Path $ahk) { Copy-Item $ahk (Join-Path $installDir 'AutoHotkey\AutoHotkey64.exe') -Force }
    $glass = Join-Path $Root 'tools\apply-windhawk-taskbar.ps1'
    if (Test-Path $glass) { Copy-Item $glass (Join-Path $installDir 'tools\') -Force }

    $target = Join-Path $installDir 'WorkspaceOS.exe'
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $run -Name 'WorkspaceOS' -Value ('"' + $target + '"')
}

if (-not (Test-Path (Join-Path $installDir 'WorkspaceOS.exe'))) {
    Fail "The install did not produce $installDir\WorkspaceOS.exe."
}

if (-not $NoLaunch -and -not (Get-Process WorkspaceOS -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath (Join-Path $installDir 'WorkspaceOS.exe')
}

# ---- Start at login ----------------------------------------------------------
# The installer writes the Run entry, but Windows also keeps a per-entry
# enabled/disabled flag, and an entry left by an older copy can point at a file
# this install replaced. Re-assert the command and clear the flag, then read it
# back, so "starts with Windows" is a fact and not an assumption.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$installedExe = Join-Path $installDir 'WorkspaceOS.exe'
$autostart = 'no - it could not be registered'
try {
    if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }
    Set-ItemProperty -Path $runKey -Name 'WorkspaceOS' -Value ('"' + $installedExe + '"')
    Remove-ItemProperty -Path $approvedKey -Name 'WorkspaceOS' -ErrorAction SilentlyContinue
    $registered = (Get-ItemProperty -Path $runKey -Name 'WorkspaceOS' -ErrorAction Stop).WorkspaceOS
    if ($registered) { $autostart = "yes ($installedExe)" }
} catch {
    $autostart = 'no - ' + $_.Exception.Message
    Write-Warning "Autostart could not be registered: $($_.Exception.Message)"
}

# ---- Start at login, robustly ------------------------------------------------
# The Run entry alone is not always enough: Windows can leave it enabled and
# still not launch it at sign-in, and a shell that has only just started is not
# ready for the bar. A logon task with a short delay starts it either way, and
# the app is single-instance, so the two never fight. Kept in the script so the
# one-line install command does not need its own quoting to set this up.
$taskName = 'WorkspaceOS Startup'
$logonTask = 'not registered'
try {
    $act = New-ScheduledTaskAction -Execute $installedExe -WorkingDirectory $installDir
    $trg = New-ScheduledTaskTrigger -AtLogOn -User ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)
    $trg.Delay = 'PT20S'
    $set = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $taskName -Action $act -Trigger $trg -Settings $set `
        -Description 'Starts the WorkspaceOS bar at logon, beside the Run key.' -Force | Out-Null
    $logonTask = 'yes (logon task + Run key)'
} catch {
    $logonTask = 'no - ' + $_.Exception.Message
    Write-Warning "Logon task could not be registered: $($_.Exception.Message)"
}

Say "Installed to $installDir"
Write-Host "   App:       WorkspaceOS " + (Get-Item (Join-Path $installDir 'WorkspaceOS.exe')).VersionInfo.FileVersion
Write-Host "   AutoHotkey bundled by the installer"
Write-Host "   cava:      $cavaStatus"
Write-Host "   Autostart: $autostart"
Write-Host "   Logon task: $logonTask"
Write-Host '   Optional:  glass (taskbar + File Explorer blur) needs Windhawk with the'
Write-Host '              windows-11-taskbar-styler and windows-11-file-explorer-styler mods,'
Write-Host '              then tools\apply-windhawk-taskbar.ps1 elevated.'
Write-Host '  Uninstall: Settings > Apps > WorkspaceOS, or Uninstall.exe in that folder.'
