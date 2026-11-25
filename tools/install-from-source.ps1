# WorkspaceOS — install from source.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/Johnnycarriere215/Workspace-Os/main/tools/install-from-source.ps1 | iex"
#
# Builds the app from the repository on this machine and installs it per-user.
# Nothing prebuilt is downloaded and nothing is run with a Mark-of-the-Web, so
# there is no SmartScreen "unknown publisher" prompt: the exe is compiled here
# from the source you can read.
#
# Needs 64-bit Windows 10/11. The .NET 8 SDK is installed per-user (no admin) if
# it is missing, and git is used when present (GitHub's source zip otherwise).
# Writes only as your user: %LOCALAPPDATA% (source + install), %TEMP% (SDK and
# AutoHotkey downloads), plus the usual HKCU Run entry and Start-menu shortcut.
# Stops the running copy before installing.
#
# Options (only when run as a file, e.g. .\install-from-source.ps1 -SkipInstall):
#   -Root <dir>     where the source is checked out   (default %LOCALAPPDATA%\WorkspaceOS\source)
#   -Branch <name>  branch to build                   (default main)
#   -SkipInstall    build only, leave the app in <Root>\publish
#   -NoLaunch       install but do not start the app

param(
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'WorkspaceOS\source'),
    [string]$Branch = 'main',
    [string]$Repo = 'https://github.com/Johnnycarriere215/Workspace-Os.git',
    [switch]$SkipInstall,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

function Say([string]$text) { Write-Host "== $text" -ForegroundColor Cyan }
function Fail([string]$text) { Write-Host "!! $text" -ForegroundColor Red; exit 1 }

Say 'WorkspaceOS: install from source'

if (-not [Environment]::Is64BitOperatingSystem) { Fail 'WorkspaceOS needs 64-bit Windows 10 or 11.' }
$build = [Environment]::OSVersion.Version.Build
if ($build -lt 17763) { Fail "Windows 10 1809 (build 17763) or newer is required; this is build $build." }

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

# ---- Source ------------------------------------------------------------------
if (Test-Path (Join-Path $Root 'build.ps1')) {
    Say "Updating the source in $Root"
    Push-Location $Root
    try {
        if (Get-Command git -ErrorAction SilentlyContinue) {
            & git pull --ff-only 2>$null
            if ($LASTEXITCODE -ne 0) { Write-Warning "git pull failed; building what is already checked out." }
        }
    } finally { Pop-Location }
} else {
    New-Item -ItemType Directory -Force -Path $Root | Out-Null

    if (Get-Command git -ErrorAction SilentlyContinue) {
        Say "Cloning $Repo ($Branch) into $Root"
        & git clone --depth 1 --branch $Branch $Repo $Root
        if ($LASTEXITCODE -ne 0) { Fail 'git clone failed.' }
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
    exit 0
}

# ---- Install per user --------------------------------------------------------
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\WorkspaceOS'

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

Say "Installed to $installDir"
Write-Host '  Uninstall: Settings > Apps > WorkspaceOS, or Uninstall.exe in that folder.'
Write-Host '  Optional glass: install the Windhawk styler mods, then run tools\apply-windhawk-taskbar.ps1 elevated.'
