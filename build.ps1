# WorkspaceOS build script
# Produces:
#   publish\WorkspaceOS.exe        self-contained application
#   publish\WorkspaceOS-Setup.exe  self-extracting installer
#
# Requirements: .NET 8 SDK (dotnet on PATH or in %LOCALAPPDATA%\Microsoft\dotnet)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Locate dotnet
$dotnet = "dotnet"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $local = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (Test-Path $local) { $dotnet = $local }
    else { throw ".NET SDK not found. Install from https://dot.net" }
}

Write-Host "== Publishing WorkspaceOS (Release, self-contained, single file) ==" -ForegroundColor Cyan
& $dotnet publish "$root\src\WorkspaceOS\WorkspaceOS.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$root\publish" --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "== Building installer ==" -ForegroundColor Cyan
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "In-box C# compiler not found" }

& $csc /nologo /target:winexe /optimize+ `
    /out:"$root\publish\WorkspaceOS-Setup.exe" `
    /resource:"$root\publish\WorkspaceOS.exe",WorkspaceOS.exe `
    /reference:System.Windows.Forms.dll /reference:System.dll `
    "$root\installer\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw "installer compilation failed" }

Write-Host "== Done ==" -ForegroundColor Green
Get-ChildItem "$root\publish\*.exe" | ForEach-Object {
    Write-Host ("  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB))
}
