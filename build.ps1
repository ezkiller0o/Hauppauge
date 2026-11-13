<#
.SYNOPSIS
    Builds "Hauppauge Live Capture Studio" as a self-contained, single-file EXE for Windows 10/11 x64
    and (optionally) places ffmpeg.exe next to it.

.DESCRIPTION
    Needs only the .NET 8 SDK (or newer) on the build machine. The resulting EXE needs NO .NET installation.

    Output: dist\Hauppauge Live Capture Studio\Hauppauge Live Capture Studio.exe

.PARAMETER SkipFfmpeg
    Do not download ffmpeg.exe. You must then copy ffmpeg.exe next to the EXE yourself
    (or into an "ffmpeg" sub-folder), or select it in the program's Settings.

.PARAMETER Clean
    Delete the previous output folder first.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipFfmpeg,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\HauppaugeLiveCaptureStudio\HauppaugeLiveCaptureStudio.csproj'
$out     = Join-Path $root 'dist\Hauppauge Live Capture Studio'
$exeName = 'Hauppauge Live Capture Studio.exe'

Write-Host ''
Write-Host '=== Hauppauge Live Capture Studio : build ===' -ForegroundColor Cyan

# 1. .NET SDK present?
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'The .NET SDK was not found. Install the .NET 8 SDK (x64) from https://dotnet.microsoft.com/download and run this script again.'
}
$sdkText = (& dotnet --version).Trim()
$sdkVersion = [version]($sdkText -replace '-.*$', '')
if ($sdkVersion -lt [version]'8.0') {
    throw "The installed .NET SDK is $sdkText. Version 8.0 or newer is required."
}
Write-Host ".NET SDK: $sdkText"

# 2. publish
if ($Clean -and (Test-Path $out)) {
    Write-Host 'Cleaning previous output...'
    Remove-Item $out -Recurse -Force
}

Write-Host 'Publishing (self-contained, win-x64, single file)...'
& dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit code $LASTEXITCODE). Read the error messages above."
}

$exe = Join-Path $out $exeName
if (-not (Test-Path $exe)) {
    throw "The build finished but '$exeName' was not found in $out."
}

# 3. documentation next to the EXE
foreach ($doc in 'README.md', 'README.ar.md', 'TESTING.md') {
    $src = Join-Path $root $doc
    if (Test-Path $src) { Copy-Item $src $out -Force }
}

# 4. ffmpeg (needed for MP4/H.264 recording only; live view and snapshots work without it)
if ($SkipFfmpeg) {
    Write-Host 'Skipping the ffmpeg download (-SkipFfmpeg). Recording needs ffmpeg.exe next to the EXE.' -ForegroundColor Yellow
}
else {
    try {
        & (Join-Path $root 'tools\fetch-ffmpeg.ps1') -Destination (Join-Path $out 'ffmpeg')
    }
    catch {
        Write-Warning "ffmpeg could not be downloaded: $($_.Exception.Message)"
        Write-Warning 'The EXE is still fine. Copy ffmpeg.exe into the "ffmpeg" folder next to it (see README.md).'
    }
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ''
Write-Host 'Build finished.' -ForegroundColor Green
Write-Host "  EXE : $exe  ($size MB)"
Write-Host '  The EXE is self-contained: no separate .NET installation is needed on the target PC.'
Write-Host ''
