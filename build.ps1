# WorkspaceOS build script
# Produces:
#   publish\WorkspaceOS.exe              self-contained application
#   publish\WorkspaceOS-Setup.exe        self-extracting installer (bundles the AutoHotkey runtime)
#   publish\WorkspaceOS-2.0.0-portable.zip  portable layout (exe + AutoHotkey runtime)
#   publish\AutoHotkey\AutoHotkey64.exe  AutoHotkey v2 runtime (downloaded on demand, committed-free)
#
# Requirements: .NET 8 SDK (dotnet on PATH or in %LOCALAPPDATA%\Microsoft\dotnet)

param(
    [string]$AhkVersion = "2.0.27"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Locate dotnet
$dotnet = "dotnet"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $local = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (Test-Path $local) { $dotnet = $local }
    else { throw ".NET SDK not found. Install from https://dot.net" }
}

# Read the version from the csproj
[xml]$csproj = Get-Content "$root\src\WorkspaceOS\WorkspaceOS.csproj"
$version = $csproj.Project.PropertyGroup.Version
Write-Host "== WorkspaceOS v$version ==" -ForegroundColor Cyan

# ---- AutoHotkey v2 runtime (bundled so users never install AHK themselves) ----
$ahkDir = "$root\publish\AutoHotkey"
$ahkExe = "$ahkDir\AutoHotkey64.exe"
if (-not (Test-Path $ahkExe)) {
    $zip = "$env:TEMP\AutoHotkey_$AhkVersion.zip"
    if (-not (Test-Path $zip)) {
        Write-Host "== Downloading AutoHotkey v$AhkVersion ==" -ForegroundColor Cyan
        $urls = @(
            "https://www.autohotkey.com/download/$AhkVersion/AutoHotkey_$AhkVersion.zip",
            "https://github.com/AutoHotkey/AutoHotkey/releases/download/v$AhkVersion/AutoHotkey_$AhkVersion.zip"
        )
        foreach ($u in $urls) {
            try { Invoke-WebRequest -Uri $u -OutFile $zip -ErrorAction Stop; break }
            catch { Write-Warning "download failed: $u" }
        }
        if (-not (Test-Path $zip)) { throw "AutoHotkey runtime download failed" }
    }
    Write-Host "== Extracting AutoHotkey runtime ==" -ForegroundColor Cyan
    Expand-Archive -Path $zip -DestinationPath "$env:TEMP\AutoHotkey_$AhkVersion" -Force
    New-Item -ItemType Directory -Force -Path $ahkDir | Out-Null
    Copy-Item "$env:TEMP\AutoHotkey_$AhkVersion\AutoHotkey64.exe" $ahkExe -Force
}

# ---- Tests ----
Write-Host "== Running tests ==" -ForegroundColor Cyan
& $dotnet test "$root\tests\WorkspaceOS.Tests\WorkspaceOS.Tests.csproj" -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

# ---- Publish app ----
Write-Host "== Publishing WorkspaceOS (Release, self-contained, single file) ==" -ForegroundColor Cyan
& $dotnet publish "$root\src\WorkspaceOS\WorkspaceOS.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$root\publish" --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# ---- Installer (dual payload) ----
Write-Host "== Building installer ==" -ForegroundColor Cyan
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "In-box C# compiler not found" }

# Response file: csc reads args from disk, immune to shell quoting/continuation quirks.
# Here-string + interpolation only — no concatenation operators.
$rsp = "$root\publish\installer.rsp"
@"
/nologo
/target:winexe
/optimize+
/out:$root\publish\WorkspaceOS-Setup.exe
/resource:$root\publish\WorkspaceOS.exe,WorkspaceOS.exe
/resource:$root\publish\AutoHotkey\AutoHotkey64.exe,AutoHotkey64.exe
/reference:System.Windows.Forms.dll
/reference:System.dll
$root\installer\Setup.cs
"@ | Set-Content -Path $rsp -Encoding utf8

& $csc "@$rsp"
if ($LASTEXITCODE -ne 0) { throw "installer compilation failed" }
Remove-Item $rsp -ErrorAction SilentlyContinue

# ---- Portable zip ----
Write-Host "== Building portable zip ==" -ForegroundColor Cyan
$portableDir = "$env:TEMP\WorkspaceOS-portable"
if (Test-Path $portableDir) { Remove-Item $portableDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$portableDir\AutoHotkey" | Out-Null
Copy-Item "$root\publish\WorkspaceOS.exe" "$portableDir\" -Force
Copy-Item "$root\publish\AutoHotkey\AutoHotkey64.exe" "$portableDir\AutoHotkey\" -Force
Copy-Item "$root\README.md" "$portableDir\README.txt" -Force
Compress-Archive -Path "$portableDir\*" -DestinationPath "$root\publish\WorkspaceOS-$version-portable.zip" -Force

Write-Host "== Done ==" -ForegroundColor Green
Get-ChildItem "$root\publish\WorkspaceOS*" | ForEach-Object {
    Write-Host ("  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB))
}
